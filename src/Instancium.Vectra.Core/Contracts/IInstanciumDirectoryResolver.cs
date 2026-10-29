namespace Instancium.Vectra.Core.Contracts;

/// <summary>
/// Resolves where the on-disk Vectra index lives for a given content root and
/// provisions the directory structure expected by <see cref="IIndexWriter"/>.
/// </summary>
public interface IInstanciumDirectoryResolver
{
    /// <summary>
    /// Resolves the absolute index directory.
    /// Default convention: <c>{rootPath}/.instancium/vectra/index</c>.
    /// When <paramref name="explicitIndexDir"/> is provided, the index is rooted there instead:
    /// <c>{explicitIndexDir}/vectra/index</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootPath"/> is null or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when the convention path is used and <paramref name="rootPath"/> does not exist.
    /// </exception>
    string ResolveIndexDirectory(string rootPath, string? explicitIndexDir = null);

    /// <summary>
    /// Creates the full directory chain for <paramref name="indexDirectory"/>.
    /// When the index lives inside a <c>.instancium</c> folder, also provisions ecosystem
    /// convention files: a <c>.gitignore</c> excluding derived artifacts and a <c>.gitkeep</c>
    /// marker keeping the product folder committable. Existing files are never overwritten.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="indexDirectory"/> is null or whitespace.</exception>
    void EnsureDirectoryStructure(string indexDirectory);
}
