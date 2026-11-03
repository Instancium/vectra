using Xunit;

namespace Instancium.Vectra.Tests.Unit.Similarity;

/// <summary>
/// Conformance tests for the similarity kernel.
/// Validates that cosine, dot-product, and euclidean similarity implementations
/// produce numerically correct results against reference vectors.
///
/// TODO(P1): populate with the following test groups once ISimilarityKernel is introduced:
///   - CosineIdentityAndOrthogonality
///   - DotProductScaling
///   - EuclideanDistanceTriangleInequality
///   - NumericalStabilityForNearZeroVectors
///   - SpanAndScalarPathEquivalence
/// </summary>
[Trait("Category", "Similarity")]
public class SimilarityKernelConformanceTests
{
    [Fact(Skip = "Placeholder — kernel not yet introduced (P1).")]
    public void Placeholder_KernelNotYetIntroduced()
    {
        // Intentionally empty. Ensures the test class is discoverable
        // and the Category=Similarity filter works before real tests land.
    }
}