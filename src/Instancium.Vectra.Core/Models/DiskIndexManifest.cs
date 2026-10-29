#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// On-disk index manifest (<c>manifest.json</c>, TZ-2 sections 3.2 and 4.4).
/// Describes the format version, embedding model and aggregate statistics.
/// Counts must exactly match the collections passed to <see cref="IndexWriteRequest"/>.
/// </summary>
public sealed record DiskIndexManifest
{
    /// <summary>The only index format version currently supported by writers and readers.</summary>
    public const int CurrentVersion = 1;

    public DiskIndexManifest(
        int version,
        int dimension,
        string modelId,
        IReadOnlyList<string> formats,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        int fileCount,
        int chunkCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(formats);
        foreach (string format in formats)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(format);
        }
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCount);

        Version = version;
        Dimension = dimension;
        ModelId = modelId;
        Formats = formats.ToArray();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        FileCount = fileCount;
        ChunkCount = chunkCount;
    }

    public int Version { get; }
    public int Dimension { get; }
    public string ModelId { get; }
    public IReadOnlyList<string> Formats { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public int FileCount { get; }
    public int ChunkCount { get; }
}
