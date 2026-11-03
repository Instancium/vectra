namespace Instancium.Vectra.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Immutable, point-in-time snapshot of <see cref="InMemoryVectorStore"/>.
///
/// Defect #4 structural fix: the snapshot captures the store's state at creation time
/// and is never mutated. Concurrent searches operate on this snapshot without locks.
///
/// For Cosine collections, all vectors are L2-normalized at snapshot creation time.
/// This allows <see cref="Search.Kernels.CosineKernel"/> to degenerate into a DotProduct
/// computation, improving search performance.
///
/// TODO [P5]: Move Cosine normalization from snapshot creation to VectraCollection.AddAsync.
/// Current approach requires full snapshot rebuild on every update, which is acceptable for
/// P4 (brute-force in-memory) but suboptimal for segmentized storage (P7).
/// </summary>
public sealed class InMemoryVectraSnapshot : IVectraSnapshot
{
    private readonly float[] _vectors;
    private readonly long[] _vectorIds;
    private bool _disposed;

    public long VectorCount { get; }
    public int Dimension { get; }
    public SimilarityMetric Metric { get; }

    public InMemoryVectraSnapshot(
        float[] vectors,
        long[] vectorIds,
        int dimension,
        SimilarityMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(vectorIds);

        if (vectors.Length != vectorIds.Length * dimension)
        {
            throw new ArgumentException(
                $"Vector array length {vectors.Length} does not match vectorIds.Length * dimension ({vectorIds.Length} * {dimension}).",
                nameof(vectors));
        }

        // TODO [P5]: Normalize vectors in-place for Cosine metric at snapshot creation.
        // This is a temporary solution; normalization should move to AddAsync in P5.
        if (metric == SimilarityMetric.Cosine)
        {
            NormalizeVectorsInPlace(vectors, dimension);
        }

        _vectors = vectors;
        _vectorIds = vectorIds;
        Dimension = dimension;
        Metric = metric;
        VectorCount = vectorIds.Length;
    }

    public ReadOnlySpan<float> GetVector(long slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (slot < 0 || slot >= VectorCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"Slot must be in range [0, {VectorCount}).");
        }

        int offset = checked((int)(slot * Dimension));
        return _vectors.AsSpan(offset, Dimension);
    }

    public long GetVectorId(long slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (slot < 0 || slot >= VectorCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"Slot must be in range [0, {VectorCount}).");
        }

        return _vectorIds[slot];
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static void NormalizeVectorsInPlace(float[] vectors, int dimension)
    {
        for (int i = 0; i < vectors.Length; i += dimension)
        {
            double sumOfSquares = 0.0;
            for (int d = 0; d < dimension; d++)
            {
                double val = vectors[i + d];
                sumOfSquares += val * val;
            }

            float norm = (float)Math.Sqrt(sumOfSquares);
            if (norm < 1e-12f)
            {
                continue; // Skip zero vectors
            }

            for (int d = 0; d < dimension; d++)
            {
                vectors[i + d] /= norm;
            }
        }
    }
}
