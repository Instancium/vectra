namespace Instancium.Vectra.Search.Kernels;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Cosine similarity kernel.
///
/// IMPORTANT: This implementation assumes that input vectors are already L2-normalized.
/// For Cosine collections, normalization is the responsibility of the write path
/// (VectraCollection.AddAsync, introduced in P5) and of the query path (interim engine
/// normalizes the query). The snapshot (Storage layer) no longer normalizes.
/// </summary>
public sealed class CosineKernel : ISimilarityKernel
{
    private readonly DotProductKernel _dotProductKernel = new();

    public SimilarityMetric Metric => SimilarityMetric.Cosine;

    public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        return _dotProductKernel.Score(query, candidate);
    }
}