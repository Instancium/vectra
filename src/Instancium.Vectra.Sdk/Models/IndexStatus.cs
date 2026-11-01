namespace Instancium.Vectra.Sdk.Models;

/// <summary>
/// Represents current health and metadata status of a profile index.
/// </summary>
public sealed record IndexStatus
{
    public IndexStatus(
        string profileId,
        bool exists,
        string indexDirectory,
        int fileCount,
        int chunkCount,
        int dimension,
        string? modelId,
        IReadOnlyList<string> formats,
        DateTimeOffset? createdAt,
        DateTimeOffset? updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

        ProfileId = profileId;
        Exists = exists;
        IndexDirectory = indexDirectory;
        FileCount = fileCount;
        ChunkCount = chunkCount;
        Dimension = dimension;
        ModelId = modelId;
        Formats = formats ?? Array.Empty<string>();
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public string ProfileId { get; }
    public bool Exists { get; }
    public string IndexDirectory { get; }
    public int FileCount { get; }
    public int ChunkCount { get; }
    public int Dimension { get; }
    public string? ModelId { get; }
    public IReadOnlyList<string> Formats { get; }
    public DateTimeOffset? CreatedAt { get; }
    public DateTimeOffset? UpdatedAt { get; }

    public static IndexStatus NotCreated(string profileId, string indexDirectory) =>
        new(
            profileId: profileId,
            exists: false,
            indexDirectory: indexDirectory,
            fileCount: 0,
            chunkCount: 0,
            dimension: 0,
            modelId: null,
            formats: Array.Empty<string>(),
            createdAt: null,
            updatedAt: null);
}
