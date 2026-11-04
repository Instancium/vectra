using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using System.Reflection;
using Xunit;

namespace Instancium.Vectra.Tests.Arch;

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
        string[] forbidden = References(Search)
            .Where(name => !IsSystemAssembly(name)
                && name != "Instancium.Vectra.Core"
                && name != "Instancium.Vectra.Storage" // ALLOWED IN P5 to build VectraEngine
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

    [Fact]
    public void VectraCoreAndSearch_ShouldNotReferenceNativeAssembliesDirectly()
    {
        // ARCHITECTURAL INVARIANT (TZ §3.2, §7 P8):
        // Core and Search must remain 100% pure managed assemblies.
        // Native acceleration MUST be loaded dynamically or via external pluggable packages.
        foreach (Assembly assembly in new[] { Core, Storage, Search })
        {
            Assert.DoesNotContain(
                References(assembly),
                name => name.Contains(".Native", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void VectraOptions_MustExposeExtensibilityPointForNativeKernels()
    {
        // ARCHITECTURAL INVARIANT:
        // This reflection test prevents AI assistants or future maintainers from accidentally removing
        // or renaming the CustomKernelResolver property during refactoring.
        Type optionsType = typeof(VectraOptions);
        PropertyInfo? property = optionsType.GetProperty(nameof(VectraOptions.CustomKernelResolver));

        Assert.NotNull(property);
        Assert.Equal(typeof(Func<SimilarityMetric, ISimilarityKernel>), property.PropertyType);
    }

    private static bool IsSystemAssembly(string name) =>
        name is "System" or "netstandard" or "mscorlib" ||
        name.StartsWith("System.", StringComparison.Ordinal);

    private static string[] References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToArray();
}