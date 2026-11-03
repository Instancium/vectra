namespace Instancium.Vectra.Search.Kernels;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Cosine similarity kernel.
///
/// IMPORTANT: This implementation assumes that input vectors are already L2-normalized.
/// For Cosine collections, <see cref="Storage.InMemoryVectraSnapshot"/> normalizes all vectors
/// at snapshot creation time, reducing Cosine to DotProduct mathematically.
///
/// TODO [P5]: Move normalization from snapshot creation to VectraCollection.AddAsync.
/// This will allow updates without full snapshot rebuild and reduce memory overhead.
/// </summary>
public sealed class CosineKernel : ISimilarityKernel
{
    private readonly DotProductKernel _dotProductKernel = new();

    public SimilarityMetric Metric => SimilarityMetric.Cosine;

    public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        // Vectors are pre-normalized by InMemoryVectraSnapshot, so Cosine == DotProduct.
        return _dotProductKernel.Score(query, candidate);
    }
}
