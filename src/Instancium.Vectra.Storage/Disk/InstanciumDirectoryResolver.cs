#nullable enable
namespace Instancium.Vectra.Storage.Disk;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;

/// <summary>
/// Resolves index directories against an explicitly configured base directory.
///
/// Defect #17 fix: there is no implicit Directory.GetCurrentDirectory() fallback.
/// The base directory comes from an explicit constructor argument or from the
/// INSTANCIUM_VECTRA_HOME environment variable; otherwise construction fails fast,
/// because an index whose location depends on the process CWD is unreproducible.
/// </summary>
public sealed class InstanciumDirectoryResolver : IInstanciumDirectoryResolver
{
    public const string EnvironmentVariableName = "INSTANCIUM_VECTRA_HOME";
    public const string InstanciumFolderName = ".instancium";
    public const string ProductFolderName = "vectra";
    public const string IndexFolderName = "index";

    private const string GitIgnoreFileName = ".gitignore";
    private const string GitKeepFileName = ".gitkeep";
    private const string GitIgnoreContent =
        "# Instancium ecosystem - derived artifacts\n" +
        "vectra/index/\n";

    private readonly string _baseDirectory;

    public InstanciumDirectoryResolver(string? baseDirectory = null)
    {
        string? root = baseDirectory ?? Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new VectraException(
                "Vectra base directory is not configured. Pass it explicitly to AddVectraStorage(baseDirectory) " +
                "or set the " + EnvironmentVariableName + " environment variable.");
        }

        _baseDirectory = Path.GetFullPath(root);
    }

    public string ResolveIndexDirectory(string rootPath, string? explicitIndexDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        if (!string.IsNullOrWhiteSpace(explicitIndexDir))
        {
            return Path.GetFullPath(Path.Combine(_baseDirectory, explicitIndexDir));
        }

        string fullPath = Path.GetFullPath(rootPath);
        if (Path.IsPathRooted(fullPath))
        {
            return fullPath;
        }

        return Path.Combine(_baseDirectory, fullPath);
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
