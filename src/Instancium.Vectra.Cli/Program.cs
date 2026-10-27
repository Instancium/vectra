using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Search;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

const string RepoId = "qwen3-embedding-0.6b";

string? apiKey = Environment.GetEnvironmentVariable("VECTRA_GATEWAY_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set VECTRA_GATEWAY_API_KEY (GatewayUser bearer token) before running the CLI.");
    return 2;
}

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddVectraInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Vectra:Gateways:Embedding:Url"] = Environment.GetEnvironmentVariable("VECTRA_EMBEDDING_GATEWAY_URL") ?? "http://localhost:5001",
    ["Vectra:Gateways:Embedding:HubPath"] = "/hub/gateway",
    ["Vectra:Gateways:Embedding:RepoId"] = RepoId,
    ["Vectra:Gateways:Embedding:ApiKey"] = apiKey,
    ["Vectra:Gateways:Embedding:MaxRetryAttempts"] = "2"
}).Build());
services.AddVectraInMemorySearch();
services.AddVectraIndexing();

await using ServiceProvider provider = services.BuildServiceProvider();
Console.WriteLine("Instancium.Vectra CLI started.");

if (args.Length == 0)
{
    Console.WriteLine("Usage: vectra <index <pdf> <from> <to> | search \"<query>\" | stats>");
    return 0;
}

string command = args[0].ToLowerInvariant();
switch (command)
{
    case "index":
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: index <pdf> <from> <to>");
            return 2;
        }

        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var request = new IndexingPipelineRequest(args[1], int.Parse(args[2]), int.Parse(args[3]), RepoId);
        IndexingPipelineResult result = await pipeline.RunAsync(request);
        Console.WriteLine(
            $"Indexed pages {result.Metrics.TotalPages}; chunks {result.Metrics.TotalChunks}; embeddings {result.Metrics.EmbeddingsGenerated}; duration {result.Metrics.TotalDuration.TotalMilliseconds:F0} ms");
        return 0;

    case "search":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: search \"<query>\"");
            return 2;
        }

        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();
        EmbeddingResponse embedResponse = await engine.GenerateEmbeddingsAsync(RepoId, new EmbeddingRequest(args[1], Normalize: true));
        IReadOnlyList<SearchResult> matches = await searchEngine.SearchAsync(embedResponse.Embedding, 3);
        foreach (SearchResult match in matches)
        {
            Console.WriteLine($"chunk={match.ChunkId} page={match.PageNumber} score={match.Score:F4} snippet={match.Snippet}");
        }

        return 0;

    case "stats":
        var store = provider.GetRequiredService<IVectorStore>();
        Console.WriteLine($"vectors={store.Count} dimension={store.Dimension}");
        return 0;

    default:
        Console.WriteLine("Unknown command. Supported: index, search, stats.");
        return 2;
}