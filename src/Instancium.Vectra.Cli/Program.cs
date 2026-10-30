using Instancium.Vectra.Cli;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Infrastructure.Chat;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.DependencyInjection;
using Instancium.Vectra.Rag;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage.DependencyInjection;
using Instancium.Vectra.Storage.Disk;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
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
    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddVectraConfiguration("config_vectra.json")
        .AddEnvironmentVariables()
        .Build();

    string key = Environment.GetEnvironmentVariable(VectraCliConfig.ApiKeyVariable)
        ?? configuration["Vectra:Gateways:Embedding:ApiKey"]
        ?? configuration["Vectra:Gateways:Chat:ApiKey"]
        ?? throw new InvalidOperationException($"{VectraCliConfig.ApiKeyVariable} is not set.");

    string gatewayUrl = Environment.GetEnvironmentVariable(VectraCliConfig.GatewayUrlVariable)
        ?? configuration["Vectra:Gateways:Embedding:Url"]
        ?? VectraCliConfig.DefaultGatewayUrl;

    string chatUrl = Environment.GetEnvironmentVariable(VectraCliConfig.ChatGatewayUrlVariable)
        ?? configuration["Vectra:Gateways:Chat:Url"]
        ?? VectraCliConfig.DefaultChatGatewayUrl;

    configuration["Vectra:Gateways:Embedding:Url"] = gatewayUrl;
    configuration["Vectra:Gateways:Embedding:HubPath"] ??= "/hub/gateway";
    configuration["Vectra:Gateways:Embedding:RepoId"] ??= VectraCliConfig.RepoId;
    configuration["Vectra:Gateways:Embedding:ApiKey"] = key;
    configuration["Vectra:Gateways:Embedding:MaxRetryAttempts"] ??= "2";

    configuration["Vectra:Gateways:Chat:Url"] = chatUrl;
    configuration["Vectra:Gateways:Chat:HubPath"] ??= "/hub/gateway";
    configuration["Vectra:Gateways:Chat:RepoId"] ??= VectraCliConfig.ChatRepoId;
    configuration["Vectra:Gateways:Chat:ApiKey"] = key;

    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

    services.AddVectraInfrastructure(configuration);
    services.AddVectraChatInfrastructure(configuration);
    services.AddVectraDiskIndex();
    services.AddVectraInMemorySearch();
    services.AddVectraIndexing();

    // Override IRagService to inject IVectraIndexSession for lazy chunk text hydration.
    ServiceDescriptor? ragDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRagService));
    if (ragDescriptor is not null)
    {
        services.Remove(ragDescriptor);
    }

    services.AddSingleton<IRagService>(sp => new RagService(
        sp.GetRequiredService<IInferenceEngine>(),
        sp.GetRequiredService<IVectorSearchEngine>(),
        sp.GetRequiredService<IChatCompletionClient>(),
        sp.GetRequiredService<ILogger<RagService>>(),
        chunkMap: null,
        chunkTextReader: null,
        indexSession: sp.GetRequiredService<IVectraIndexSession>()));

    return services.BuildServiceProvider();
}

static async Task<int> RunReplAsync()
{
    await using ServiceProvider provider = BuildServiceProvider();
    Console.WriteLine("Instancium.Vectra REPL started. Index lives on disk under .instancium/vectra/index.");
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

        string[] tokens = Tokenize(line);
        if (tokens.Length == 0)
        {
            continue;
        }

        if (tokens[0] is "exit" or "quit")
        {
            break;
        }

        try
        {
            await ExecuteCommandAsync(provider, tokens);
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
    CommandLineParser parsed = CommandLineParser.Parse(args);
    string? command = parsed.Command;

    return command switch
    {
        "index" => await RunIndexAsync(provider, parsed),
        "update" => await RunUpdateAsync(provider, parsed),
        "search" => await RunSearchAsync(provider, parsed),
        "ask" => await RunAskAsync(provider, parsed),
        "status" => await RunStatusAsync(provider, parsed),
        "doctor" => await RunDoctorAsync(provider, parsed),
        "probe-chat" => await RunProbeChatAsync(provider),
        "bench" => await RunBenchAsync(parsed),
        "stats" => RunStats(provider),
        "help" => PrintUsage(),
        null => PrintUsage(),
        _ => PrintUnknown(command)
    };
}

// =====================================================================
// Command: index <path> [--index-dir <dir>]
// =====================================================================
static async Task<int> RunIndexAsync(ServiceProvider provider, CommandLineParser args)
{
    if (args.PositionalArgs.Count < 1)
    {
        Console.Error.WriteLine("Usage: vectra index <path> [--index-dir <dir>]");
        return 2;
    }

    string path = Path.GetFullPath(args.PositionalArgs[0]);
    string? explicitDir = args.GetFlag("index-dir");

    if (!File.Exists(path) && !Directory.Exists(path))
    {
        Console.Error.WriteLine($"Path not found: {path}");
        return 1;
    }

    var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
    string indexDir = resolver.ResolveIndexDirectory(path, explicitDir);
    resolver.EnsureDirectoryStructure(indexDir);

    return await RunIndexCoreAsync(provider, path, indexDir);
}

// =====================================================================
// Command: update <path> [--index-dir <dir>]
// =====================================================================
static async Task<int> RunUpdateAsync(ServiceProvider provider, CommandLineParser args)
{
    if (args.PositionalArgs.Count < 1)
    {
        Console.Error.WriteLine("Usage: vectra update <path> [--index-dir <dir>]");
        return 2;
    }

    string path = Path.GetFullPath(args.PositionalArgs[0]);
    string? explicitDir = args.GetFlag("index-dir");

    if (!File.Exists(path) && !Directory.Exists(path))
    {
        Console.Error.WriteLine($"Path not found: {path}");
        return 1;
    }

    var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
    string indexDir = resolver.ResolveIndexDirectory(path, explicitDir);

    if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
    {
        Console.Error.WriteLine($"Index not found at '{indexDir}'. Run 'vectra index {path}' first.");
        return 1;
    }

    return await RunIndexCoreAsync(provider, path, indexDir);
}

// =====================================================================
// Shared indexer core
// =====================================================================
static async Task<int> RunIndexCoreAsync(ServiceProvider provider, string path, string indexDir)
{
    var indexer = provider.GetRequiredService<IIncrementalIndexer>();
    string repoId = VectraCliConfig.RepoId;

    Console.WriteLine($"Indexing '{path}'");
    Console.WriteLine($"  Index directory: {indexDir}");
    Console.WriteLine($"  Embedding model: {repoId}");
    Console.WriteLine();

    var stopwatch = Stopwatch.StartNew();
    IncrementalIndexingResult result = await indexer.RunAsync(path, indexDir, repoId);
    stopwatch.Stop();

    Console.WriteLine("=== Indexing Complete ===");
    Console.WriteLine($"  Files:          {result.Manifest.FileCount}");
    Console.WriteLine($"  Chunks:         {result.Manifest.ChunkCount}");
    Console.WriteLine($"  Chunks added:   {result.ChunksAdded}");
    Console.WriteLine($"  Chunks removed: {result.ChunksDeactivated}");
    Console.WriteLine($"  Index written:  {result.IndexWritten}");
    Console.WriteLine($"  Duration:       {stopwatch.ElapsedMilliseconds} ms");
    Console.WriteLine();
    Console.WriteLine($"  Formats:   {string.Join(", ", result.Manifest.Formats)}");
    Console.WriteLine($"  Model:     {result.Manifest.ModelId}");
    Console.WriteLine($"  Dimension: {result.Manifest.Dimension}");

    if (result.Changes.Added.Count > 0 || result.Changes.Modified.Count > 0 || result.Changes.Removed.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Change summary:");
        Console.WriteLine($"    Added:     {result.Changes.Added.Count}");
        Console.WriteLine($"    Modified:  {result.Changes.Modified.Count}");
        Console.WriteLine($"    Removed:   {result.Changes.Removed.Count}");
        Console.WriteLine($"    Unchanged: {result.Changes.Unchanged.Count}");
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("  No changes detected. Index left untouched.");
    }

    return 0;
}

// =====================================================================
// Command: search <query...> [--index-dir <dir>] [--top-k <n>]
// =====================================================================
static async Task<int> RunSearchAsync(ServiceProvider provider, CommandLineParser args)
{
    if (args.PositionalArgs.Count < 1)
    {
        Console.Error.WriteLine("Usage: vectra search <query> [--index-dir <dir>] [--top-k <n>]");
        return 2;
    }

    string query = string.Join(' ', args.PositionalArgs);
    int topK = ParseTopK(args.GetFlag("top-k"), defaultTopK: 5);

    string? indexDir = ResolveExistingIndex(provider, args.GetFlag("index-dir"));
    if (indexDir is null)
    {
        return 1;
    }

    var session = provider.GetRequiredService<IVectraIndexSession>();
    await session.OpenAsync(indexDir);

    var engine = provider.GetRequiredService<IInferenceEngine>();
    var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();

    Console.WriteLine($"Searching for: \"{query}\" (top-{topK})");
    Console.WriteLine($"  Index: {indexDir}");
    Console.WriteLine($"  Model: {session.Manifest.ModelId} (dim={session.Manifest.Dimension})");
    Console.WriteLine();

    var embedStopwatch = Stopwatch.StartNew();
    EmbeddingResponse embedResponse = await engine.GenerateEmbeddingsAsync(
        session.Manifest.ModelId,
        new EmbeddingRequest(query, Normalize: true));
    embedStopwatch.Stop();

    var searchStopwatch = Stopwatch.StartNew();
    IReadOnlyList<SearchResult> matches = await searchEngine.SearchAsync(embedResponse.Embedding, topK);
    searchStopwatch.Stop();

    Console.WriteLine($"=== Search Results (Top-{topK}) ===");
    Console.WriteLine($"  Embedding time: {embedStopwatch.Elapsed.TotalMilliseconds:F1} ms");
    Console.WriteLine($"  Search time:    {searchStopwatch.Elapsed.TotalMilliseconds:F2} ms");
    Console.WriteLine();

    if (matches.Count == 0)
    {
        Console.WriteLine("No results found.");
        return 0;
    }

    for (int i = 0; i < matches.Count; i++)
    {
        SearchResult match = matches[i];
        string text = await session.GetChunkTextAsync(match.ChunkId);

        Console.WriteLine($"#{i + 1}");
        Console.WriteLine($"  Chunk ID: {match.ChunkId}");
        Console.WriteLine($"  File ID:  {match.FileId}");
        Console.WriteLine($"  Page:     {match.PageNumber}");
        Console.WriteLine($"  Score:    {match.Score:F4}");
        Console.WriteLine($"  Text:     {Truncate(text, 200)}");
        Console.WriteLine();
    }

    return 0;
}

// =====================================================================
// Command: ask <question...> [--index-dir <dir>] [--top-k <n>]
// =====================================================================
static async Task<int> RunAskAsync(ServiceProvider provider, CommandLineParser args)
{
    if (args.PositionalArgs.Count < 1)
    {
        Console.Error.WriteLine("Usage: vectra ask <question> [--index-dir <dir>] [--top-k <n>]");
        return 2;
    }

    string question = string.Join(' ', args.PositionalArgs);
    int topK = ParseTopK(args.GetFlag("top-k"), defaultTopK: 3);

    string? indexDir = ResolveExistingIndex(provider, args.GetFlag("index-dir"));
    if (indexDir is null)
    {
        return 1;
    }

    var session = provider.GetRequiredService<IVectraIndexSession>();
    await session.OpenAsync(indexDir);

    var rag = provider.GetRequiredService<IRagService>();

    Console.WriteLine($"Question: {question}");
    Console.WriteLine();

    RagRetrievalResult retrieval = await rag.RetrieveAsync(
        new RagRequest(question, session.Manifest.ModelId, topK));

    Console.WriteLine($"=== Retrieved Context (Top-{topK}) ===");
    for (int i = 0; i < retrieval.Context.Count; i++)
    {
        RagContextChunk chunk = retrieval.Context[i];
        Console.WriteLine($"  [{i + 1}] page {chunk.PageNumber} | score {chunk.Score:F4} | {Truncate(chunk.Snippet, 120)}");
    }

    Console.WriteLine();
    Console.WriteLine("=== Answer (streamed from chat gateway) ===");
    await foreach (string token in rag.StreamAnswerAsync(retrieval))
    {
        Console.Write(token);
    }

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine($"Citations: {string.Join(", ", retrieval.Context.Select(c => $"p.{c.PageNumber}"))}");

    return 0;
}

// =====================================================================
// Command: status [--index-dir <dir>]
// =====================================================================
static async Task<int> RunStatusAsync(ServiceProvider provider, CommandLineParser args)
{
    string? indexDir = ResolveExistingIndex(provider, args.GetFlag("index-dir"));
    if (indexDir is null)
    {
        return 1;
    }

    var reader = provider.GetRequiredService<IIndexReader>();
    DiskIndexManifest manifest = await reader.ReadManifestAsync(indexDir);

    Console.WriteLine("=== Index Status ===");
    Console.WriteLine($"  Directory: {indexDir}");
    Console.WriteLine($"  Version:   {manifest.Version}");
    Console.WriteLine($"  Model:     {manifest.ModelId}");
    Console.WriteLine($"  Dimension: {manifest.Dimension}");
    Console.WriteLine($"  Formats:   {string.Join(", ", manifest.Formats)}");
    Console.WriteLine($"  Files:     {manifest.FileCount}");
    Console.WriteLine($"  Chunks:    {manifest.ChunkCount}");
    Console.WriteLine($"  Created:   {manifest.CreatedAt:O}");
    Console.WriteLine($"  Updated:   {manifest.UpdatedAt:O}");

    return 0;
}

// =====================================================================
// Command: doctor [--index-dir <dir>]
// =====================================================================
static async Task<int> RunDoctorAsync(ServiceProvider provider, CommandLineParser args)
{
    string? indexDir = ResolveExistingIndex(provider, args.GetFlag("index-dir"));
    if (indexDir is null)
    {
        return 1;
    }

    Console.WriteLine($"=== Doctor Report for {indexDir} ===");
    Console.WriteLine();

    int errors = 0;

    // Phase 1: check presence of all six artifacts.
    foreach (string artifact in IndexArtifactNames.All)
    {
        string artifactPath = Path.Combine(indexDir, artifact);
        if (File.Exists(artifactPath))
        {
            long size = new FileInfo(artifactPath).Length;
            Console.WriteLine($"  [OK  ] {artifact} ({size} bytes)");
        }
        else
        {
            Console.WriteLine($"  [FAIL] {artifact} — MISSING");
            errors++;
        }
    }

    Console.WriteLine();

    if (errors > 0)
    {
        Console.WriteLine($"Doctor found {errors} missing artifact(s). Rebuild the index with 'vectra index <path>'.");
        return 1;
    }

    // Phase 2: consistency checks.
    try
    {
        var reader = provider.GetRequiredService<IIndexReader>();
        DiskIndexManifest manifest = await reader.ReadManifestAsync(indexDir);
        IReadOnlyList<FileState> fileStates = await reader.ReadFileStatesAsync(indexDir);
        IReadOnlyList<ChunkMapEntry> chunkMap = await reader.ReadChunkMapAsync(indexDir);
        ReadOnlyMemory<float> vectors = await reader.ReadVectorsAsync(indexDir);

        Console.WriteLine("=== Consistency Checks ===");

        if (fileStates.Count != manifest.FileCount)
        {
            Console.WriteLine($"  [FAIL] file.state.json has {fileStates.Count} entries, manifest declares {manifest.FileCount}");
            errors++;
        }
        else
        {
            Console.WriteLine($"  [OK  ] file.state.json: {fileStates.Count} entries");
        }

        if (chunkMap.Count != manifest.ChunkCount)
        {
            Console.WriteLine($"  [FAIL] chunk.map.json has {chunkMap.Count} entries, manifest declares {manifest.ChunkCount}");
            errors++;
        }
        else
        {
            Console.WriteLine($"  [OK  ] chunk.map.json: {chunkMap.Count} entries");
        }

        int expectedVectorCount = manifest.ChunkCount * manifest.Dimension;
        if (vectors.Length != expectedVectorCount)
        {
            Console.WriteLine($"  [FAIL] vectors.bin has {vectors.Length} floats, expected {expectedVectorCount}");
            errors++;
        }
        else
        {
            Console.WriteLine($"  [OK  ] vectors.bin: {vectors.Length} floats ({vectors.Length * sizeof(float)} bytes)");
        }

        int activeChunks = chunkMap.Count(c => c.IsActive);
        int inactiveChunks = chunkMap.Count - activeChunks;
        Console.WriteLine($"  [INFO] Active chunks: {activeChunks}, soft-deleted: {inactiveChunks}");

        foreach (IGrouping<IndexFileStatus, FileState> group in fileStates.GroupBy(f => f.Status))
        {
            Console.WriteLine($"  [INFO] Files with status {group.Key}: {group.Count()}");
        }

        Console.WriteLine($"  [INFO] Model: {manifest.ModelId}, dimension: {manifest.Dimension}");
    }
    catch (IndexCorruptedException ex)
    {
        Console.WriteLine($"  [FAIL] Index corruption detected: {ex.Message}");
        errors++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [FAIL] Unexpected error during consistency check: {ex.Message}");
        errors++;
    }

    Console.WriteLine();
    if (errors == 0)
    {
        Console.WriteLine("Doctor: index is healthy.");
        return 0;
    }

    Console.WriteLine($"Doctor: {errors} issue(s) found.");
    return 1;
}

// =====================================================================
// Command: probe-chat (preserved from previous CLI revision)
// =====================================================================
static async Task<int> RunProbeChatAsync(ServiceProvider provider)
{
    var probe = provider.GetRequiredService<ChatContractProbe>();
    IReadOnlyList<ChatProbeStep> steps = await probe.RunAsync();

    bool allOk = true;
    foreach (ChatProbeStep step in steps)
    {
        string status = step.Succeeded ? "[OK  ]" : "[FAIL]";
        string detail = step.Detail is null ? string.Empty : $" :: {step.Detail}";
        string error = step.ServerError is null ? string.Empty : $" ({step.ServerError})";
        Console.WriteLine($"{status} {step.Operation} -> '{step.WireName}' ({step.ArgumentCount} arg(s)){detail}{error}");
        if (!step.Succeeded)
        {
            allOk = false;
        }
    }

    return allOk ? 0 : 1;
}

// =====================================================================
// Command: bench <pdf> [iterations] (preserved from previous CLI revision)
// =====================================================================
static async Task<int> RunBenchAsync(CommandLineParser args)
{
    if (args.PositionalArgs.Count < 1)
    {
        Console.Error.WriteLine("Usage: vectra bench <pdf> [iterations]");
        return 2;
    }

    string pdfPath = Path.GetFullPath(args.PositionalArgs[0]);
    int iterations = args.PositionalArgs.Count >= 2 && int.TryParse(args.PositionalArgs[1], out int parsedIterations)
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

// =====================================================================
// Command: stats (in-memory, preserved for REPL workflows)
// =====================================================================
static int RunStats(ServiceProvider provider)
{
    var store = provider.GetRequiredService<IVectorStore>();
    Console.WriteLine("=== Index Statistics (in-memory) ===");
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

// =====================================================================
// Helpers
// =====================================================================

static string? ResolveExistingIndex(ServiceProvider provider, string? explicitDir)
{
    var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
    string indexDir = resolver.ResolveIndexDirectory(Directory.GetCurrentDirectory(), explicitDir);

    if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
    {
        Console.Error.WriteLine($"Index not found at '{indexDir}'. Run 'vectra index <path>' first.");
        return null;
    }

    return indexDir;
}

static int ParseTopK(string? value, int defaultTopK)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultTopK;
    }

    if (int.TryParse(value, out int parsed) && parsed >= 1)
    {
        return parsed;
    }

    throw new ArgumentException($"Invalid --top-k value: '{value}'. Must be a positive integer.");
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

static int PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  vectra                                              Interactive REPL");
    Console.WriteLine("  vectra index <path> [--index-dir <dir>]             Index a file or folder");
    Console.WriteLine("  vectra update <path> [--index-dir <dir>]            Incremental re-index");
    Console.WriteLine("  vectra search <query> [--index-dir <dir>] [--top-k <n>]");
    Console.WriteLine("                                                      Search the on-disk index");
    Console.WriteLine("  vectra ask <question> [--index-dir <dir>] [--top-k <n>]");
    Console.WriteLine("                                                      Retrieve + streamed grounded answer");
    Console.WriteLine("  vectra status [--index-dir <dir>]                   Show manifest & statistics");
    Console.WriteLine("  vectra doctor [--index-dir <dir>]                   Verify index integrity");
    Console.WriteLine("  vectra probe-chat                                   Chat-gateway handshake probe");
    Console.WriteLine("  vectra bench <pdf> [iterations]                     Benchmark matrix (20/50/100 pages)");
    Console.WriteLine("  vectra stats                                        In-memory index statistics (REPL)");
    Console.WriteLine("  vectra help                                         Show this help");
    Console.WriteLine();
    Console.WriteLine("Index convention: <root>/.instancium/vectra/index/ (override with --index-dir).");
    Console.WriteLine("REPL commands: index, update, search, ask, status, doctor, probe-chat, bench, stats, help, exit.");
    return 0;
}

static int PrintUnknown(string? command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

internal static class VectraCliConfig
{
    private static readonly IConfiguration _configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddVectraConfiguration("config_vectra.json")
        .AddEnvironmentVariables()
        .Build();

    public const string ApiKeyVariable = "VECTRA_GATEWAY_API_KEY";
    public const string GatewayUrlVariable = "VECTRA_EMBEDDING_GATEWAY_URL";
    public const string ChatGatewayUrlVariable = "VECTRA_CHAT_GATEWAY_URL";
    public const string DefaultGatewayUrl = "http://localhost:5000";
    public const string DefaultChatGatewayUrl = "http://localhost:5000";

    public static string RepoId => _configuration["Vectra:Gateways:Embedding:RepoId"] ?? "qwen3-embedding-0.6b";
    public static string ChatRepoId => _configuration["Vectra:Gateways:Chat:RepoId"] ?? "qwen3-vl-8b-instruct";
    public static int DefaultTopK => GetIntValue("Vectra:Cli:DefaultTopK", 3);
    public static int DefaultBenchIterations => GetIntValue("Vectra:Cli:DefaultBenchIterations", 5);
    public static int MaxProbePages => GetIntValue("Vectra:Cli:MaxProbePages", 100_000);
    public static int[] BenchPageWindows => _configuration.GetSection("Vectra:Cli:BenchPageWindows").Get<int[]>() ?? [20, 50, 100];

    public static string[] BenchQueries => _configuration.GetSection("Vectra:Cli:BenchQueries").Get<string[]>() ??
    [
        "temperature measurement",
        "pressure transmitter",
        "flow meter",
        "control valve",
        "level sensor"
    ];

    private static int GetIntValue(string key, int fallback)
    {
        string? raw = _configuration[key];
        return int.TryParse(raw, out int value) ? value : fallback;
    }
}
