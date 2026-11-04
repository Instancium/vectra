#nullable enable
namespace Instancium.Vectra.Tests.Unit.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Core.Testing;
using Instancium.Vectra.Search;
using Xunit;

[Trait("Category", "Unit")]
public sealed class VectraTextExtensionsTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "vectra-text-tests", Guid.NewGuid().ToString("N"));
    private readonly IVectraEngine _engine;
    private readonly HashedStubEmbeddingProvider _provider;

    public VectraTextExtensionsTests()
    {
        Directory.CreateDirectory(_baseDir);
        _engine = VectraEngineFactory.Create(new VectraOptions { BaseDirectory = _baseDir });
        _provider = new HashedStubEmbeddingProvider(dimension: 16);
    }

    public void Dispose()
    {
        _engine.DisposeAsync().GetAwaiter().GetResult();
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    [Fact]
    public async Task AddTextAndSearchText_RoundTrip_ShouldHydratePayloadAndMatchPlantedDocument()
    {
        var col = _engine.GetOrOpenCollection(
            "text_roundtrip",
            new CollectionOptions(_provider.Dimension, SimilarityMetric.Cosine, _provider.ModelId));

        long id1 = await col.AddTextAsync(_provider, "The quick brown fox jumps over the lazy dog", "Story Alpha");
        long id2 = await col.AddTextAsync(_provider, "Artificial intelligence and machine learning engineering", "Tech Beta");

        var hits = await col.SearchTextAsync(_provider, "The quick brown fox jumps over the lazy dog", new SearchOptions(topK: 1));

        var hit = Assert.Single(hits);
        Assert.Equal(id1, hit.VectorId);
        Assert.Equal("Story Alpha", hit.Payload);
        Assert.True(hit.VectorScore > 0.999f, $"Expected near-identical vector score, got {hit.VectorScore}");
    }

    [Fact]
    public async Task AddTextAsync_WhenPayloadIsNull_ShouldDefaultPayloadToSourceText()
    {
        var col = _engine.GetOrOpenCollection(
            "text_default_payload",
            new CollectionOptions(_provider.Dimension, SimilarityMetric.Cosine, _provider.ModelId));

        const string sourceText = "Self-contained snippet without explicit payload override";
        long id = await col.AddTextAsync(_provider, sourceText);

        string? payload = await col.GetPayloadTextAsync(id);
        Assert.Equal(sourceText, payload);
    }

    [Fact]
    public async Task SearchTextAsync_ShouldPropagateQueryTextToReranker()
    {
        var col = _engine.GetOrOpenCollection(
            "text_rerank_propagation",
            new CollectionOptions(_provider.Dimension, SimilarityMetric.Cosine, _provider.ModelId));

        await col.AddTextAsync(_provider, "Document payload content", "Doc 1");

        var spyReranker = new QueryCapturingReranker();
        const string queryText = "Domain specific query for reranker inspection";

        await col.SearchTextAsync(_provider, queryText, new SearchOptions(reranker: spyReranker));

        Assert.Equal(queryText, spyReranker.CapturedQueryText);
    }

    [Fact]
    public async Task HashedStubEmbeddingProvider_ShouldGenerateDeterministicAndNormalizedVectors()
    {
        const string input = "Determinism verification string";

        var v1 = await _provider.EmbedAsync(input);
        var v2 = await _provider.EmbedAsync(input);

        Assert.Equal(_provider.Dimension, v1.Length);
        Assert.True(v1.Span.SequenceEqual(v2.Span), "Identical input texts must produce bit-identical vectors.");

        double sumSquares = 0.0;
        foreach (float val in v1.Span)
        {
            sumSquares += (double)val * val;
        }

        double norm = Math.Sqrt(sumSquares);
        Assert.True(Math.Abs(norm - 1.0) < 1e-5, $"Vector must be unit-normalized, calculated norm was {norm}.");
    }

    [Fact]
    public async Task AddTextAsync_ShouldThrowDimensionMismatch_WhenProviderMismatchWithCollection()
    {
        var mismatchedProvider = new HashedStubEmbeddingProvider(dimension: 32);
        var col = _engine.GetOrOpenCollection(
            "dim_mismatch",
            new CollectionOptions(_provider.Dimension, SimilarityMetric.Cosine, _provider.ModelId));

        await Assert.ThrowsAsync<VectraDimensionMismatchException>(async () =>
            await col.AddTextAsync(mismatchedProvider, "Arbitrary text"));
    }

    [Fact]
    public async Task TextExtensions_ShouldThrowOnNullOrWhitespaceInputs()
    {
        var col = _engine.GetOrOpenCollection(
            "arg_guards",
            new CollectionOptions(_provider.Dimension, SimilarityMetric.Cosine, _provider.ModelId));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await col.AddTextAsync(_provider, "   "));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await col.SearchTextAsync(_provider, string.Empty));
    }

    private sealed class QueryCapturingReranker : IReranker
    {
        public string? CapturedQueryText { get; private set; }

        public Task<IReadOnlyList<VectorSearchHit>> RerankAsync(
            SearchQuery query,
            IReadOnlyList<VectorSearchHit> candidates,
            CancellationToken cancellationToken = default)
        {
            CapturedQueryText = query.QueryText;
            return Task.FromResult(candidates);
        }
    }
}
