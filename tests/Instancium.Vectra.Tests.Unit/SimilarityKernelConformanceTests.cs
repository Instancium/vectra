namespace Instancium.Vectra.Tests.Unit.Similarity;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search.Kernels;
using Xunit;

/// <summary>
/// Conformance tests: every production <see cref="Core.Contracts.ISimilarityKernel"/> must match
/// <see cref="ReferenceSimilarityKernel"/> within relative error ≤ 1e-5 on a fixed dataset.
///
/// Dataset includes edge cases: zero vectors, near-orthogonal vectors, identical vectors,
/// high-dimensional vectors (1024-d), and vectors with extreme values.
/// </summary>
[Trait("Category", "Similarity")]
public sealed class SimilarityKernelConformanceTests
{
    private const double RelativeErrorThreshold = 1e-5;

    public static IEnumerable<object[]> ConformanceTestData()
    {
        // Edge cases
        yield return new object[] { new float[] { 0f, 0f, 0f }, new float[] { 0f, 0f, 0f } };
        yield return new object[] { new float[] { 1f, 0f, 0f }, new float[] { 0f, 1f, 0f } };
        yield return new object[] { new float[] { 1f, 2f, 3f }, new float[] { 1f, 2f, 3f } };
        yield return new object[] { new float[] { -1f, -2f, -3f }, new float[] { 1f, 2f, 3f } };

        // Random high-dimensional vectors
        var random = new Random(42);
        for (int i = 0; i < 10; i++)
        {
            yield return new object[] { RandomVector(1024, random), RandomVector(1024, random) };
        }
    }

    [Theory]
    [MemberData(nameof(ConformanceTestData))]
    public void DotProductKernel_ShouldMatchReference(float[] a, float[] b)
    {
        var reference = new ReferenceSimilarityKernel(SimilarityMetric.DotProduct);
        var production = new DotProductKernel();

        float expected = reference.Score(a, b);
        float actual = production.Score(a, b);

        AssertConformance(expected, actual);
    }

    [Theory]
    [MemberData(nameof(ConformanceTestData))]
    public void CosineKernel_ShouldMatchReference(float[] a, float[] b)
    {
        // Normalize inputs (CosineKernel expects pre-normalized vectors)
        float[] normalizedA = Normalize(a);
        float[] normalizedB = Normalize(b);

        var reference = new ReferenceSimilarityKernel(SimilarityMetric.Cosine);
        var production = new CosineKernel();

        float expected = reference.Score(normalizedA, normalizedB);
        float actual = production.Score(normalizedA, normalizedB);

        AssertConformance(expected, actual);
    }

    [Theory]
    [MemberData(nameof(ConformanceTestData))]
    public void EuclideanKernel_ShouldMatchReference(float[] a, float[] b)
    {
        var reference = new ReferenceSimilarityKernel(SimilarityMetric.Euclidean);
        var production = new EuclideanKernel();

        float expected = reference.Score(a, b);
        float actual = production.Score(a, b);

        AssertConformance(expected, actual);
    }

    private static void AssertConformance(float expected, float actual)
    {
        if (Math.Abs(expected) < 1e-12f)
        {
            // Near-zero: use absolute error
            Assert.True(
                Math.Abs(actual - expected) < 1e-6f,
                $"Absolute error {Math.Abs(actual - expected)} exceeds threshold 1e-6 (expected={expected}, actual={actual}).");
        }
        else
        {
            // Non-zero: use relative error
            double relativeError = Math.Abs((actual - expected) / expected);
            Assert.True(
                relativeError <= RelativeErrorThreshold,
                $"Relative error {relativeError} exceeds threshold {RelativeErrorThreshold} (expected={expected}, actual={actual}).");
        }
    }

    private static float[] RandomVector(int dimension, Random random)
    {
        float[] vector = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2.0 - 1.0);
        }

        return vector;
    }

    private static float[] Normalize(float[] vector)
    {
        double sumOfSquares = 0.0;
        foreach (float value in vector)
        {
            sumOfSquares += (double)value * value;
        }

        float norm = (float)Math.Sqrt(sumOfSquares);
        if (norm < 1e-12f)
        {
            return vector;
        }

        return vector.Select(value => value / norm).ToArray();
    }
}
