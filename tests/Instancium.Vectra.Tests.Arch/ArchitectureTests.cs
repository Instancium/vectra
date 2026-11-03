using System;
using System.Linq;
using System.Reflection;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Xunit;

namespace Instancium.Vectra.Tests.Arch;

/// <summary>
/// Architecture guard tests for the v2 engine perimeter: Core, Search, Storage.
/// Legacy satellite projects are excluded from the solution (TZ §3.3) and are no
/// longer part of the guarded surface.
/// DI abstractions (Microsoft.Extensions.*) are allowed: they are composition seams,
/// not infrastructure. Forbidden: SignalR, PdfPig, InstantAIGate, sibling layers.
/// </summary>
[Trait("Category", "Arch")]
public sealed class ArchitectureTests
{
    private static readonly Assembly Core = typeof(IVectorStore).Assembly;
    private static readonly Assembly Search = typeof(InMemoryVectorSearchEngine).Assembly;
    private static readonly Assembly Storage = typeof(InMemoryVectorStore).Assembly;

    [Fact]
    public void Core_ShouldReferenceOnlySystemAssemblies()
    {
        string[] nonSystem = References(Core)
            .Where(name => !IsSystemAssembly(name))
            .ToArray();

        Assert.Empty(nonSystem);
    }

    [Fact]
    public void Storage_ShouldReferenceOnlyCoreSystemAndDependencyInjection()
    {
        string[] forbidden = References(Storage)
            .Where(name => !IsSystemAssembly(name)
                && name != "Instancium.Vectra.Core"
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Search_ShouldReferenceOnlyCoreSystemAndDependencyInjection()
    {
        // Search depends only on Core contracts (IVectorStore); concrete Storage
        // implementations are injected via DI and must not be referenced at compile time.
        string[] forbidden = References(Search)
            .Where(name => !IsSystemAssembly(name)
                && name != "Instancium.Vectra.Core"
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbidden);
    }

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