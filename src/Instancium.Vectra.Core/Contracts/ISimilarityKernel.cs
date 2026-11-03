namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Pure scoring function for a fixed similarity metric.
/// Implementations are stateless and thread-safe; the engine may invoke them concurrently
/// from multiple search threads.
///
/// Contract:
///   - The kernel never allocates per-call.
///   - The returned score is the raw <c>VectorScore</c> that flows unchanged into
///     <see cref="VectorSearchHit.VectorScore"/> unless a reranker overrides
///     <see cref="VectorSearchHit.FinalScore"/> downstream.
///   - Every implementation must pass <c>SimilarityKernelConformanceTests</c> against
///     <c>ReferenceSimilarityKernel</c> with relative error ≤ 1e-5.
/// </summary>
public interface ISimilarityKernel
{
    /// <summary>Metric this kernel computes. Fixed per instance.</summary>
    SimilarityMetric Metric { get; }

    /// <summary>
    /// Computes the similarity score between <paramref name="query"/> and <paramref name="candidate"/>.
    /// Both spans must have identical length equal to the collection's <see cref="CollectionOptions.Dimension"/>.
    /// </summary>
    float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate);
}
