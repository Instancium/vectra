#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Atomic write request for the on-disk index: the new manifest plus the chunk records
/// appended in this transaction. File-level state was removed together with
/// file_state.json (TZ §7, P3); aggregate counters live in the manifest.
/// </summary>
public sealed record IndexWriteRequest
{
    public DiskIndexManifest Manifest { get; init; }
    public IReadOnlyList<ChunkRecord> Chunks { get; init; }

    public IndexWriteRequest(DiskIndexManifest manifest, IReadOnlyList<ChunkRecord> chunks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(chunks);

        Manifest = manifest;
        Chunks = chunks;
    }
}