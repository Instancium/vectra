#nullable enable

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Search;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
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

Console.WriteLine("Instancium.Vectra CLI started.");

if (args.Length == 0)
{
    return;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "index":
        if (args.Length < 4)
        {
            Console.WriteLine("Usage: index <pdf> <from> <to>");
            return;
        }

        var pipeline = provider.GetRequiredService<IIndexingPipeline>();
        var request = new IndexingPipelineRequest(args[1], int.Parse(args[2]), int.Parse(args[3]), "qwen3-embedding-0.6b");
        var result = await pipeline.RunAsync(request);
        Console.WriteLine($"Indexed pages {result.Metrics.TotalPages}; chunks {result.Metrics.TotalChunks}; embeddings {result.Metrics.EmbeddingsGenerated}; duration {result.Metrics.TotalDuration.TotalMilliseconds} ms");
        break;

    case "search":
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: search \"<query>\"");
            return;
        }

        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();
        var embedResponse = await engine.GenerateEmbeddingsAsync("qwen3-embedding-0.6b", new EmbeddingRequest(args[1], Normalize: true));
        var matches = await searchEngine.SearchAsync(embedResponse.Embedding, 3);
        foreach (var match in matches)
        {
            Console.WriteLine($"chunk={match.ChunkId} page={match.FileId} score={match.Score:F4} snippet={match.Snippet}");
        }
        break;

    case "stats":
        Console.WriteLine("stats command is available when an index has been prepared.");
        break;

    case "bench":
        Console.WriteLine("bench command delegated to integration bench tests.");
        break;

    default:
        Console.WriteLine("Unknown command. Supported: index, search, stats, bench");
        break;
}
