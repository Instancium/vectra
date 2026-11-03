#nullable enable
namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InstanciumDirectoryResolverTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-resolver-v2", Guid.NewGuid().ToString("N")));
    private readonly InstanciumDirectoryResolver _resolver = new();

    public InstanciumDirectoryResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Resolver_ShouldResolveConventionPath()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root);
        Assert.Equal(Path.Combine(_root, ".instancium", "vectra", "index"), indexDirectory);
    }

    [Fact]
    public void Resolver_ShouldUseExplicitDir_WhenProvided()
    {
        string explicitBase = Path.Combine(_root, "data", "idx");
        string indexDirectory = _resolver.ResolveIndexDirectory(_root, explicitBase);
        Assert.Equal(Path.Combine(explicitBase, "vectra", "index"), indexDirectory);
    }

    [Fact]
    public void Resolver_ShouldUseParentDirectory_WhenRootIsSingleFile()
    {
        string tempFile = Path.Combine(_root, "single.pdf");
        File.WriteAllText(tempFile, "%PDF-stub");
        string indexDirectory = _resolver.ResolveIndexDirectory(tempFile);
        Assert.Equal(Path.Combine(_root, ".instancium", "vectra", "index"), indexDirectory);
    }

    [Fact]
    public void Resolver_ShouldCreateGitignore()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root);
        _resolver.EnsureDirectoryStructure(indexDirectory);
        string gitignorePath = Path.Combine(_root, ".instancium", ".gitignore");
        Assert.True(File.Exists(gitignorePath));
        Assert.Contains("vectra/index/", File.ReadAllText(gitignorePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_ShouldThrow_WhenRootDoesNotExist()
    {
        Assert.Throws<DirectoryNotFoundException>(() => _resolver.ResolveIndexDirectory(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Resolver_ShouldThrow_OnEmptyArguments()
    {
        Assert.ThrowsAny<ArgumentException>(() => _resolver.ResolveIndexDirectory("  "));
        Assert.ThrowsAny<ArgumentException>(() => _resolver.EnsureDirectoryStructure(" "));
    }
}

