namespace Instancium.Vectra.Sdk.Models;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Encapsulates the execution result of an indexing operation for a given profile.
/// </summary>
public sealed record IndexResult
{
    public IndexResult(
        string profileId,
        string folderPath,
        string indexDirectory,
        int filesIndexed,
        int chunksAdded,
        int chunksDeactivated,
        bool hasChanges,
        TimeSpan duration,
        DiskIndexManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        ProfileId = profileId;
        FolderPath = folderPath;
        IndexDirectory = indexDirectory;
        FilesIndexed = filesIndexed;
        ChunksAdded = chunksAdded;
        ChunksDeactivated = chunksDeactivated;
        HasChanges = hasChanges;
        Duration = duration;
        Manifest = manifest;
    }

    public string ProfileId { get; }
    public string FolderPath { get; }
    public string IndexDirectory { get; }
    public int FilesIndexed { get; }
    public int ChunksAdded { get; }
    public int ChunksDeactivated { get; }
    public bool HasChanges { get; }
    public TimeSpan Duration { get; }
    public DiskIndexManifest Manifest { get; }
}
