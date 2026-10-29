#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Complete, validated input for one atomic on-disk index write (TZ-2 stage 1).
/// The writer sorts chunks and files by id before serializing, so the output is deterministic
/// regardless of input order.
/// </summary>
public sealed record IndexWriteRequest
{
    public IndexWriteRequest(
        DiskIndexManifest manifest,
        IReadOnlyList<FileState> files,
        IReadOnlyList<ChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfNotEqual(manifest.Version, DiskIndexManifest.CurrentVersion);

        var fileIds = new HashSet<long>();
        foreach (FileState file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (!fileIds.Add(file.FileId))
            {
                throw new ArgumentException($"Duplicate file id {file.FileId}.", nameof(files));
            }
        }

        var chunkIds = new HashSet<long>();
        var chunksByFile = new Dictionary<long, HashSet<long>>();
        foreach (ChunkRecord chunk in chunks)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            if (!chunkIds.Add(chunk.ChunkId))
            {
                throw new ArgumentException($"Duplicate chunk id {chunk.ChunkId}.", nameof(chunks));
            }
            if (chunk.Vector.Length != manifest.Dimension)
            {
                throw new ArgumentException(
                    $"Chunk {chunk.ChunkId} has dimension {chunk.Vector.Length} but the manifest declares {manifest.Dimension}.",
                    nameof(chunks));
            }
            if (!fileIds.Contains(chunk.FileId))
            {
                throw new ArgumentException(
                    $"Chunk {chunk.ChunkId} references unknown file id {chunk.FileId}.",
                    nameof(chunks));
            }
            if (!chunksByFile.TryGetValue(chunk.FileId, out HashSet<long>? bucket))
            {
                bucket = new HashSet<long>();
                chunksByFile[chunk.FileId] = bucket;
            }
            bucket.Add(chunk.ChunkId);
        }

        foreach (FileState file in files)
        {
            HashSet<long> declared = file.ChunkIds.ToHashSet();
            HashSet<long> actual = chunksByFile.GetValueOrDefault(file.FileId) ?? new HashSet<long>();
            if (!declared.SetEquals(actual))
            {
                throw new ArgumentException(
                    $"File {file.FileId} declares chunk ids [{string.Join(", ", file.ChunkIds)}] " +
                    "but the chunk records reference a different set.",
                    nameof(files));
            }
        }

        ArgumentOutOfRangeException.ThrowIfNotEqual(manifest.FileCount, files.Count);
        ArgumentOutOfRangeException.ThrowIfNotEqual(manifest.ChunkCount, chunks.Count);

        Manifest = manifest;
        Files = files.ToArray();
        Chunks = chunks.ToArray();
    }

    public DiskIndexManifest Manifest { get; }
    public IReadOnlyList<FileState> Files { get; }
    public IReadOnlyList<ChunkRecord> Chunks { get; }
}
