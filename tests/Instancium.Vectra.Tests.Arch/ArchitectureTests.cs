using System;
using System.Linq;
using System.Reflection;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Indexing.Pipeline;
using Instancium.Vectra.Infrastructure.Embeddings;
using Instancium.Vectra.Rag;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;

namespace Instancium.Vectra.Tests.Arch;

/// <summary>
/// Architecture guard tests. Enforces dependency direction and isolation rules
/// using plain reflection only (no third-party arch-testing dependencies).
/// </summary>
[Trait("Category", "Arch")]
public sealed class ArchitectureTests
{
    private static readonly Assembly Core = typeof(IVectorStore).Assembly;
    private static readonly Assembly Indexing = typeof(IndexingPipeline).Assembly;
    private static readonly Assembly Infrastructure = typeof(InstantAIGateEmbeddingAdapter).Assembly;
    private static readonly Assembly Rag = typeof(RagService).Assembly;
    private static readonly Assembly Search = typeof(InMemoryVectorSearchEngine).Assembly;
    private static readonly Assembly Storage = typeof(InMemoryVectorStore).Assembly;

    // ── Existing layering rules ────────────────────────────────────────────────

    [Fact]
    public void Core_ShouldNotReferenceVectraLayersOrGateway()
    {
        Assert.DoesNotContain(References(Core), name => name.StartsWith("Instancium.Vectra", StringComparison.Ordinal) && name != "Instancium.Vectra.Core");
        Assert.DoesNotContain(References(Core), name => name.StartsWith("InstantAIGate", StringComparison.Ordinal));
    }

    [Fact]
    public void Indexing_ShouldNotReferenceInfrastructureOrSignalR()
    {
        Assert.DoesNotContain(References(Indexing), name => name == "Instancium.Vectra.Infrastructure");
        Assert.DoesNotContain(References(Indexing), name => name.StartsWith("Microsoft.AspNetCore.SignalR", StringComparison.Ordinal));
    }

    [Fact]
    public void StorageAndSearch_ShouldReferenceOnlyCore()
    {
        foreach (Assembly assembly in new[] { Storage, Search })
        {
            Assert.DoesNotContain(References(assembly), name => name.StartsWith("InstantAIGate", StringComparison.Ordinal));
            Assert.DoesNotContain(References(assembly), name => name == "Instancium.Vectra.Infrastructure");
            Assert.DoesNotContain(References(assembly), name => name == "Instancium.Vectra.Indexing");
        }
    }

    [Fact]
    public void SignalR_ShouldBeKnownOnlyToInfrastructure()
    {
        foreach (Assembly assembly in new[] { Core, Indexing, Search, Storage })
        {
            Assert.DoesNotContain(References(assembly), name => name.StartsWith("Microsoft.AspNetCore.SignalR", StringComparison.Ordinal));
        }

        Assert.Contains(References(Infrastructure), name => name == "Microsoft.AspNetCore.SignalR.Client");
    }

    [Fact]
    public void Infrastructure_ShouldNotReferenceUpperLayers()
    {
        Assert.DoesNotContain(References(Infrastructure), name => name == "Instancium.Vectra.Indexing");
        Assert.DoesNotContain(References(Infrastructure), name => name == "Instancium.Vectra.Search");
        Assert.DoesNotContain(References(Infrastructure), name => name == "Instancium.Vectra.Storage");
    }

    [Fact]
    public void Rag_ShouldNotReferenceInfrastructureIndexingOrSignalR()
    {
        Assert.DoesNotContain(References(Rag), name => name == "Instancium.Vectra.Infrastructure");
        Assert.DoesNotContain(References(Rag), name => name == "Instancium.Vectra.Indexing");
        Assert.DoesNotContain(References(Rag), name => name.StartsWith("Microsoft.AspNetCore.SignalR", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_ShouldNotReferenceRag()
    {
        Assert.DoesNotContain(References(Infrastructure), name => name == "Instancium.Vectra.Rag");
    }

    // ── P0 rule: Core references only System.* ─────────────────────────────────

    [Fact]
    public void Core_ShouldReferenceOnlySystemAssemblies()
    {
        string[] nonSystem = References(Core)
            .Where(name => !IsSystemAssembly(name))
            .ToArray();

        Assert.Empty(nonSystem);
    }

    // ── P0 rule: Storage/Search must not reference gateway or PDF libraries ────

    [Fact]
    public void StorageAndSearch_ShouldNotReferenceSignalRPdfPigOrInstantAIGate()
    {
        foreach (Assembly assembly in new[] { Storage, Search })
        {
            Assert.DoesNotContain(
                References(assembly),
                name => name.StartsWith("Microsoft.AspNetCore.SignalR", StringComparison.Ordinal));
            Assert.DoesNotContain(
                References(assembly),
                name => name.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal));
            Assert.DoesNotContain(
                References(assembly),
                name => name.StartsWith("InstantAIGate", StringComparison.Ordinal));
        }
    }

    private static bool IsSystemAssembly(string name) =>
        name is "System" or "netstandard" or "mscorlib" ||
        name.StartsWith("System.", StringComparison.Ordinal);

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToArray();
}