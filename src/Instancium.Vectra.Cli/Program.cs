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
using System.Diagnostics;
using System.Text;

string? apiKey = Environment.GetEnvironmentVariable(VectraCliConfig.ApiKeyVariable);
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine($"Set {VectraCliConfig.ApiKeyVariable} (GatewayUser bearer token) before running the CLI.");
    return 2;
}

if (args.Length == 0)
{
    return await RunReplAsync();
}

await using (ServiceProvider oneShotProvider = BuildServiceProvider())
{
    return await ExecuteCommandAsync(oneShotProvider, args);
}

static ServiceProvider BuildServiceProvider()
{
    string key = Environment.GetEnvironmentVariable(VectraCliConfig.ApiKeyVariable)
        ?? throw new InvalidOperationException($"{VectraCliConfig.ApiKeyVariable} is not set.");
    string gatewayUrl = Environment.GetEnvironmentVariable(VectraCliConfig.GatewayUrlVariable)
        ?? VectraCliConfig.DefaultGatewayUrl;

    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
    services.AddVectraInfrastructure(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Vectra:Gateways:Embedding:Url"] = gatewayUrl,
            ["Vectra:Gateways:Embedding:HubPath"] = "/hub/gateway",
            ["Vectra:Gateways:Embedding:RepoId"] = VectraCliConfig.RepoId,
            ["Vectra:Gateways:Embedding:ApiKey"] = key,
            ["Vectra:Gateways:Embedding:MaxRetryAttempts"] = "2"
        })
        .Build());
    services.AddVectraInMemorySearch();
    services.AddVectraIndexing();
    return services.BuildServiceProvider();
}

static async Task<int> RunReplAsync()
{
    await using ServiceProvider provider = BuildServiceProvider();
    Console.WriteLine("Instancium.Vectra REPL started. The index lives in this process memory.");
    PrintUsage();

    while (true)
    {
        if (!Console.IsInputRedirected)
        {
            Console.Write("vectra> ");
        }

        string? line = await Console.In.ReadLineAsync();
        if (line is null)
        {
            break;
        }

        string[] command = Tokenize(line);
        if (command.Length == 0)
        {
            continue;
        }

        if (command[0] is "exit" or "quit")
        {
            break;
        }

        try
        {
            await ExecuteCommandAsync(provider, command);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
        }
    }

    Console.WriteLine("REPL stopped.");
    return 0;
}

static async Task<int> ExecuteCommandAsync(ServiceProvider provider, string[] args)
{
    string command = args[0].ToLowerInvariant();
    switch (command)
    {
        case "index":
            return await RunIndexAsync(provider, args);
        case "search":
            return await RunSearchAsync(provider, args);
        case "stats":
            return RunStats(provider);
        case "bench":
            return await RunBenchAsync(args);
        case "help":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 2;
    }
}

static async Task<int> RunIndexAsync(ServiceProvider provider, string[] args)
{
    if (args.Length < 4
        || !int.TryParse(args[2], out int pageFrom)
        || !int.TryParse(args[3], out int pageTo))
    {
        Console.Error.WriteLine("Usage: index <pdf> <from> <to>");
        return 2;
    }

    var pipeline = provider.GetRequiredService<IIndexingPipeline>();
    var request = new IndexingPipelineRequest(Path.GetFullPath(args[1]), pageFrom, pageTo, VectraCliConfig.RepoId);

    Console.WriteLine($"Indexing {request.PdfPath} pages {pageFrom}-{pageTo}...");
    IndexingPipelineResult result = await pipeline.RunAsync(request);

    Console.WriteLine();
    Console.WriteLine("=== Indexing Complete ===");
    Console.WriteLine($"Total pages:      {result.Metrics.TotalPages}");
    Console.WriteLine($"Total chunks:     {result.Metrics.TotalChunks}");
    Console.WriteLine($"Embeddings:       {result.Metrics.EmbeddingsGenerated}");
    Console.WriteLine();
    Console.WriteLine("Timings:");
    Console.WriteLine($"  Extraction:     {result.Metrics.ExtractionDuration.TotalMilliseconds:F0} ms");
    Console.WriteLine($"  Chunking:       {result.Metrics.ChunkingDuration.TotalMilliseconds:F0} ms");
    Console.WriteLine($"  Embedding:      {result.Metrics.EmbeddingDuration.TotalMilliseconds:F0} ms");
    Console.WriteLine($"  Total:          {result.Metrics.TotalDuration.TotalMilliseconds:F0} ms");
    Console.WriteLine();
    Console.WriteLine("Performance:");
    Console.WriteLine($"  Pages/sec:      {result.Metrics.PagesPerSecond:F2}");
    Console.WriteLine($"  Chunks/sec:     {result.Metrics.ChunksPerSecond:F2}");
    Console.WriteLine($"  Peak RAM:       {result.Metrics.PeakWorkingSetMb} MB");
    Console.WriteLine();
    Console.WriteLine("Note: the index lives in process memory. Use the REPL (run without arguments) to keep it across commands.");
    return 0;
}

static async Task<int> RunSearchAsync(ServiceProvider provider, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: search \"<query>\" [topK]");
        return 2;
    }

    int topK = args.Length >= 3 && int.TryParse(args[2], out int parsedTopK)
        ? parsedTopK
        : VectraCliConfig.DefaultTopK;
    if (topK < 1)
    {
        Console.Error.WriteLine("topK must be >= 1.");
        return 2;
    }

    var store = provider.GetRequiredService<IVectorStore>();
    if (store.Count == 0)
    {
        Console.Error.WriteLine("Index is empty. Run 'index <pdf> <from> <to>' first (inside the REPL).");
        return 1;
    }

    var engine = provider.GetRequiredService<IInferenceEngine>();
    var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();

    string query = args[1];
    Console.WriteLine($"Searching for: \"{query}\" (top-{topK})");

    var embedStopwatch = Stopwatch.StartNew();
    EmbeddingResponse embedResponse = await engine.GenerateEmbeddingsAsync(
        VectraCliConfig.RepoId,
        new EmbeddingRequest(query, Normalize: true));
    embedStopwatch.Stop();

    var searchStopwatch = Stopwatch.StartNew();
    IReadOnlyList<SearchResult> matches = await searchEngine.SearchAsync(embedResponse.Embedding, topK);
    searchStopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine($"=== Search Results (Top-{topK}) ===");
    Console.WriteLine($"Embedding time: {embedStopwatch.Elapsed.TotalMilliseconds:F1} ms");
    Console.WriteLine($"Search time:    {searchStopwatch.Elapsed.TotalMilliseconds:F2} ms");
    Console.WriteLine();

    if (matches.Count == 0)
    {
        Console.WriteLine("No results found.");
        return 0;
    }

    for (int i = 0; i < matches.Count; i++)
    {
        SearchResult match = matches[i];
        Console.WriteLine($"#{i + 1}");
        Console.WriteLine($"  Chunk ID:   {match.ChunkId}");
        Console.WriteLine($"  Page:       {match.PageNumber}");
        Console.WriteLine($"  Score:      {match.Score:F4}");
        Console.WriteLine($"  Snippet:    {Truncate(match.Snippet, 150)}");
        Console.WriteLine();
    }

    return 0;
}

static int RunStats(ServiceProvider provider)
{
    var store = provider.GetRequiredService<IVectorStore>();

    Console.WriteLine("=== Index Statistics ===");
    Console.WriteLine($"Vectors:    {store.Count}");
    Console.WriteLine($"Dimension:  {store.Dimension}");

    if (store.Count > 0)
    {
        IReadOnlyList<ChunkMetadata> metadata = store.GetMetadata();
        List<int> pages = metadata.Select(m => m.PageNumber).Distinct().OrderBy(p => p).ToList();
        Console.WriteLine($"Pages:      {pages.Count} (range: {pages[0]}-{pages[^1]})");
        Console.WriteLine($"Files:      {metadata.Select(m => m.FileId).Distinct().Count()}");
    }

    return 0;
}

static async Task<int> RunBenchAsync(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: bench <pdf> [iterations]");
        return 2;
    }

    string pdfPath = Path.GetFullPath(args[1]);
    int iterations = args.Length >= 3 && int.TryParse(args[2], out int parsedIterations)
        ? parsedIterations
        : VectraCliConfig.DefaultBenchIterations;
    if (iterations < 1)
    {
        Console.Error.WriteLine("iterations must be >= 1.");
        return 2;
    }

    if (!File.Exists(pdfPath))
    {
        Console.Error.WriteLine($"PDF not found: {pdfPath}");
        return 1;
    }

    int totalPages;
    await using (ServiceProvider probeProvider = BuildServiceProvider())
    {
        ITextExtractor extractor = probeProvider.GetRequiredService<ITextExtractor>();
        IReadOnlyList<PageText> probe = await extractor.ExtractAsync(
            new TextExtractionRequest(pdfPath, 1, VectraCliConfig.MaxProbePages));
        totalPages = probe.Count == 0 ? 0 : probe.Max(p => p.PageNumber);
    }

    Console.WriteLine("=== Vectra Benchmark ===");
    Console.WriteLine($"PDF:         {pdfPath}");
    Console.WriteLine($"Pages:       {totalPages}");
    Console.WriteLine($"Iterations:  {iterations}");
    Console.WriteLine();

    var report = new StringBuilder();
    report.AppendLine("# Vectra Benchmark Report");
    report.AppendLine();
    report.AppendLine($"**PDF:** {Path.GetFileName(pdfPath)}");
    report.AppendLine($"**Date:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    report.AppendLine($"**Model:** {VectraCliConfig.RepoId}");
    report.AppendLine($"**Search repetitions per query:** {iterations} (+1 warm-up round)");
    report.AppendLine();
    report.AppendLine("## Matrix: 20 -> 50 -> 100 pages");
    report.AppendLine();
    report.AppendLine("| Pages | Chunks | Index Time (ms) | Chunks/sec | Search P95 (ms) |");
    report.AppendLine("|------:|-------:|----------------:|-----------:|----------------:|");

    bool anyWindow = false;
    foreach (int window in VectraCliConfig.BenchPageWindows)
    {
        if (window > totalPages)
        {
            Console.WriteLine($"Skipping {window}-page window (PDF has {totalPages} pages).");
            continue;
        }

        anyWindow = true;
        Console.WriteLine($"--- Benchmarking {window} pages ---");

        await using ServiceProvider windowProvider = BuildServiceProvider();
        var pipeline = windowProvider.GetRequiredService<IIndexingPipeline>();
        var engine = windowProvider.GetRequiredService<IInferenceEngine>();
        var searchEngine = windowProvider.GetRequiredService<IVectorSearchEngine>();

        var indexStopwatch = Stopwatch.StartNew();
        IndexingPipelineResult result = await pipeline.RunAsync(
            new IndexingPipelineRequest(pdfPath, 1, window, VectraCliConfig.RepoId));
        indexStopwatch.Stop();

        var queryVectors = new List<ReadOnlyMemory<float>>(VectraCliConfig.BenchQueries.Length);
        foreach (string query in VectraCliConfig.BenchQueries)
        {
            EmbeddingResponse response = await engine.GenerateEmbeddingsAsync(
                VectraCliConfig.RepoId,
                new EmbeddingRequest(query, Normalize: true));
            queryVectors.Add(response.Embedding);
        }

        foreach (ReadOnlyMemory<float> vector in queryVectors)
        {
            await searchEngine.SearchAsync(vector, VectraCliConfig.DefaultTopK);
        }

        var latencies = new List<double>(queryVectors.Count * iterations);
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            foreach (ReadOnlyMemory<float> vector in queryVectors)
            {
                var searchStopwatch = Stopwatch.StartNew();
                await searchEngine.SearchAsync(vector, VectraCliConfig.DefaultTopK);
                searchStopwatch.Stop();
                latencies.Add(searchStopwatch.Elapsed.TotalMilliseconds);
            }
        }

        latencies.Sort();
        double p95 = Percentile95(latencies);
        double chunksPerSecond = result.Metrics.TotalChunks / Math.Max(indexStopwatch.Elapsed.TotalSeconds, 0.001);

        Console.WriteLine($"  Indexed:     {result.Metrics.TotalChunks} chunks in {indexStopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"  Chunks/sec:  {chunksPerSecond:F1}");
        Console.WriteLine($"  Search P95:  {p95:F3} ms");
        Console.WriteLine();

        report.AppendLine($"| {window} | {result.Metrics.TotalChunks} | {indexStopwatch.ElapsedMilliseconds} | {chunksPerSecond:F1} | {p95:F3} |");
    }

    if (!anyWindow)
    {
        Console.Error.WriteLine(
            $"PDF has only {totalPages} pages; the smallest benchmark window is {VectraCliConfig.BenchPageWindows[0]} pages.");
        return 1;
    }

    report.AppendLine();
    report.AppendLine("## Notes");
    report.AppendLine();
    report.AppendLine("- Index time includes PDF extraction, chunking and embedding gateway calls.");
    report.AppendLine("- Search P95 measures in-process brute-force SIMD search only (query embedding excluded).");
    report.AppendLine("- Each window is indexed into an isolated in-memory store.");

    string artifactsDir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts");
    Directory.CreateDirectory(artifactsDir);
    string reportPath = Path.Combine(artifactsDir, "bench-report.md");
    await File.WriteAllTextAsync(reportPath, report.ToString());

    Console.WriteLine("=== Benchmark Complete ===");
    Console.WriteLine($"Report saved to: {reportPath}");
    return 0;
}

static double Percentile95(IReadOnlyList<double> sortedAsc)
{
    if (sortedAsc.Count == 0)
    {
        return 0.0;
    }

    int index = (int)Math.Ceiling(0.95 * sortedAsc.Count) - 1;
    return sortedAsc[Math.Clamp(index, 0, sortedAsc.Count - 1)];
}

static string[] Tokenize(string line)
{
    var tokens = new List<string>();
    var builder = new StringBuilder();
    bool inQuotes = false;

    foreach (char ch in line)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (char.IsWhiteSpace(ch) && !inQuotes)
        {
            if (builder.Length > 0)
            {
                tokens.Add(builder.ToString());
                builder.Clear();
            }

            continue;
        }

        builder.Append(ch);
    }

    if (builder.Length > 0)
    {
        tokens.Add(builder.ToString());
    }

    return tokens.ToArray();
}

static string Truncate(string text, int maxLength) =>
    string.IsNullOrEmpty(text) || text.Length <= maxLength
        ? text
        : text[..maxLength] + "...";

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  vectra                              Interactive REPL (index persists in process memory)");
    Console.WriteLine("  vectra index <pdf> <from> <to>      Index PDF pages");
    Console.WriteLine("  vectra search \"<query>\" [topK]      Search top-K results (default 3)");
    Console.WriteLine("  vectra stats                        Show index statistics");
    Console.WriteLine("  vectra bench <pdf> [iterations]     Run 20/50/100-page benchmark matrix");
    Console.WriteLine("  vectra help                         Show this help");
    Console.WriteLine("REPL commands: index, search, stats, bench, help, exit");
}

internal static class VectraCliConfig
{
    public const string ApiKeyVariable = "VECTRA_GATEWAY_API_KEY";
    public const string GatewayUrlVariable = "VECTRA_EMBEDDING_GATEWAY_URL";
    public const string DefaultGatewayUrl = "http://localhost:5001";
    public const string RepoId = "qwen3-embedding-0.6b";
    public const int DefaultTopK = 3;
    public const int DefaultBenchIterations = 5;
    public const int MaxProbePages = 100_000;

    public static readonly int[] BenchPageWindows = { 20, 50, 100 };

    public static readonly string[] BenchQueries =
    {
        "temperature measurement",
        "pressure transmitter",
        "flow meter",
        "control valve",
        "level sensor"
    };
}