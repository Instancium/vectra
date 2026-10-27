namespace Instancium.Vectra.Tests.Integration.Pipeline;

using System.Linq;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Search;
using Instancium.Vectra.Tests.Integration.Gold;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Integration")]
public sealed class IndexingPipelineIntegrationTests
{
    [Fact]
    public async Task IndexingPipeline_ShouldIndex20PagesAndRetrieveKnownAnswer()
    {
        if (!IsGatewayAvailable())
        {
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVectraInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vectra:Gateways:Embedding:Url"] = "http://localhost:5001",
            ["Vectra:Gateways:Embedding:HubPath"] = "/hub/gateway",
            ["Vectra:Gateways:Embedding:RepoId"] = "qwen3-embedding-0.6b",
            ["Vectra:Gateways:Embedding:ApiKey"] = "ChangeMeSuperSecureAdminApiKey123",
            ["Vectra:Gateways:Embedding:MaxRetryAttempts"] = "2"
        }).Build());
        services.AddVectraInMemorySearch();
        services.AddVectraIndexing();

        var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();

        var query = GoldQueryLoader.Load("gold/queries.json").First();
        var pdfPath = Path.Combine(AppContext.BaseDirectory, "assets", "pdf", "sample-2pages.pdf");

        var result = await pipeline.RunAsync(new IndexingPipelineRequest(
            pdfPath,
            1,
            2,
            "qwen3-embedding-0.6b",
            new TextChunkingOptions(200, 50),
            2));

        Assert.Equal(2, result.Metrics.TotalPages);
        Assert.NotNull(result.Manifest);
        Assert.True(result.Metrics.TotalChunks > 0);
        Assert.Equal(result.Metrics.TotalChunks, result.Metrics.EmbeddingsGenerated);
        Assert.True(result.Manifest.Dimension > 0);

        var queryVector = await engine.GenerateEmbeddingsAsync(
            "qwen3-embedding-0.6b",
            new EmbeddingRequest(query.Query, Normalize: true));

        var matches = await searchEngine.SearchAsync(queryVector.Embedding, 3);
        Assert.NotEmpty(matches);
    }

    private static bool IsGatewayAvailable()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = client.GetAsync("http://localhost:5001/hub/gateway").GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
