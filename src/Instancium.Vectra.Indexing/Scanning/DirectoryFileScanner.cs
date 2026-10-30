namespace Instancium.Vectra.Indexing.Scanning;

using System.Security.Cryptography;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class DirectoryFileScanner : IFileScanner
{
    private const int FileStreamBufferSize = 81_920;

    private readonly ILogger<DirectoryFileScanner> _logger;

    public DirectoryFileScanner(ILogger<DirectoryFileScanner>? logger = null)
    {
        _logger = logger ?? NullLogger<DirectoryFileScanner>.Instance;
    }

    public async Task<IReadOnlyList<ScannedFile>> ScanAsync(
        string rootPath,
        IReadOnlyList<string> supportedExtensions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(supportedExtensions);

        HashSet<string> extensions = NormalizeExtensions(supportedExtensions);
        if (extensions.Count == 0)
        {
            return Array.Empty<ScannedFile>();
        }

        string fullRoot = Path.GetFullPath(rootPath);

        if (File.Exists(fullRoot))
        {
            return await ScanSingleFileAsync(fullRoot, extensions, cancellationToken).ConfigureAwait(false);
        }

        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Content root '{fullRoot}' does not exist.");
        }

        var discovered = new List<(string FullPath, string RelativePath)>();
        CollectFiles(fullRoot, fullRoot, extensions, discovered, cancellationToken);

        var scanned = new List<ScannedFile>(discovered.Count);
        foreach ((string fullPath, string relativePath) in discovered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned.Add(await HashFileAsync(fullPath, relativePath, cancellationToken).ConfigureAwait(false));
        }

        scanned.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return scanned;
    }

    private static async Task<IReadOnlyList<ScannedFile>> ScanSingleFileAsync(
        string fullPath,
        HashSet<string> extensions,
        CancellationToken cancellationToken)
    {
        string? extension = TryNormalizeExtension(Path.GetExtension(fullPath));
        if (extension is null || !extensions.Contains(extension))
        {
            return Array.Empty<ScannedFile>();
        }

        ScannedFile file = await HashFileAsync(fullPath, Path.GetFileName(fullPath), cancellationToken)
            .ConfigureAwait(false);
        return new[] { file };
    }

    private void CollectFiles(
        string directory,
        string root,
        HashSet<string> extensions,
        List<(string FullPath, string RelativePath)> collector,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IEnumerable<string> files;
        IEnumerable<string> subDirectories;
        try
        {
            files = Directory.EnumerateFiles(directory);
            subDirectories = Directory.EnumerateDirectories(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(
                "Skipping inaccessible directory '{Directory}': {Error}",
                directory,
                ex.Message);
            return;
        }

        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.'))
            {
                // Hidden files are treated as service data and are never indexed.
                continue;
            }

            string? extension = TryNormalizeExtension(Path.GetExtension(file));
            if (extension is null || !extensions.Contains(extension))
            {
                continue;
            }

            string relativePath = NormalizeRelativePath(Path.GetRelativePath(root, file));
            collector.Add((file, relativePath));
        }

        foreach (string subDirectory in subDirectories)
        {
            string name = Path.GetFileName(subDirectory);
            if (name.StartsWith('.'))
            {
                // Hidden directories (.instancium included) hold derived or service data
                // and are never scanned.
                continue;
            }

            CollectFiles(subDirectory, root, extensions, collector, cancellationToken);
        }
    }

    private static HashSet<string> NormalizeExtensions(IReadOnlyList<string> supportedExtensions)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (string extension in supportedExtensions)
        {
            string? value = TryNormalizeExtension(extension);
            if (value is not null)
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    private static string? TryNormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        try
        {
            return ContentExtractorRegistry.NormalizeExtension(extension);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string relativePath) =>
        relativePath.Replace('\\', '/');

    private static async ValueTask<ScannedFile> HashFileAsync(
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileStreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        // Streamed hashing (risk R-3): the file is never fully loaded into memory.
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return new ScannedFile(fullPath, relativePath, Convert.ToHexStringLower(hash), stream.Length);
    }
}
