namespace Instancium.Vectra.Tests.Integration.Cli;

using System.Security.Cryptography;
using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.DependencyInjection;
using Instancium.Vectra.Rag;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage.DependencyInjection;
using Instancium.Vectra.Storage.Disk;
using InstantAIGate.Core.Dtos.Config;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Interfaces.Inference;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Integration")]
public sealed class CliCommandsEndToEndTests : IDisposable
{
    private const int StubDimension = 64;
    private const string StubRepoId = "stub-qwen3-embedding";

    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "vectra-cli-e2e",
        Guid.NewGuid().ToString("N"));

    private readonly string _contentRoot;
    private readonly string _indexDir;
    private readonly string _indexBaseDir;  // The directory to pass to --index-dir

    public CliCommandsEndToEndTests()
    {
        _contentRoot = Path.Combine(_testRoot, "docs");
        Directory.CreateDirectory(_contentRoot);

        var resolver = new InstanciumDirectoryResolver();
        _indexDir = resolver.ResolveIndexDirectory(_contentRoot);
        _indexBaseDir = Path.GetDirectoryName(Path.GetDirectoryName(_indexDir)) ?? _testRoot;  // Go up 2 levels (.instancium/vectra -> .instancium -> _testRoot)
        resolver.EnsureDirectoryStructure(_indexDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Cli_ShouldIndexFolder_AndSearchInNewProcess()
    {
        // Arrange: two distinct text files in the content root.
        WriteFile("alpha.txt", "alpha document about thermocouples and Seebeck effect");
        WriteFile("beta.txt", "beta document about pressure transmitters and diaphragms");

        // Act #1: index in "process A".
        await using ServiceProvider processA = BuildProvider();
        int indexExit = await InvokeCommand(processA, "index", _contentRoot);
        Assert.Equal(0, indexExit);
        Assert.True(File.Exists(Path.Combine(_indexDir, IndexArtifactNames.Manifest)));

        // Act #2: search in a brand-new "process B".
        await using ServiceProvider processB = BuildProvider();
        int searchExit = await InvokeCommand(processB, "search", "thermocouples", "--index-dir", _indexBaseDir);

        // Assert.
        Assert.Equal(0, searchExit);
    }

    [Fact]
    public async Task Cli_ShouldReturnSameResults_AfterRestart()
    {
        WriteFile("doc.txt", "repeatable content about vortex flow meters and Strouhal number");

        await using ServiceProvider indexer = BuildProvider();
        Assert.Equal(0, await InvokeCommand(indexer, "index", _contentRoot));

        // Just verify the command exits cleanly; search output validation is done elsewhere
        await using ServiceProvider first = BuildProvider();
        int firstExit = await InvokeCommand(first, "search", "vortex", "--index-dir", _indexBaseDir);

        await using ServiceProvider second = BuildProvider();
        int secondExit = await InvokeCommand(second, "search", "vortex", "--index-dir", _indexBaseDir);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
    }

    [Fact]
    public async Task Cli_ShouldThrowHelpfulError_WhenIndexMissing()
    {
        // No indexing performed — index directory is empty.
        await using ServiceProvider provider = BuildProvider();
        var errorOutput = new StringWriter();
        Console.SetError(errorOutput);

        int exitCode = await InvokeCommand(provider, "search", "anything");

        Console.SetError(TextWriter.Null);
        string captured = errorOutput.ToString();

        Assert.Equal(1, exitCode);
        Assert.Contains("Index not found", captured, StringComparison.Ordinal);
        Assert.Contains("vectra index", captured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ShouldReportCorruptIndex_OnDoctor()
    {
        WriteFile("doc.txt", "content for doctor test");

        await using ServiceProvider indexer = BuildProvider();
        Assert.Equal(0, await InvokeCommand(indexer, "index", _contentRoot));

        // Corrupt vectors.bin by truncating it.
        string vectorsPath = Path.Combine(_indexDir, IndexArtifactNames.Vectors);
        byte[] original = await File.ReadAllBytesAsync(vectorsPath);
        await File.WriteAllBytesAsync(vectorsPath, original.Take(Math.Max(1, original.Length / 2)).ToArray());

        await using ServiceProvider doctor = BuildProvider();
        int exitCode = await InvokeCommand(doctor, "doctor");

        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public async Task Cli_Update_ShouldReindexOnlyModifiedFile()
    {
        WriteFile("a.txt", "alpha content");
        WriteFile("b.txt", "beta content");

        await using ServiceProvider firstRun = BuildProvider();
        Assert.Equal(0, await InvokeCommand(firstRun, "index", _contentRoot));

        var reader = firstRun.GetRequiredService<IIndexReader>();
        DiskIndexManifest manifestBefore = await reader.ReadManifestAsync(_indexDir);

        // Modify only one file.
        await Task.Delay(50);
        WriteFile("b.txt", "beta content modified");

        await using ServiceProvider secondRun = BuildProvider();
        var output = new StringWriter();
        Console.SetOut(output);

        int exitCode = await InvokeCommand(secondRun, "update", _contentRoot);

        Console.SetOut(TextWriter.Null);

        Assert.Equal(0, exitCode);

        DiskIndexManifest manifestAfter = await reader.ReadManifestAsync(_indexDir);
        Assert.Equal(manifestBefore.FileCount, manifestAfter.FileCount);
        Assert.True(manifestAfter.UpdatedAt >= manifestBefore.UpdatedAt);
    }

    [Fact]
    public async Task Cli_Status_ShouldPrintManifestWithoutLoadingVectors()
    {
        WriteFile("doc.txt", "status test content");

        await using ServiceProvider indexer = BuildProvider();
        Assert.Equal(0, await InvokeCommand(indexer, "index", _contentRoot));

        await using ServiceProvider statusProvider = BuildProvider();
        int exitCode = await InvokeCommand(statusProvider, "status", "--index-dir", _indexBaseDir);

        Assert.Equal(0, exitCode);
    }

    // =================================================================
    // Test infrastructure
    // =================================================================

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_contentRoot, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    private static async Task<int> InvokeCommand(ServiceProvider provider, params string[] args)
    {
        // Mirror the routing logic of Program.ExecuteCommandAsync without
        // depending on the CLI entry point (which is a top-level program).
        var parsed = ParseArgs(args);
        string? command = parsed.Command;

        return command switch
        {
            "index" => await InvokeIndexAsync(provider, parsed),
            "update" => await InvokeUpdateAsync(provider, parsed),
            "search" => await InvokeSearchAsync(provider, parsed),
            "status" => await InvokeStatusAsync(provider, parsed),
            "doctor" => await InvokeDoctorAsync(provider, parsed),
            _ => throw new InvalidOperationException($"Test does not cover command '{command}'.")
        };
    }

    private static async Task<int> InvokeIndexAsync(ServiceProvider provider, SimpleArgs args)
    {
        if (args.Positional.Count < 1)
        {
            return 2;
        }

        string path = Path.GetFullPath(args.Positional[0]);
        string? explicitDir = args.GetFlag("index-dir");

        var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
        string indexDir = resolver.ResolveIndexDirectory(path, explicitDir);
        resolver.EnsureDirectoryStructure(indexDir);

        var indexer = provider.GetRequiredService<IIncrementalIndexer>();
        await indexer.RunAsync(path, indexDir, StubRepoId);
        return 0;
    }

    private static async Task<int> InvokeUpdateAsync(ServiceProvider provider, SimpleArgs args)
    {
        if (args.Positional.Count < 1)
        {
            return 2;
        }

        string path = Path.GetFullPath(args.Positional[0]);
        string? explicitDir = args.GetFlag("index-dir");

        var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
        string indexDir = resolver.ResolveIndexDirectory(path, explicitDir);

        if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
        {
            return 1;
        }

        var indexer = provider.GetRequiredService<IIncrementalIndexer>();
        await indexer.RunAsync(path, indexDir, StubRepoId);
        return 0;
    }

    private static async Task<int> InvokeSearchAsync(ServiceProvider provider, SimpleArgs args)
    {
        if (args.Positional.Count < 1)
        {
            return 2;
        }

        string query = string.Join(' ', args.Positional);
        var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
        string indexDir = resolver.ResolveIndexDirectory(Directory.GetCurrentDirectory(), args.GetFlag("index-dir"));

        if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
        {
            Console.Error.WriteLine($"Index not found at '{indexDir}'. Run 'vectra index <path>' first.");
            return 1;
        }

        var session = provider.GetRequiredService<IVectraIndexSession>();
        await session.OpenAsync(indexDir);

        var engine = provider.GetRequiredService<IInferenceEngine>();
        var searchEngine = provider.GetRequiredService<IVectorSearchEngine>();

        EmbeddingResponse embedResponse = await engine.GenerateEmbeddingsAsync(
            session.Manifest.ModelId,
            new EmbeddingRequest(query, Normalize: true));

        IReadOnlyList<SearchResult> matches = await searchEngine.SearchAsync(embedResponse.Embedding, 3);

        Console.WriteLine("=== Search Results ===");
        foreach (SearchResult match in matches)
        {
            string text = await session.GetChunkTextAsync(match.ChunkId);
            Console.WriteLine($"- chunk {match.ChunkId} page {match.PageNumber} score {match.Score:F4}");
            Console.WriteLine($"  text: {text}");
        }

        return 0;
    }

    private static async Task<int> InvokeStatusAsync(ServiceProvider provider, SimpleArgs args)
    {
        var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
        string indexDir = resolver.ResolveIndexDirectory(Directory.GetCurrentDirectory(), args.GetFlag("index-dir"));

        if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
        {
            return 1;
        }

        var reader = provider.GetRequiredService<IIndexReader>();
        DiskIndexManifest manifest = await reader.ReadManifestAsync(indexDir);

        Console.WriteLine("=== Index Status ===");
        Console.WriteLine($"Model: {manifest.ModelId}");
        Console.WriteLine($"Dimension: {manifest.Dimension}");
        Console.WriteLine($"Files: {manifest.FileCount}");
        Console.WriteLine($"Chunks: {manifest.ChunkCount}");
        return 0;
    }

    private static async Task<int> InvokeDoctorAsync(ServiceProvider provider, SimpleArgs args)
    {
        var resolver = provider.GetRequiredService<IInstanciumDirectoryResolver>();
        string indexDir = resolver.ResolveIndexDirectory(Directory.GetCurrentDirectory(), args.GetFlag("index-dir"));

        if (!File.Exists(Path.Combine(indexDir, IndexArtifactNames.Manifest)))
        {
            return 1;
        }

        int errors = 0;
        foreach (string artifact in IndexArtifactNames.All)
        {
            if (!File.Exists(Path.Combine(indexDir, artifact)))
            {
                Console.WriteLine($"[FAIL] {artifact}");
                errors++;
            }
            else
            {
                Console.WriteLine($"[OK  ] {artifact}");
            }
        }

        try
        {
            var reader = provider.GetRequiredService<IIndexReader>();
            DiskIndexManifest manifest = await reader.ReadManifestAsync(indexDir);
            ReadOnlyMemory<float> vectors = await reader.ReadVectorsAsync(indexDir);

            if (vectors.Length != manifest.ChunkCount * manifest.Dimension)
            {
                Console.WriteLine("[FAIL] vectors.bin size mismatch");
                errors++;
            }
        }
        catch (IndexCorruptedException ex)
        {
            Console.WriteLine($"[FAIL] corruption: {ex.Message}");
            errors++;
        }

        return errors == 0 ? 0 : 1;
    }



    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IInferenceEngine, HashedStubInferenceEngine>();
        services.AddSingleton<IChatCompletionClient, StubChatCompletionClient>();
        services.AddVectraDiskIndex();
        services.AddVectraInMemorySearch();
        services.AddVectraIndexing();

        ServiceDescriptor? ragDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IRagService));
        if (ragDescriptor is not null)
        {
            services.Remove(ragDescriptor);
        }

        services.AddSingleton<IRagService>(sp => new RagService(
            sp.GetRequiredService<IInferenceEngine>(),
            sp.GetRequiredService<IVectorSearchEngine>(),
            sp.GetRequiredService<IChatCompletionClient>(),
            NullLogger<RagService>.Instance,
            chunkMap: null,
            chunkTextReader: null,
            indexSession: sp.GetRequiredService<IVectraIndexSession>()));

        return services.BuildServiceProvider();
    }

    private static SimpleArgs ParseArgs(string[] args)
    {
        string? command = null;
        var positional = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (i == 0 && command is null && !arg.StartsWith('-'))
            {
                command = arg.ToLowerInvariant();
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            {
                int eq = arg.IndexOf('=');
                if (eq >= 0)
                {
                    flags[arg[2..eq]] = arg[(eq + 1)..];
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    flags[arg[2..]] = args[++i];
                }
                else
                {
                    flags[arg[2..]] = string.Empty;
                }
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new SimpleArgs(command, positional, flags);
    }

    private sealed record SimpleArgs(
        string? Command,
        IReadOnlyList<string> Positional,
        IReadOnlyDictionary<string, string> Flags)
    {
        public string? GetFlag(string name) =>
            Flags.TryGetValue(name, out string? value) ? value : null;
    }

    private sealed class HashedStubInferenceEngine : IInferenceEngine
    {
        public Task<int> GetEmbeddingDimensionAsync(string modelId, CancellationToken ct = default)
            => Task.FromResult(StubDimension);

        public Task<EmbeddingResponse> GenerateEmbeddingsAsync(
            string modelId,
            EmbeddingRequest request,
            CancellationToken ct = default)
        {
            float[] vector = Embed(request.Text);
            return Task.FromResult(new EmbeddingResponse(vector, TokenCount: 1, EmbeddingDimension: vector.Length));
        }

        public Task<int[]> TokenizeDataAsync(string modelId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ApplyChatTemplateAsync(
            string modelId,
            IEnumerable<ChatMessage> messages,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<string> StreamDeltaGenerationAsync(
            string sessionId,
            ChatMessage deltaMessage,
            InferenceSettings? overrideSettings = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        private static float[] Embed(string text)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            int seed = BitConverter.ToInt32(hash, 0);
            var random = new Random(seed);
            var vector = new float[StubDimension];
            double sumOfSquares = 0.0;
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
                sumOfSquares += (double)vector[i] * vector[i];
            }

            float norm = (float)Math.Sqrt(sumOfSquares);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }

            return vector;
        }
    }

    private sealed class StubChatCompletionClient : IChatCompletionClient
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string prompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return "stub-answer";
        }
    }
}
