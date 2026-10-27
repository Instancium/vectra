namespace Instancium.Vectra.Tests.Unit.Search;

using System.Diagnostics;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryVectorSearchEngineTests
{
    [Fact]
    public async Task InMemoryVectorSearchEngine_ShouldFindExactMatchInTop1()
    {
        var store = new InMemoryVectorStore();
        var search = new InMemoryVectorSearchEngine(store);

        var target = new[] { 0.2f, 0.4f, 0.6f, 0.8f };
        store.Upsert(1, 10, 1, "A", target);
        store.Upsert(2, 10, 1, "B", new[] { 0.1f, 0.3f, 0.5f, 0.7f });
        store.Upsert(3, 10, 2, "C", new[] { 0.8f, 0.6f, 0.4f, 0.2f });

        var result = await search.SearchAsync(target, 1);

        var first = Assert.Single(result);
        Assert.Equal(1, first.ChunkId);
        Assert.Equal(10, first.FileId);
        Assert.Equal("A", first.Snippet);
        Assert.True(first.Score > 0.999f, $"Expected near-perfect score, got {first.Score}.");
    }

    [Fact]
    public async Task InMemoryVectorSearchEngine_ShouldFindExactMatchInTop1_Fast()
    {
        var store = new InMemoryVectorStore();
        var search = new InMemoryVectorSearchEngine(store);

        var random = new Random(42);
        var dimension = 1024;
        var count = 50_000;

        for (var index = 0; index < count; index++)
        {
            var vector = new float[dimension];
            for (var i = 0; i < dimension; i++)
            {
                vector[i] = (float)random.NextDouble() * 2f - 1f;
            }

            store.Upsert(index + 1, 1, (index % 10) + 1, $"chunk-{index}", vector);
        }

        var target = new float[dimension];
        for (var i = 0; i < dimension; i++)
        {
            target[i] = 0.7f;
        }

        var stopwatch = Stopwatch.StartNew();
        var matches = await search.SearchAsync(target, 3);
        stopwatch.Stop();

        Assert.Equal(3, matches.Count);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"Search took too long: {stopwatch.ElapsedMilliseconds}ms.");
    }
}
