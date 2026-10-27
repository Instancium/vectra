namespace Instancium.Vectra.Tests.Integration.Pipeline;

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
using Microsoft.Extensions.Logging;
using Xunit;

[Trait("Category", "Integration")]
public sealed class IndexingPipelineIntegrationTests
{
    [GatewayFact(requiresCorpus: true)]
    public async Task IndexingPipeline_ShouldIndex20PagesAndRetrieveKnownAnswer()
    {
        string corpusPath = GatewayTestEnvironment.CorpusPath!;
        await using ServiceProvider provider = BuildProvider();
        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();
        var store = provider.GetRequiredService<IVectorStore>();

        var result = await pipeline.RunAsync(new IndexingPipelineRequest(
            corpusPath,
            1,
            20,
            "qwen3-embedding-0.6b",
            new TextChunkingOptions(1200, 200),
            4));

        Assert.True(result.Metrics.TotalPages >= 15, $"Expected at least 15 non-empty pages, got {result.Metrics.TotalPages}.");
        Assert.True(result.Metrics.TotalChunks > 0);
        Assert.Equal(result.Metrics.TotalChunks, result.Metrics.EmbeddingsGenerated);
        Assert.Equal(result.Metrics.TotalChunks, store.Count);
        Assert.Equal(result.Metrics.TotalChunks, result.Manifest.ChunkIds.Distinct().Count());
        Assert.Equal(await engine.GetEmbeddingDimensionAsync("qwen3-embedding-0.6b"), result.Manifest.Dimension);
        Assert.Equal(store.Dimension, result.Manifest.Dimension);

        GoldQuery query = GoldQueryLoader.Load().First();
        EmbeddingResponse queryVector = await engine.GenerateEmbeddingsAsync(
            "qwen3-embedding-0.6b",
            new EmbeddingRequest(query.Query, Normalize: true));

        IReadOnlyList<SearchResult> firstRun = await searchEngine.SearchAsync(queryVector.Embedding, 3);
        IReadOnlyList<SearchResult> secondRun = await searchEngine.SearchAsync(queryVector.Embedding, 3);

        Assert.Equal(firstRun.Select(m => m.ChunkId), secondRun.Select(m => m.ChunkId));
        Assert.Equal(firstRun.Select(m => m.Score), secondRun.Select(m => m.Score));
        Assert.True(
            firstRun.Any(m => m.PageNumber >= query.ExpectedPageFrom && m.PageNumber <= query.ExpectedPageTo),
            $"Gold query '{query.Query}' expected page [{query.ExpectedPageFrom}..{query.ExpectedPageTo}], got pages [{string.Join(", ", firstRun.Select(m => m.PageNumber))}].");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));
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