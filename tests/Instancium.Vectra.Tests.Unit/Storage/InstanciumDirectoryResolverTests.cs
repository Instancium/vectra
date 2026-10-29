namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InstanciumDirectoryResolverTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-stage1", Guid.NewGuid().ToString("N")));
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
    public void InstanciumDirectoryResolver_ShouldResolveConventionPath()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root);

        Assert.Equal(Path.Combine(_root, ".instancium", "vectra", "index"), indexDirectory);
    }

    [Fact]
    public void InstanciumDirectoryResolver_ShouldCreateGitignore()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root);

        _resolver.EnsureDirectoryStructure(indexDirectory);

        Assert.True(Directory.Exists(indexDirectory));

        string instanciumDirectory = Path.Combine(_root, ".instancium");
        string gitignorePath = Path.Combine(instanciumDirectory, ".gitignore");
        Assert.True(File.Exists(gitignorePath));

        string gitignore = File.ReadAllText(gitignorePath);
        Assert.Contains("# Instancium ecosystem", gitignore, StringComparison.Ordinal);
        Assert.Contains("vectra/index/", gitignore, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(instanciumDirectory, "vectra", ".gitkeep")));

        File.WriteAllText(gitignorePath, "custom-user-rules");
        _resolver.EnsureDirectoryStructure(indexDirectory);
        Assert.Equal("custom-user-rules", File.ReadAllText(gitignorePath));
    }

    [Fact]
    public void InstanciumDirectoryResolver_ShouldUseExplicitDir_WhenProvided()
    {
        string explicitBase = Path.Combine(_root, "data", "idx");

        string indexDirectory = _resolver.ResolveIndexDirectory(_root, explicitBase);

        Assert.Equal(Path.Combine(explicitBase, "vectra", "index"), indexDirectory);

        _resolver.EnsureDirectoryStructure(indexDirectory);
        Assert.True(Directory.Exists(indexDirectory));

        Assert.False(File.Exists(Path.Combine(explicitBase, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".instancium")));
    }

    [Fact]
    public void InstanciumDirectoryResolver_ShouldThrow_WhenConventionRootDoesNotExist()
    {
        string missingRoot = Path.Combine(_root, "missing-root");

        Assert.Throws<DirectoryNotFoundException>(() => _resolver.ResolveIndexDirectory(missingRoot));
    }

    [Fact]
    public void InstanciumDirectoryResolver_ShouldThrow_OnEmptyArguments()
    {
        Assert.ThrowsAny<ArgumentException>(() => _resolver.ResolveIndexDirectory("  "));
        Assert.ThrowsAny<ArgumentException>(() => _resolver.EnsureDirectoryStructure(" "));
    }
}
