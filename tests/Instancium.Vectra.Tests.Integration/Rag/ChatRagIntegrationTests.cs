namespace Instancium.Vectra.Tests.Integration.Rag;

using System.Net.Http;
using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Rag.DependencyInjection;
using Instancium.Vectra.Search;
using Instancium.Vectra.Tests.Integration.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Integration")]
public sealed class ChatRagIntegrationTests
{
    private const string EmbeddingRepoId = "qwen3-embedding-0.6b";

    [ChatGatewayFact]
    public async Task ChatRag_ShouldStreamGroundedAnswerFromLiveChatGateway()
    {
        string corpusPath = GatewayTestEnvironment.CorpusPath!;
        await using ServiceProvider provider = BuildProvider();

        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var rag = provider.GetRequiredService<IRagService>();

        IndexingPipelineResult indexing = await pipeline.RunAsync(
            new IndexingPipelineRequest(corpusPath, 1, 20, EmbeddingRepoId));
        Assert.True(indexing.Metrics.TotalChunks > 0);

        RagRetrievalResult retrieval = await rag.RetrieveAsync(new RagRequest(
            "How does a pressure transmitter convert diaphragm deformation into an electrical signal?",
            EmbeddingRepoId,
            topK: 3));
        Assert.NotEmpty(retrieval.Context);
        Assert.All(retrieval.Context, chunk => Assert.InRange(chunk.PageNumber, 1, 20));

        var answer = new StringBuilder();
        await foreach (string token in rag.StreamAnswerAsync(retrieval))
        {
            answer.Append(token);
        }

        Assert.True(
            answer.Length >= 40,
            $"Chat gateway returned a suspiciously short answer: '{answer}'.");
    }

    private static ServiceProvider BuildProvider()
    {
        string chatUrl = Environment.GetEnvironmentVariable("VECTRA_CHAT_GATEWAY_URL") ?? "http://localhost:5000";
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vectra:Gateways:Embedding:Url"] = GatewayTestEnvironment.GatewayUrl,
                ["Vectra:Gateways:Embedding:HubPath"] = "/hub/gateway",
                ["Vectra:Gateways:Embedding:RepoId"] = EmbeddingRepoId,
                ["Vectra:Gateways:Embedding:ApiKey"] = GatewayTestEnvironment.ApiKey,
                ["Vectra:Gateways:Embedding:MaxRetryAttempts"] = "2",
                ["Vectra:Gateways:Chat:Url"] = chatUrl,
                ["Vectra:Gateways:Chat:HubPath"] = "/hub/gateway",
                ["Vectra:Gateways:Chat:RepoId"] = "qwen3-vl-8b-instruct",
                ["Vectra:Gateways:Chat:ApiKey"] = GatewayTestEnvironment.ApiKey
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddVectraInfrastructure(configuration);
        services.AddVectraChatInfrastructure(configuration);
        services.AddVectraInMemorySearch();
        services.AddVectraIndexing();
        services.AddVectraRag();
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Runs only when the chat gateway process is reachable (/health/live, always 200 per guide v1.4)
/// AND the model is loaded (/health/ready, 200 vs 503). VECTRA_REQUIRE_CHAT=1 disables skipping.
/// </summary>
public sealed class ChatGatewayFactAttribute : FactAttribute
{
    public ChatGatewayFactAttribute()
    {
        Skip = GatewayTestEnvironment.GetSkipReason(requiresCorpus: true) ?? ProbeChatGateway();
    }

    private static string? ProbeChatGateway()
    {
        if (Environment.GetEnvironmentVariable("VECTRA_REQUIRE_CHAT") == "1")
        {
            return null;
        }

        string url = Environment.GetEnvironmentVariable("VECTRA_CHAT_GATEWAY_URL") ?? "http://localhost:5000";

        string? live = Probe($"{url}/health/live", timeoutSeconds: 3);
        if (live is not null)
        {
            return $"Chat gateway unreachable at {url}/health/live ({live}).";
        }

        string? ready = Probe($"{url}/health/ready", timeoutSeconds: 5);
        if (ready is not null)
        {
            return $"Chat gateway process is alive but not ready at {url}/health/ready ({ready}); model may still be loading.";
        }

        return null;
    }

    private static string? Probe(string url, int timeoutSeconds)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }
}
