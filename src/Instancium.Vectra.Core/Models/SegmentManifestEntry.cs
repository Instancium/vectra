#nullable enable
namespace Instancium.Vectra.Core.Models;

using System;

/// <summary>
/// Cryptographic integrity descriptor and sizing metadata for an immutable on-disk storage segment.
/// Captures SHA-256 digests for vectors, mapping, and payload artifacts to guarantee fault detection
/// on corrupted storage media before queries consume torn or invalid floating-point buffers.
/// </summary>
public sealed record SegmentManifestEntry
{
    public SegmentManifestEntry(
        long segmentId,
        int vectorCount,
        string vectorsSha256,
        string mapSha256,
        string payloadSha256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);
        ArgumentOutOfRangeException.ThrowIfNegative(vectorCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(vectorsSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadSha256);

        SegmentId = segmentId;
        VectorCount = vectorCount;
        VectorsSha256 = vectorsSha256;
        MapSha256 = mapSha256;
        PayloadSha256 = payloadSha256;
    }

    public long SegmentId { get; }
    public int VectorCount { get; }
    public string VectorsSha256 { get; }
    public string MapSha256 { get; }
    public string PayloadSha256 { get; }
}
