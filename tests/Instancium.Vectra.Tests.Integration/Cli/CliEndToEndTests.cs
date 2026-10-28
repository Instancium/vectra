namespace Instancium.Vectra.Tests.Integration.Cli;

using System.Security.Cryptography;
using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Search;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Integration")]
public sealed class CliEndToEndTests
{
    private const int StubDimension = 64;
    private const int PageWindow = 20;

    private static readonly string CorpusPdfPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "pdf", "test-doc.pdf");

    [Fact]
    public async Task CliEndToEnd_ShouldIndexAndSearch()
    {
        // Arrange
        Assert.True(File.Exists(CorpusPdfPath), $"Missing test asset: {CorpusPdfPath}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IInferenceEngine, HashedStubInferenceEngine>();
        services.AddVectraInMemorySearch();
        services.AddVectraIndexing();

        await using ServiceProvider provider = services.BuildServiceProvider();

        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();
        var store = provider.GetRequiredService<IVectorStore>();

        // Act: index 20 pages of the real corpus through the full pipeline
        IndexingPipelineResult result = await pipeline.RunAsync(
            new IndexingPipelineRequest(CorpusPdfPath, 1, PageWindow, "stub-qwen3-embedding"));

        // Assert: indexing invariants
        Assert.True(result.Metrics.TotalPages >= 15,
            $"Expected at least 15 non-empty pages, got {result.Metrics.TotalPages}.");
        Assert.True(result.Metrics.TotalChunks > 0);
        Assert.Equal(result.Metrics.TotalChunks, result.Metrics.EmbeddingsGenerated);
        Assert.Equal(result.Metrics.TotalChunks, store.Count);
        Assert.Equal(StubDimension, store.Dimension);
        Assert.All(store.GetMetadata(), meta => Assert.InRange(meta.PageNumber, 1, PageWindow));

        // Act: search by the exact text of the largest indexed chunk
        ChunkMetadata target = store.GetMetadata()
            .OrderByDescending(meta => meta.Snippet.Length)
            .First();
        EmbeddingResponse queryVector = await engine.GenerateEmbeddingsAsync(
            "stub-qwen3-embedding",
            new EmbeddingRequest(target.Snippet, Normalize: true));

        IReadOnlyList<SearchResult> firstRun = await searchEngine.SearchAsync(queryVector.Embedding, 3);
        IReadOnlyList<SearchResult> secondRun = await searchEngine.SearchAsync(queryVector.Embedding, 3);

        // Assert: retrieval quality and determinism
        Assert.NotEmpty(firstRun);
        Assert.Equal(target.ChunkId, firstRun[0].ChunkId);
        Assert.Equal(target.PageNumber, firstRun[0].PageNumber);
        Assert.True(firstRun[0].Score > 0.999f,
            $"Expected near-perfect self-similarity, got {firstRun[0].Score:F4}.");
        Assert.Equal(firstRun.Select(m => m.ChunkId), secondRun.Select(m => m.ChunkId));
    }

    /// <summary>
    /// Deterministic embedding stub: SHA-256(text) seeds a normalized pseudo-random vector.
    /// Identical texts map to identical unit vectors (score 1.0); unrelated texts are near-orthogonal.
    /// Mocks the existing InstantAIGate.Core contract without touching the gateway.
    /// </summary>
    private sealed class HashedStubInferenceEngine : IInferenceEngine
    {
        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(StubDimension);

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string modelId,
            EmbeddingRequest request,
            CancellationToken ct = default)
        {
            float[] vector = Embed(request.Text);
            return Task.FromResult(new EmbeddingResponse(vector, TokenCount: 1, EmbeddingDimension: vector.Length));
        }

        public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
            => throw new NotSupportedException("Tokenization is not supported by the CLI e2e stub.");

        public Task<string> ApplyChatTemplateAsync(
            string modelId,
            IEnumerable<ChatMessage> messages,
            CancellationToken ct = default)
            => throw new NotSupportedException("Chat templates are not supported by the CLI e2e stub.");

        public IAsyncEnumerable<string> StreamDeltaGenerationAsync(
            string sessionId,
            ChatMessage deltaMessage,
            InferenceSettings? overrideSettings = null,
            CancellationToken ct = default)
            => throw new NotSupportedException("Streaming is not supported by the CLI e2e stub.");

        private static float[] Embed(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            int seed = BitConverter.ToInt32(hash, 0);
            var random = new Random(seed);

            var vector = new float[StubDimension];
            double sumOfSquares = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
                sumOfSquares += (double)vector[i] * vector[i];
            }

            float norm = (float)Math.Sqrt(sumOfSquares);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }

            return vector;
        }
    }
}