#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryVectorSearchEngineTests
{
    [Fact]
    public async Task SearchAsync_ShouldReturnHitWithVectorIdPayloadAndRawScore()
    {
        var store = new InMemoryVectorStore();
        var search = new InMemoryVectorSearchEngine(store);
        float[] target = Normalize(new float[] { 0.2f, 0.4f, 0.6f, 0.8f });
        store.Upsert(1, 10, 5, "A", target);
        store.Upsert(2, 10, 6, "B", Normalize(new float[] { 0.1f, 0.3f, 0.5f, 0.7f }));

        IReadOnlyList<VectorSearchHit> result = await search.SearchAsync(
            new SearchQuery(target),
            new SearchOptions(topK: 1));

        VectorSearchHit first = Assert.Single(result);
        Assert.Equal(1, first.VectorId);
        Assert.Equal("A", first.Payload);
        Assert.True(first.VectorScore > 0.999f, $"Expected near-perfect score, got {first.VectorScore}.");
        Assert.Equal(first.VectorScore, first.FinalScore);
        Assert.Null(first.Diagnostics);
    }

    [Fact]
    public async Task SearchAsync_ShouldBreakTiesByVectorIdAscending()
    {
        var store = new InMemoryVectorStore();
        var search = new InMemoryVectorSearchEngine(store);
        float[] vector = Normalize(new float[] { 1f, 0f });
        store.Upsert(7, 1, 1, "seven", vector);
        store.Upsert(3, 1, 1, "three", vector);

        IReadOnlyList<VectorSearchHit> result = await search.SearchAsync(
            new SearchQuery(vector),
            new SearchOptions(topK: 2));

        Assert.Equal(new long[] { 3, 7 }, result.Select(hit => hit.VectorId).ToArray());
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyWhenStoreIsEmpty()
    {
        var search = new InMemoryVectorSearchEngine(new InMemoryVectorStore());
        IReadOnlyList<VectorSearchHit> result = await search.SearchAsync(
            new SearchQuery(new float[] { 1f, 2f }),
            new SearchOptions(topK: 3));
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowOnDimensionMismatch()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "A", new float[] { 1f, 2f, 3f });
        var search = new InMemoryVectorSearchEngine(store);
        await Assert.ThrowsAsync<ArgumentException>(
            () => search.SearchAsync(new SearchQuery(new float[] { 1f }), new SearchOptions(topK: 1)));
    }

    [Fact]
    public async Task SearchAsync_LegacyOverload_ShouldDelegateToPrimaryContract()
    {
        var store = new InMemoryVectorStore();
        IVectorSearchEngine search = new InMemoryVectorSearchEngine(store);
        float[] vector = Normalize(new float[] { 1f, 0f });
        store.Upsert(1, 1, 1, "one", vector);

        IReadOnlyList<VectorSearchHit> result = await search.SearchAsync(vector, 1);

        VectorSearchHit first = Assert.Single(result);
        Assert.Equal(1, first.VectorId);
        Assert.Equal("one", first.Payload);
    }

    private static float[] Normalize(float[] vector)
    {
        double sumOfSquares = 0.0;
        foreach (float value in vector)
        {
            sumOfSquares += (double)value * value;
        }

        float norm = (float)Math.Sqrt(sumOfSquares);
        return vector.Select(value => value / norm).ToArray();
    }
}