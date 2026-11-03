namespace Instancium.Vectra.Search.Kernels;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Factory for metric-specific similarity kernels.
/// </summary>
public static class SimilarityKernelFactory
{
    public static ISimilarityKernel Create(SimilarityMetric metric) => metric switch
    {
        SimilarityMetric.Cosine => new CosineKernel(),
        SimilarityMetric.DotProduct => new DotProductKernel(),
        SimilarityMetric.Euclidean => new EuclideanKernel(),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported metric.")
    };
}
