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
using Instancium.Vectra.Indexing.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class IndexingPipelineTests
{
    [Fact]
    public async Task IndexingPipeline_ShouldCallExtractorChunkerEngineStoreInOrder()
    {
        var extractor = new StubTextExtractor();
        var chunker = new StubTextChunker();
        var engine = new StubInferenceEngine();
        var store = new StubVectorStore();
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
    }

    [Fact]
    public async Task IndexingPipeline_ShouldThrowIndexingFailedException_WhenGatewayNotReady()
    {
        var extractor = new StubTextExtractor();
        var chunker = new StubTextChunker();
        var engine = new StubInferenceEngine(readyDimension: 0);
        var store = new StubVectorStore();
        var pipeline = new IndexingPipeline(
            extractor,
            chunker,
            engine,
            store,
            NullLogger<IndexingPipeline>.Instance);

        var request = new IndexingPipelineRequest("sample.pdf", 1, 1, "repo-test");

        await Assert.ThrowsAsync<IndexingFailedException>(() => pipeline.RunAsync(request));
    }

    [Fact]
    public async Task IndexingPipeline_ShouldRespectMaxParallelEmbeddings()
    {
        var extractor = new StubTextExtractor();
        var chunker = new StubTextChunker();
        var engine = new StubInferenceEngine();
        var store = new StubVectorStore();
        var pipeline = new IndexingPipeline(
            extractor,
            chunker,
            engine,
            store,
            NullLogger<IndexingPipeline>.Instance);

        var request = new IndexingPipelineRequest(
            "sample.pdf",
            1,
            3,
            "repo-test",
            maxParallelEmbeddings: 3);

        await pipeline.RunAsync(request);

        Assert.Equal(3, engine.MaxParallelDegreeObserved);
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
    public void ServiceCollectionExtensions_ShouldRegisterPipelineComponents()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInferenceEngine>(new StubInferenceEngine());
        services.AddSingleton<IVectorStore>(new StubVectorStore());
        services.AddSingleton<ITextExtractor>(new StubTextExtractor());
        services.AddSingleton<ITextChunker>(new StubTextChunker());

        var provider = services.BuildServiceProvider();

        var extractor = provider.GetRequiredService<ITextExtractor>();
        var chunker = provider.GetRequiredService<ITextChunker>();

        Assert.NotNull(extractor);
        Assert.NotNull(chunker);
    }

    private sealed class StubTextExtractor : ITextExtractor
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<PageText>> ExtractAsync(TextExtractionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<PageText>>([
                new PageText(1, "alpha beta gamma"),
                new PageText(2, "delta epsilon zeta")
            ]);
        }
    }

    private sealed class StubTextChunker : ITextChunker
    {
        public int Calls { get; private set; }

        public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages, string sourceFile)
        {
            Calls++;
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
        private readonly int _readyDimension;
        private int _inFlight;

        public int DimensionCalls { get; private set; }

        public int GenerateCalls { get; private set; }

        public int MaxParallelDegreeObserved { get; private set; }

        public StubInferenceEngine(int readyDimension = 4)
        {
            _readyDimension = readyDimension;
        }

        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
        {
            DimensionCalls++;
            return Task.FromResult(_readyDimension);
        }

        public async Task<EmbeddingResponse> GenerateEmbeddingsAsync(string modelId, EmbeddingRequest request, CancellationToken ct = default)
        {
            GenerateCalls++;
            var currentInFlight = Interlocked.Increment(ref _inFlight);
            MaxParallelDegreeObserved = Math.Max(MaxParallelDegreeObserved, currentInFlight);
            await Task.Delay(10, ct);
            Interlocked.Decrement(ref _inFlight);

            var vector = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
            return new EmbeddingResponse(vector, vector.Length, vector.Length);
        }

        public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ApplyChatTemplateAsync(string modelId, IEnumerable<ChatMessage> messages, CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamDeltaGenerationAsync(string sessionId, ChatMessage deltaMessage, InferenceSettings? overrideSettings = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class StubVectorStore : IVectorStore
    {
        public int UpsertCalls { get; private set; }

        public int Count => 0;

        public int Dimension => 4;

        public ReadOnlySpan<float> GetVectorsSpan() => ReadOnlySpan<float>.Empty;

        public IReadOnlyList<ChunkMetadata> GetMetadata() => Array.Empty<ChunkMetadata>();

        public void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector)
        {
            UpsertCalls++;
        }
    }
}
