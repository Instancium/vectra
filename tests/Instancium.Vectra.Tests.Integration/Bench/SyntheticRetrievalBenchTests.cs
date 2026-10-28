#nullable enable
namespace Instancium.Vectra.Tests.Integration.Bench;

using System.Diagnostics;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Search;
using Instancium.Vectra.Tests.Integration.Support;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// H3: Validates embedding model + dot-product ranking quality in isolation.
/// No PDF, no extraction, no chunking. Chunks are injected directly.
/// If this fails, the problem is model quality or ranking, not infrastructure.
/// </summary>
[Trait("Category", "Bench")]
public sealed class SyntheticRetrievalBenchTests
{
    private const double HitRateTarget = 0.8;
    private const double P95BudgetMs = 20.0;

    [GatewayFact]
    public async Task SyntheticChunks_ShouldAchieveHitRateAndLatency()
    {
        await using ServiceProvider provider = BuildProvider();
        var engine = provider.GetRequiredService<IInferenceEngine>();
        var store = provider.GetRequiredService<IVectorStore>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();

        // Arrange: inject 12 synthetic "sections" with distractors
        var topics = SyntheticCorpus.Generate();
        long chunkId = 0;
        foreach (var topic in topics)
        {
            foreach (var chunk in topic.Chunks)
            {
                chunkId++;
                EmbeddingResponse response = await engine.GenerateEmbeddingsAsync(
                    "qwen3-embedding-0.6b",
                    new EmbeddingRequest(chunk.Text, Normalize: true));
                store.Upsert(chunkId, 1, chunk.PageNumber, chunk.Text, response.Embedding);
            }
        }

        // Act: query each topic, expect its chunks in Top-3
        int hits = 0;
        var latencies = new List<double>();

        foreach (var topic in topics)
        {
            EmbeddingResponse queryVec = await engine.GenerateEmbeddingsAsync(
                "qwen3-embedding-0.6b",
                new EmbeddingRequest(topic.Query, Normalize: true));

            for (int rep = 0; rep < 7; rep++)
            {
                var sw = Stopwatch.StartNew();
                IReadOnlyList<SearchResult> results = await searchEngine.SearchAsync(queryVec.Embedding, 3);
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);

                if (rep == 0)
                {
                    bool hit = results.Any(r => r.PageNumber >= topic.PageFrom && r.PageNumber <= topic.PageTo);
                    if (hit) hits++;
                }
            }
        }

        latencies.Sort();
        double p95 = latencies[(int)Math.Ceiling(0.95 * latencies.Count) - 1];
        double hitRate = (double)hits / topics.Count;

        Assert.True(hitRate >= HitRateTarget,
            $"H3 FAILED: hit-rate {hitRate:F2} < {HitRateTarget}. Model/ranking quality insufficient.");
        Assert.True(p95 <= P95BudgetMs,
            $"H3 FAILED: p95 {p95:F3} ms > {P95BudgetMs} ms.");
    }

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