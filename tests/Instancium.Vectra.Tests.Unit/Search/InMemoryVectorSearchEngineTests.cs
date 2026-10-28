// tests/Instancium.Vectra.Tests.Unit/Search/InMemoryVectorSearchEngineTests.cs
#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InMemoryVectorSearchEngineTests
{
    public InMemoryVectorSearchEngineTests()
    {
    }

    [Fact]
    public async Task SearchAsync_ShouldFindExactMatchInTop1WithUnitScoreAndVector()
    {
        var store = new InMemoryVectorStore();
        var search = new InMemoryVectorSearchEngine(store);
        float[] target = Normalize(new float[] { 0.2f, 0.4f, 0.6f, 0.8f });
        store.Upsert(1, 10, 5, "A", target);
        store.Upsert(2, 10, 6, "B", Normalize(new float[] { 0.1f, 0.3f, 0.5f, 0.7f }));
        store.Upsert(3, 11, 7, "C", Normalize(new float[] { 0.8f, 0.6f, 0.4f, 0.2f }));

        IReadOnlyList<SearchResult> result = await search.SearchAsync(target, 1);

        SearchResult first = Assert.Single(result);
        Assert.Equal(1, first.ChunkId);
        Assert.Equal(10, first.FileId);
        Assert.Equal(5, first.PageNumber);
        Assert.Equal("A", first.Snippet);
        Assert.True(first.Score > 0.999f, $"Expected near-perfect score, got {first.Score}.");
        Assert.Equal(target, first.Vector.ToArray());
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyWhenStoreIsEmpty()
    {
        var search = new InMemoryVectorSearchEngine(new InMemoryVectorStore());

        IReadOnlyList<SearchResult> result = await search.SearchAsync(new float[] { 1f, 2f }, 3);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAsync_ShouldThrowOnDimensionMismatch()
    {
        var store = new InMemoryVectorStore();
        store.Upsert(1, 1, 1, "A", new float[] { 1f, 2f, 3f });
        var search = new InMemoryVectorSearchEngine(store);

        await Assert.ThrowsAsync<ArgumentException>(() => search.SearchAsync(new float[] { 1f }, 1));
    }

    //[Fact]
    //public async Task SearchAsync_ShouldKeepPlantedVectorInTop1_UnderP95Budget_At50kScale()
    //{
    //    var store = new InMemoryVectorStore();
    //    var search = new InMemoryVectorSearchEngine(store);
    //    var random = new Random(42);

    //    for (int index = 0; index < 5000; index++)
    //    {
    //        var vector = new float[1024];
    //        for (int i = 0; i < 1024; i++)
    //        {
    //            vector[i] = (float)random.NextDouble() * 2f - 1f;
    //        }

    //        store.Upsert(index + 1, 1, (index % 10) + 1, $"chunk-{index}", Normalize(vector));
    //    }

    //    float[] planted = Normalize(Enumerable.Repeat(0.7f, 1024).ToArray());
    //    store.Upsert(5001, 9, 42, "planted", planted);

    //    for (int warmup = 0; warmup < 3; warmup++)
    //    {
    //        await search.SearchAsync(planted, 3);
    //    }

    //    var latencies = new List<double>();
    //    IReadOnlyList<SearchResult>? last = null;
    //    for (int run = 0; run < 15; run++)
    //    {
    //        var stopwatch = Stopwatch.StartNew();
    //        last = await search.SearchAsync(planted, 3);
    //        stopwatch.Stop();
    //        latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
    //    }

    //    latencies.Sort();
    //    double p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];
    //    _output.WriteLine($"vectors={store.Count} dimension=1024 p95={p95:F3} ms (budget 30 ms; TZ Stage-4 target 5 ms tracked in bench-report).");

    //    Assert.NotNull(last);
    //    Assert.Equal(5001, last[0].ChunkId);
    //    Assert.Equal(42, last[0].PageNumber);
    //    Assert.True(last[0].Score > 0.999f, $"Expected near-perfect score, got {last[0].Score}.");
    //    Assert.True(p95 <= 30.0, $"p95 search latency {p95:F3} ms exceeds budget 30 ms.");
    //}

    private static float[] Normalize(float[] vector)
    {
        double sumOfSquares = 0.0;
        foreach (float value in vector)
        {
            sumOfSquares += value * value;
        }

        float norm = (float)Math.Sqrt(sumOfSquares);
        return vector.Select(value => value / norm).ToArray();
    }
}


