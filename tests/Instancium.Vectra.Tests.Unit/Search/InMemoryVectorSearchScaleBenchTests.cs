#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using System.Diagnostics;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// In-memory brute-force scale guard: 5k vectors x 1024 dims under a p95 budget.
/// Lives in the Bench category, not Unit: latency assertions require warm-up and
/// isolation from parallel test contention to stay stable on shared machines.
/// </summary>
[Trait("Category", "Bench")]
[Collection(SearchScaleBenchCollection.Name)]
public sealed class InMemoryVectorSearchScaleBenchTests
{
    private const int VectorCount = 5_000;
    private const int Dimension = 1024;
    private const int WarmupSearches = 5;
    private const int MeasuredSearches = 30;
    private const double P95BudgetMilliseconds = 30.0;

    private readonly ITestOutputHelper _output;

    public InMemoryVectorSearchScaleBenchTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SearchAsync_ShouldKeepPlantedVectorInTop1_UnderP95Budget_AtMvpScale()
    {
        // Arrange: deterministic random corpus plus one exact-match planted vector so the top hit is unambiguous.
        var store = new InMemoryVectorStore();
        var random = new Random(42);

        var planted = new float[Dimension];
        for (int i = 0; i < Dimension; i++)
        {
            planted[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        for (long id = 1; id <= VectorCount; id++)
        {
            var vector = new float[Dimension];
            for (int i = 0; i < Dimension; i++)
            {
                vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
            }

            store.Upsert(id, 1, (int)((id - 1) % 100) + 1, $"chunk {id}", vector);
        }

        store.Upsert(VectorCount + 1, 1, 42, "planted target chunk", planted);
        var engine = new InMemoryVectorSearchEngine(store);

        // Warm-up: JIT the SIMD path, page in store memory, stabilize GC. Not measured.
        for (int warmup = 0; warmup < WarmupSearches; warmup++)
        {
            await engine.SearchAsync(planted, 3);
        }

        // Act: measured repetitions.
        var latencies = new List<double>(MeasuredSearches);
        var stopwatch = new Stopwatch();
        IReadOnlyList<SearchResult> lastRun = Array.Empty<SearchResult>();
        for (int measured = 0; measured < MeasuredSearches; measured++)
        {
            stopwatch.Restart();
            lastRun = await engine.SearchAsync(planted, 3);
            stopwatch.Stop();
            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        // Assert: correctness first, then the latency budget.
        Assert.Equal(VectorCount + 1, lastRun[0].ChunkId);
        Assert.True(lastRun[0].Score > 0.99f, $"Planted score {lastRun[0].Score:F4} is too low.");

        latencies.Sort();
        double p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];
        _output.WriteLine(
            $"vectors={store.Count} dimension={Dimension} min={latencies[0]:F3} ms median={latencies[latencies.Count / 2]:F3} ms p95={p95:F3} ms (budget {P95BudgetMilliseconds:F0} ms).");

        Assert.True(
            p95 <= P95BudgetMilliseconds,
            $"p95 search latency {p95:F3} ms exceeds budget {P95BudgetMilliseconds:F0} ms.");
    }
}

/// <summary>
/// Runs the scale bench serially: latency measurements must not share CPU with parallel collections.
/// </summary>
[CollectionDefinition(SearchScaleBenchCollection.Name, DisableParallelization = true)]
public sealed class SearchScaleBenchCollection
{
    public const string Name = "Search scale bench";
}
