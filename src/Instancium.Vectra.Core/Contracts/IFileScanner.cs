namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Scans a file or a directory tree and produces hash-identified file records.
/// </summary>
public interface IFileScanner
{
    /// <summary>
    /// Scans <paramref name="rootPath"/> (a single file or a directory).
    /// Returns files whose extension is present in <paramref name="supportedExtensions"/>,
    /// ordered by <see cref="ScannedFile.RelativePath"/> (ordinal).
    /// </summary>
    Task<IReadOnlyList<ScannedFile>> ScanAsync(
        string rootPath,
        IReadOnlyList<string> supportedExtensions,
        CancellationToken cancellationToken = default);
}
