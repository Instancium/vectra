#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using System.Diagnostics;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;
using Xunit.Abstractions;

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
        var store = new InMemoryVectorStore();
        var random = new Random(42);

        var planted = new float[Dimension];
        for (int i = 0; i < Dimension; i++)
        {
            planted[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        float[] plantedNormalized = Normalize(planted);

        for (long id = 1; id <= VectorCount; id++)
        {
            var vector = new float[Dimension];
            for (int i = 0; i < Dimension; i++)
            {
                vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
            }

            store.Upsert(id, 1, (int)((id - 1) % 100) + 1, "chunk " + id, Normalize(vector));
        }

        store.Upsert(VectorCount + 1, 1, 42, "planted target chunk", plantedNormalized);

        var engine = new InMemoryVectorSearchEngine(store);
        var query = new SearchQuery(plantedNormalized);
        var options = new SearchOptions(topK: 3);

        for (int warmup = 0; warmup < WarmupSearches; warmup++)
        {
            await engine.SearchAsync(query, options);
        }

        var latencies = new List<double>(MeasuredSearches);
        var stopwatch = new Stopwatch();
        IReadOnlyList<VectorSearchHit> lastRun = Array.Empty<VectorSearchHit>();

        for (int measured = 0; measured < MeasuredSearches; measured++)
        {
            stopwatch.Restart();
            lastRun = await engine.SearchAsync(query, options);
            stopwatch.Stop();
            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.Equal(VectorCount + 1, lastRun[0].VectorId);
        Assert.True(lastRun[0].VectorScore > 0.99f, $"Planted score {lastRun[0].VectorScore:F4} is too low.");

        latencies.Sort();
        double p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];

        _output.WriteLine(
            $"vectors={store.Count} dimension={Dimension} min={latencies[0]:F3} ms median={latencies[latencies.Count / 2]:F3} ms p95={p95:F3} ms (budget {P95BudgetMilliseconds:F0} ms).");

        Assert.True(
            p95 <= P95BudgetMilliseconds,
            $"p95 search latency {p95:F3} ms exceeds budget {P95BudgetMilliseconds:F0} ms.");
    }

    private static float[] Normalize(float[] vector)
    {
        double sumOfSquares = 0.0;
        foreach (float value in vector)
        {
            sumOfSquares += (double)value * value;
        }

        float norm = (float)Math.Sqrt(sumOfSquares);
        if (norm < 1e-12f)
        {
            return vector;
        }

        var normalized = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            normalized[i] = vector[i] / norm;
        }

        return normalized;
    }
}

[CollectionDefinition(SearchScaleBenchCollection.Name, DisableParallelization = true)]
public sealed class SearchScaleBenchCollection
{
    public const string Name = "Search scale bench";
}