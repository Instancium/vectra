#nullable enable
namespace Instancium.Vectra.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Immutable, point-in-time snapshot over an <see cref="InMemoryVectorStore"/> COW array.
///
/// Memory ownership:
///   - The snapshot WRAPS the store array without copying. This is safe because
///     copy-on-write guarantees the array is never mutated in place; the span therefore
///     stays valid for the snapshot's lifetime even though the store does not transfer
///     ownership.
///
/// Normalization contract (TZ §4.2):
///   - The snapshot does NOT normalize vectors. For Cosine collections, normalization is
///     the responsibility of the write path (VectraCollection.AddAsync, introduced in P5)
///     and of the query path (interim engine normalizes the query). This keeps the
///     snapshot materialization O(1) and prepares for segmentized storage (P7).
///
/// Dispose is intentionally a no-op: instances are cached and shared by the owning store,
/// so consumers must never dispose them; memory is either store-owned (immutable) or a
/// GC-managed copy released with the snapshot itself.
/// </summary>
public sealed class InMemoryVectraSnapshot : IVectraSnapshot
{
    private readonly float[] _vectors;
    private readonly long[] _vectorIds;

    public InMemoryVectraSnapshot(
        float[] vectors,
        long[] vectorIds,
        int dimension,
        SimilarityMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(vectorIds);
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);

        if (vectorIds.Length != 0 && vectors.Length != vectorIds.Length * dimension)
        {
            throw new ArgumentException(
                $"Vector array length {vectors.Length} does not match vectorIds.Length * dimension ({vectorIds.Length} * {dimension}).",
                nameof(vectors));
        }

        _vectors = vectors;
        _vectorIds = vectorIds;
        Dimension = dimension;
        Metric = metric;
        VectorCount = vectorIds.Length;
    }

    public long VectorCount { get; }
    public int Dimension { get; }
    public SimilarityMetric Metric { get; }

    public ReadOnlySpan<float> GetVector(long slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, VectorCount);
        int offset = checked((int)(slot * Dimension));
        return _vectors.AsSpan(offset, Dimension);
    }

    public long GetVectorId(long slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, VectorCount);
        return _vectorIds[slot];
    }

    public void Dispose()
    {
        // No-op by design: see type remarks. The owner (store) retires cached snapshots via GC.
    }
}