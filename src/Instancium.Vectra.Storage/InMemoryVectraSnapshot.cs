#nullable enable
namespace Instancium.Vectra.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Immutable, point-in-time snapshot over an <see cref="InMemoryVectorStore"/> COW array.
///
/// Memory ownership:
///   - DotProduct / Euclidean: the snapshot WRAPS the store array without copying. This is
///     safe because copy-on-write guarantees the array is never mutated in place; the span
///     therefore stays valid for the snapshot's lifetime even though the store does not
///     transfer ownership.
///   - Cosine: the snapshot materializes ONE L2-normalized copy at construction time so that
///     <c>CosineKernel</c> degenerates into a dot product on the hot path.
///     TODO [P5]: move Cosine normalization from snapshot creation to VectraCollection.AddAsync;
///     current approach rebuilds the normalized copy on every store mutation, which is fine for
///     in-memory brute force but suboptimal for segmentized storage (P7).
///
/// Dispose is intentionally a no-op: instances are cached and shared by the owning store, so
/// consumers must never dispose them; memory is either store-owned (immutable) or a GC-managed
/// copy released with the snapshot itself.
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

        if (metric == SimilarityMetric.Cosine && dimension > 0 && vectorIds.Length > 0)
        {
            float[] normalized = (float[])vectors.Clone();
            NormalizeInPlace(normalized, dimension);
            _vectors = normalized;
        }
        else
        {
            _vectors = vectors;
        }

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

    private static void NormalizeInPlace(float[] vectors, int dimension)
    {
        for (int i = 0; i < vectors.Length; i += dimension)
        {
            double sumOfSquares = 0.0;
            for (int d = 0; d < dimension; d++)
            {
                double value = vectors[i + d];
                sumOfSquares += value * value;
            }

            float norm = (float)Math.Sqrt(sumOfSquares);
            if (norm < 1e-12f)
            {
                continue; // Zero vector: leave as-is; cosine score degrades to 0 by contract.
            }

            for (int d = 0; d < dimension; d++)
            {
                vectors[i + d] /= norm;
            }
        }
    }
}