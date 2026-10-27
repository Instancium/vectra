namespace Instancium.Vectra.Indexing.Pipeline;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Threading;

public sealed class IndexingPipeline : IIndexingPipeline
{
    private readonly ITextExtractor _extractor;
    private readonly ITextChunker _chunker;
    private readonly IInferenceEngine _engine;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<IndexingPipeline> _logger;

    public IndexingPipeline(
        ITextExtractor extractor,
        ITextChunker chunker,
        IInferenceEngine engine,
        IVectorStore vectorStore,
        ILogger<IndexingPipeline>? logger = null)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? NullLogger<IndexingPipeline>.Instance;
    }

    public async Task<IndexingPipelineResult> RunAsync(
        IndexingPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var totalStopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var initialWorkingSet = process.WorkingSet64;

        // 1. Readiness probe: GetEmbeddingDimensionAsync
        int dimension;
        try
        {
            dimension = await _engine.GetEmbeddingDimensionAsync(request.RepoId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IndexingFailedException(
                $"Gateway readiness probe failed for repo '{request.RepoId}'.", ex);
        }

        if (dimension <= 0)
        {
            throw new IndexingFailedException(
                $"Gateway returned invalid dimension {dimension} for repo '{request.RepoId}'. " +
                "Model may not be ready for embedding workloads.");
        }

        _logger.LogInformation(
            "Gateway ready. Repo={RepoId}, Dimension={Dimension}",
            request.RepoId, dimension);

        // 2. Extract
        var extractionStopwatch = Stopwatch.StartNew();
        var pages = await _extractor.ExtractAsync(
            new TextExtractionRequest(request.PdfPath, request.PageFrom, request.PageTo),
            cancellationToken);
        extractionStopwatch.Stop();

        _logger.LogInformation(
            "Extraction completed. Pages={PageCount}, Duration={Duration}ms",
            pages.Count, extractionStopwatch.ElapsedMilliseconds);

        // 3. Chunk
        var chunkingStopwatch = Stopwatch.StartNew();
        var chunks = _chunker.Chunk(pages, request.PdfPath);
        chunkingStopwatch.Stop();

        _logger.LogInformation(
            "Chunking completed. Chunks={ChunkCount}, Duration={Duration}ms",
            chunks.Count, chunkingStopwatch.ElapsedMilliseconds);

        // 4. Embed batch with bounded parallelism
        var embeddingStopwatch = Stopwatch.StartNew();
        var chunkIds = new long[chunks.Count];
        var fileId = 1L;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = request.MaxParallelEmbeddings,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            chunks.Select((chunk, index) => (chunk, index)),
            parallelOptions,
            async (item, token) =>
            {
                var (chunk, index) = item;
                token.ThrowIfCancellationRequested();

                var response = await _engine.GenerateEmbeddingsAsync(
                    request.RepoId,
                    new EmbeddingRequest(chunk.Text, Normalize: true),
                    token);

                var chunkId = index + 1;
                chunkIds[index] = chunkId;

                _vectorStore.Upsert(
                    chunkId,
                    fileId,
                    chunk.PageNumber,
                    chunk.Text,
                    response.Embedding);

                _logger.LogDebug(
                    "Embedded chunk {ChunkId}/{Total}. Page={Page}",
                    chunkId, chunks.Count, chunk.PageNumber);
            });

        embeddingStopwatch.Stop();
        totalStopwatch.Stop();

        // 5. Build manifest and metrics
        var manifest = new IndexManifest(
            request.PdfPath,
            "1.0.0",
            dimension,
            request.RepoId,
            DateTimeOffset.UtcNow,
            chunkIds);

        var peakWorkingSet = Math.Max(process.WorkingSet64, initialWorkingSet);

        var metrics = new IndexingMetrics
        {
            TotalPages = pages.Count,
            TotalChunks = chunks.Count,
            EmbeddingsGenerated = chunks.Count,
            ExtractionDuration = extractionStopwatch.Elapsed,
            ChunkingDuration = chunkingStopwatch.Elapsed,
            EmbeddingDuration = embeddingStopwatch.Elapsed,
            TotalDuration = totalStopwatch.Elapsed,
            PagesPerSecond = pages.Count > 0
                ? pages.Count / Math.Max(extractionStopwatch.Elapsed.TotalSeconds, 0.001)
                : 0,
            ChunksPerSecond = chunks.Count > 0
                ? chunks.Count / Math.Max(embeddingStopwatch.Elapsed.TotalSeconds, 0.001)
                : 0,
            PeakWorkingSetMb = peakWorkingSet / (1024 * 1024)
        };

        _logger.LogInformation(
            "Indexing completed. Chunks={Chunks}, Embeddings={Embeddings}, TotalDuration={Total}ms",
            chunks.Count, chunks.Count, totalStopwatch.ElapsedMilliseconds);

        return new IndexingPipelineResult(manifest, metrics);
    }
}