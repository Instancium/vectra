#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Atomic write request for the on-disk index: the new manifest plus the chunk records
/// written in this transaction. File-level state was removed together with
/// file_state.json (TZ §7, P3); aggregate counters live in the manifest.
/// </summary>
public sealed record IndexWriteRequest
{
    public IndexWriteRequest(DiskIndexManifest manifest, IReadOnlyList<ChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfNotEqual(manifest.Version, DiskIndexManifest.CurrentVersion);

        var chunkIds = new HashSet<long>();
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
        }

        ArgumentOutOfRangeException.ThrowIfNotEqual(manifest.ChunkCount, chunks.Count);
        Manifest = manifest;
        Chunks = chunks.ToArray();
    }

    public DiskIndexManifest Manifest { get; }
    public IReadOnlyList<ChunkRecord> Chunks { get; }
}

