namespace Instancium.Vectra.Storage.Disk;

using Instancium.Vectra.Core.Contracts;

/// <summary>
/// Default <see cref="IInstanciumDirectoryResolver"/> implementation.
/// Knows the <c>.instancium</c> ecosystem convention: an index for root <c>X</c> lives at
/// <c>X/.instancium/vectra/index</c> unless an explicit base directory is provided, in which case
/// the index lives at <c>{explicit}/vectra/index</c>.
/// </summary>
public sealed class InstanciumDirectoryResolver : IInstanciumDirectoryResolver
{
    public const string InstanciumFolderName = ".instancium";
    public const string ProductFolderName = "vectra";
    public const string IndexFolderName = "index";

    private const string GitIgnoreFileName = ".gitignore";
    private const string GitKeepFileName = ".gitkeep";
    private const string GitIgnoreContent =
        "# Instancium ecosystem - derived artifacts\n" +
        "vectra/index/\n";

    public string ResolveIndexDirectory(string rootPath, string? explicitIndexDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!string.IsNullOrWhiteSpace(explicitIndexDir))
        {
            return Path.Combine(Path.GetFullPath(explicitIndexDir), ProductFolderName, IndexFolderName);
        }

        string fullPath = Path.GetFullPath(rootPath);
        string targetBaseDirectory;

        if (File.Exists(fullPath))
        {
            targetBaseDirectory = Directory.GetCurrentDirectory();
        }
        else if (Directory.Exists(fullPath))
        {
            targetBaseDirectory = fullPath;
        }
        else
        {
            throw new DirectoryNotFoundException($"Content root '{fullPath}' does not exist.");
        }

        return Path.Combine(targetBaseDirectory, InstanciumFolderName, ProductFolderName, IndexFolderName);
    }

    public void EnsureDirectoryStructure(string indexDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

        string fullIndexDirectory = Path.GetFullPath(indexDirectory);
        Directory.CreateDirectory(fullIndexDirectory);

        DirectoryInfo? productDirectory = Directory.GetParent(fullIndexDirectory);
        DirectoryInfo? ecosystemDirectory = productDirectory?.Parent;
        if (productDirectory is null || ecosystemDirectory is null)
        {
            return;
        }

        if (!string.Equals(ecosystemDirectory.Name, InstanciumFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureFile(Path.Combine(ecosystemDirectory.FullName, GitIgnoreFileName), GitIgnoreContent);
        EnsureFile(Path.Combine(productDirectory.FullName, GitKeepFileName), string.Empty);
    }

    private static void EnsureFile(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, content);
    }
}
