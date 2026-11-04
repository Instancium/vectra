#nullable enable
namespace Instancium.Vectra.Storage.Segments;

using System;

/// <summary>
/// Uncompressed vector and payload input destined for segment packing.
/// Distinct from mutable runtime entries to enforce immutable invariants during segment compilation.
/// </summary>
public sealed record SegmentEntry
{
    public SegmentEntry(long vectorId, ReadOnlyMemory<float> vector, string? payload = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vectorId, 1L);
        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        VectorId = vectorId;
        Vector = vector;
        Payload = payload;
    }

    public long VectorId { get; }
    public ReadOnlyMemory<float> Vector { get; }
    public string? Payload { get; }
}
