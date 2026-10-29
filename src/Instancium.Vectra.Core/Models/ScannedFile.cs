#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A file discovered by the scanner. RelativePath is the stable identity key
/// used in file.state.json and by the change classifier.
/// </summary>
public sealed record ScannedFile
{
    public ScannedFile(string fullPath, string relativePath, string sha256, long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        FullPath = fullPath;
        RelativePath = relativePath;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
    }

    public string FullPath { get; }

    /// <summary>Relative to the scan root, forward slashes, original casing preserved.</summary>
    public string RelativePath { get; }

    /// <summary>SHA-256 of the file content, lowercase hex.</summary>
    public string Sha256 { get; }

    public long SizeBytes { get; }
}
