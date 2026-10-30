// tests/Instancium.Vectra.Tests.Unit/Indexing/IndexingPipelineTests.cs
#nullable enable
namespace Instancium.Vectra.Tests.Unit.Indexing;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Chunking;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Indexing.Extraction;
using Instancium.Vectra.Indexing.Incremental;
using Instancium.Vectra.Indexing.Pipeline;
using Instancium.Vectra.Indexing.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IndexingPipelineTests
{
    [Fact]
    public async Task IndexingPipeline_ShouldCallExtractorChunkerEngineStoreInOrder()
    {
        var recorder = new PipelineSequenceRecorder();
        var extractor = new StubTextExtractor(recorder);
        var chunker = new StubTextChunker(recorder);
        var engine = new StubInferenceEngine(recorder);
        var store = new StubVectorStore(recorder);
        var pipeline = new IndexingPipeline(
            extractor,
            chunker,
            engine,
            store,
            NullLogger<IndexingPipeline>.Instance);

        var request = new IndexingPipelineRequest(
            "sample.pdf",
            1,
            2,
            "repo-test",
            maxParallelEmbeddings: 2);

        var result = await pipeline.RunAsync(request);

        Assert.Equal(2, result.Metrics.TotalPages);
        Assert.Equal(1, extractor.Calls);
        Assert.Equal(1, chunker.Calls);
        Assert.Equal(1, engine.DimensionCalls);
        Assert.Equal(4, engine.GenerateCalls);
        Assert.Equal(4, store.UpsertCalls);
        Assert.Equal(4, result.Metrics.TotalChunks);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, store.UpsertedChunkIds.OrderBy(id => id).ToArray());

        List<string> sequence = recorder.Snapshot().ToList();
        


        Assert.Equal("dimension", sequence[0]);
        Assert.Equal("extract", sequence[1]);
        Assert.Equal("chunk", sequence[2]);

        List<string> embedded = sequence.Where(e => e.StartsWith("embed:", StringComparison.Ordinal)).ToList();
        List<string> upserted = sequence.Where(e => e.StartsWith("upsert:", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, embedded.Count);
        Assert.Equal(4, upserted.Count);
        Assert.Equal(
            embedded.Select(e => e["embed:".Length..]).OrderBy(t => t, StringComparer.Ordinal).ToArray(),
            upserted.Select(u => u["upsert:".Length..]).OrderBy(t => t, StringComparer.Ordinal).ToArray());


        foreach (string text in embedded.Select(e => e["embed:".Length..]))
        {
            int embedPosition = sequence.IndexOf($"embed:{text}");
            int upsertPosition = sequence.IndexOf($"upsert:{text}");
            Assert.True(embedPosition >= 0 && upsertPosition > embedPosition, $"Upsert preceded its embedding for '{text}'.");
        }
    }

    [Fact]
    public async Task IndexingPipeline_ShouldThrowIndexingFailedException_WhenGatewayNotReady()
    {
        var recorder = new PipelineSequenceRecorder();
        var pipeline = new IndexingPipeline(
            new StubTextExtractor(recorder),
            new StubTextChunker(recorder),
            new StubInferenceEngine(recorder, readyDimension: 0),
            new StubVectorStore(recorder),
            NullLogger<IndexingPipeline>.Instance);

        var request = new IndexingPipelineRequest("sample.pdf", 1, 1, "repo-test");

        await Assert.ThrowsAsync<IndexingFailedException>(() => pipeline.RunAsync(request));
    }

    [Fact]
    public async Task IndexingPipeline_ShouldRespectMaxParallelEmbeddings()
    {
        var recorder = new PipelineSequenceRecorder();
        var engine = new StubInferenceEngine(recorder, embeddingDelayMs: 20);
        var pipeline = new IndexingPipeline(
            new StubTextExtractor(recorder),
            new StubTextChunker(recorder),
            engine,
            new StubVectorStore(recorder),
            NullLogger<IndexingPipeline>.Instance);

        var request = new IndexingPipelineRequest(
            "sample.pdf",
            1,
            3,
            "repo-test",
            maxParallelEmbeddings: 3);

        var result = await pipeline.RunAsync(request);

        Assert.Equal(6, engine.GenerateCalls);
        Assert.Equal(6, result.Metrics.TotalChunks);
        Assert.True(engine.MaxParallelDegreeObserved <= 3, $"Parallelism limit violated: observed {engine.MaxParallelDegreeObserved}.");
        Assert.True(engine.MaxParallelDegreeObserved >= 2, $"Expected concurrent embeddings, observed {engine.MaxParallelDegreeObserved}.");
    }

    [Fact]
    public void IndexingPipelineRequest_ShouldValidateInputs()
    {
        Assert.ThrowsAny<ArgumentException>(() => new IndexingPipelineRequest(string.Empty, 1, 1, "repo"));
        Assert.ThrowsAny<ArgumentException>(() => new IndexingPipelineRequest("sample.pdf", 1, 1, string.Empty));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new IndexingPipelineRequest("sample.pdf", 2, 1, "repo"));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new IndexingPipelineRequest("sample.pdf", 1, 1, "repo", maxParallelEmbeddings: 0));
    }

    [Fact]
    public void AddVectraIndexing_ShouldRegisterCoreComponentsWithoutExternalContracts()
    {
        var services = new ServiceCollection();
        services.AddVectraIndexing();

        Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IInferenceEngine));
        Assert.DoesNotContain(services, sd => sd.ServiceType == typeof(IVectorStore));

        Assert.Contains(services, sd => sd.ServiceType == typeof(ITextExtractor));
        Assert.Contains(services, sd => sd.ServiceType == typeof(ITextChunker));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IIndexingPipeline));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IContentExtractor));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IContentExtractorRegistry));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IFileScanner));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IChangeClassifier));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IIncrementalIndexer));
    }

    [Fact]
    public void AddVectraIndexing_ShouldResolvePipelineWhenContractsProvided()
    {
        var recorder = new PipelineSequenceRecorder();
        var services = new ServiceCollection();
        services.AddSingleton<IInferenceEngine>(new StubInferenceEngine(recorder));
        services.AddSingleton<IVectorStore>(new StubVectorStore(recorder));
        services.AddVectraIndexing();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<IndexingPipeline>(provider.GetRequiredService<IIndexingPipeline>());
        Assert.IsType<ContentExtractorRegistry>(provider.GetRequiredService<IContentExtractorRegistry>());
        Assert.IsType<DirectoryFileScanner>(provider.GetRequiredService<IFileScanner>());
        Assert.IsType<ChangeClassifier>(provider.GetRequiredService<IChangeClassifier>());
    }

    internal sealed class PipelineSequenceRecorder
    {
        private readonly object _sync = new();
        private readonly List<string> _events = new();

        public void Record(string stage)
        {
            lock (_sync)
            {
                _events.Add(stage);
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }
    }

    private sealed class StubTextExtractor : ITextExtractor
    {
        private readonly PipelineSequenceRecorder _recorder;
        private int _calls;

        public StubTextExtractor(PipelineSequenceRecorder recorder) => _recorder = recorder;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<PageText>> ExtractAsync(TextExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _recorder.Record("extract");
            int pageCount = request.PageTo - request.PageFrom + 1;
            var pages = new List<PageText>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                int pageNumber = request.PageFrom + i;
                pages.Add(new PageText(pageNumber, $"page-{pageNumber}-text alpha beta gamma"));
            }

            return Task.FromResult<IReadOnlyList<PageText>>(pages);
        }
    }

    private sealed class StubTextChunker : ITextChunker
    {
        private readonly PipelineSequenceRecorder _recorder;
        private int _calls;

        public StubTextChunker(PipelineSequenceRecorder recorder) => _recorder = recorder;

        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages, string sourceFile)
        {
            Interlocked.Increment(ref _calls);
            _recorder.Record("chunk");
            return pages
                .SelectMany(page => new[]
                {
                    new TextChunk(page.Text, page.PageNumber, sourceFile),
                    new TextChunk(page.Text + "-tail", page.PageNumber, sourceFile)
                })
                .ToList();
        }
    }

    private sealed class StubInferenceEngine : IInferenceEngine
    {
        private readonly PipelineSequenceRecorder _recorder;
        private readonly int _readyDimension;
        private readonly int _embeddingDelayMs;
        private int _dimensionCalls;
        private int _generateCalls;
        private int _inFlight;
        private int _maxParallelObserved;

        public StubInferenceEngine(PipelineSequenceRecorder recorder, int readyDimension = 4, int embeddingDelayMs = 10)
        {
            _recorder = recorder;
            _readyDimension = readyDimension;
            _embeddingDelayMs = embeddingDelayMs;
        }

        public int DimensionCalls => Volatile.Read(ref _dimensionCalls);
        public int GenerateCalls => Volatile.Read(ref _generateCalls);
        public int MaxParallelDegreeObserved => Volatile.Read(ref _maxParallelObserved);

        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _dimensionCalls);
            _recorder.Record("dimension");
            return Task.FromResult(_readyDimension);
        }

        public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(string modelId, EmbeddingRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _generateCalls);
            ObserveConcurrency();
            _recorder.Record($"embed:{request.Text}");
            await Task.Delay(_embeddingDelayMs, ct);
            Interlocked.Decrement(ref _inFlight);
            float[] vector = { 0.1f, 0.2f, 0.3f, 0.4f };
            return new EmbeddingResponse(vector, vector.Length, vector.Length);
        }

        public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamDeltaGenerationAsync(
            string sessionId,
            ChatMessage deltaMessage,
            InferenceSettings? overrideSettings = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        private void ObserveConcurrency()
        {
            int current = Interlocked.Increment(ref _inFlight);
            int observed;
            while (current > (observed = Volatile.Read(ref _maxParallelObserved)))
            {
                if (Interlocked.CompareExchange(ref _maxParallelObserved, current, observed) == observed)
                {
                    break;
                }
            }
        }
    }

    private sealed class StubVectorStore : IVectorStore
    {
        private readonly PipelineSequenceRecorder _recorder;
        private readonly object _sync = new();
        private readonly List<ChunkMetadata> _metadata = new();
        private readonly List<long> _chunkIds = new();
        private int _upsertCalls;

        public StubVectorStore(PipelineSequenceRecorder recorder) => _recorder = recorder;

        public int UpsertCalls => Volatile.Read(ref _upsertCalls);

        public long[] UpsertedChunkIds
        {
            get
            {
                lock (_sync)
                {
                    return _chunkIds.ToArray();
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _metadata.Count;
                }
            }
        }

        public int Dimension => 4;

        public ReadOnlySpan<float> GetVectorsSpan() => ReadOnlySpan<float>.Empty;

        public IReadOnlyList<ChunkMetadata> GetMetadata()
        {
            lock (_sync)
            {
                return _metadata.ToArray();
            }
        }

        public void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector)
        {
            Interlocked.Increment(ref _upsertCalls);
            _recorder.Record($"upsert:{snippet}");
            lock (_sync)
            {
                _metadata.Add(new ChunkMetadata(chunkId, fileId, pageNumber, snippet));
                _chunkIds.Add(chunkId);
            }
        }
    }
}