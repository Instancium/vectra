// tests/Instancium.Vectra.Tests.Integration/Bench/IndexingPipelineBenchTests.cs
#nullable enable
namespace Instancium.Vectra.Tests.Integration.Bench;

using System.Diagnostics;
using System.Linq;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Search;
using Instancium.Vectra.Tests.Integration.Gold;
using Instancium.Vectra.Tests.Integration.Support;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Bench")]
public sealed class IndexingPipelineBenchTests
{
    private const double HitRateTarget = 0.8;
    private const double P95SearchBudgetMilliseconds = 20.0;
    private const int SearchRepetitions = 7;

    [GatewayFact(requiresCorpus: true)]
    public async Task Bench_ShouldAchieveHitRateAbove80Percent_AndP95Below20ms()
    {
        string corpusPath = GatewayTestEnvironment.CorpusPath!;
        IReadOnlyList<GoldQuery> queries = GoldQueryLoader.Load();
        Assert.True(queries.Count >= 10, $"Gold set must contain at least 10 queries, got {queries.Count}.");

        await using ServiceProvider provider = BuildProvider();
        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();
        var store = provider.GetRequiredService<IVectorStore>();

        IndexingPipelineResult indexing = await pipeline.RunAsync(new IndexingPipelineRequest(
            corpusPath,
            1,
            20,
            "qwen3-embedding-0.6b",
            new TextChunkingOptions(1200, 200),
            4));
        Assert.Equal(indexing.Metrics.TotalChunks, store.Count);

        int hits = 0;
        var searchLatencies = new List<double>();
        var rows = new List<string>();

        foreach (GoldQuery query in queries)
        {
            EmbeddingResponse queryVector = await engine.GenerateEmbeddingsAsync(
                "qwen3-embedding-0.6b",
                new EmbeddingRequest(query.Query, Normalize: true));

            IReadOnlyList<SearchResult> baseline = await searchEngine.SearchAsync(queryVector.Embedding, 3);
            bool hit = baseline.Any(m => m.PageNumber >= query.ExpectedPageFrom && m.PageNumber <= query.ExpectedPageTo);
            if (hit)
            {
                hits++;
            }

            for (int repetition = 0; repetition < SearchRepetitions; repetition++)
            {
                var stopwatch = Stopwatch.StartNew();
                IReadOnlyList<SearchResult> run = await searchEngine.SearchAsync(queryVector.Embedding, 3);
                stopwatch.Stop();
                searchLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                Assert.Equal(baseline.Select(m => m.ChunkId), run.Select(m => m.ChunkId));
            }

            rows.Add($"| query: {Truncate(query.Query)} | {(hit ? "hit" : "miss")} | pages {query.ExpectedPageFrom}-{query.ExpectedPageTo} | top3 pages: {string.Join("/", baseline.Select(m => m.PageNumber))} |");
        }

        searchLatencies.Sort();
        double p95 = searchLatencies[(int)Math.Ceiling(0.95 * searchLatencies.Count) - 1];
        double hitRate = (double)hits / queries.Count;

        var reportRows = new List<string>
        {
            $"| 20 pages index | chunks={indexing.Metrics.TotalChunks} | {indexing.Metrics.TotalDuration.TotalMilliseconds:F0} ms | {indexing.Metrics.ChunksPerSecond:F1} chunks/s, RAM peak {indexing.Metrics.PeakWorkingSetMb} MB |",
            $"| retrieval Top-3 hit-rate | {hitRate:F2} | - | target >= {HitRateTarget:F2} |",
            $"| in-process search p95 | - | {p95:F3} ms | target <= {P95SearchBudgetMilliseconds:F0} ms, n={searchLatencies.Count} |",
        };
        reportRows.AddRange(rows);

        string report = BenchReport.GenerateMarkdown("Vectra MVP Bench (20 pages, live gateway)", reportRows);
        string artifactsDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        await File.WriteAllTextAsync(Path.Combine(artifactsDirectory, "bench-report.md"), report);

        Assert.True(hitRate >= HitRateTarget, $"Top-3 hit-rate {hitRate:F2} below target {HitRateTarget:F2}. See artifacts/bench-report.md.");
        Assert.True(p95 <= P95SearchBudgetMilliseconds, $"p95 search latency {p95:F3} ms exceeds {P95SearchBudgetMilliseconds:F0} ms.");
    }

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..60] + "...";

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVectraInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vectra:Gateways:Embedding:Url"] = GatewayTestEnvironment.GatewayUrl,
            ["Vectra:Gateways:Embedding:HubPath"] = "/hub/gateway",
            ["Vectra:Gateways:Embedding:RepoId"] = "qwen3-embedding-0.6b",
            ["Vectra:Gateways:Embedding:ApiKey"] = GatewayTestEnvironment.ApiKey,
            ["Vectra:Gateways:Embedding:MaxRetryAttempts"] = "2"
        }).Build());
        services.AddVectraInMemorySearch();
        services.AddVectraIndexing();
        return services.BuildServiceProvider();
    }
}