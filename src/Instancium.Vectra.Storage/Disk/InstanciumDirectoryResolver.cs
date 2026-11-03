#nullable enable
namespace Instancium.Vectra.Storage.Disk;

using Instancium.Vectra.Core.Contracts;

/// <summary>
/// Resolves index directories against an explicitly provided content root.
/// Defect #17 fix: there is no Directory.GetCurrentDirectory() fallback anywhere.
/// When the root is a single file, its containing directory is used (deterministic);
/// otherwise the root must exist as a directory.
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
        string baseDirectory;
        if (File.Exists(fullPath))
        {
            baseDirectory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException($"Root '{fullPath}' has no parent directory.", nameof(rootPath));
        }
        else if (Directory.Exists(fullPath))
        {
            baseDirectory = fullPath;
        }
        else
        {
            throw new DirectoryNotFoundException($"Content root '{fullPath}' does not exist.");
        }

        return Path.Combine(baseDirectory, InstanciumFolderName, ProductFolderName, IndexFolderName);
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

