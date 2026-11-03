namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Similarity metric of a collection. Fixed at collection creation and immutable afterwards.
/// </summary>
public enum SimilarityMetric
{
    Cosine = 0,
    DotProduct = 1,
    Euclidean = 2,
}
