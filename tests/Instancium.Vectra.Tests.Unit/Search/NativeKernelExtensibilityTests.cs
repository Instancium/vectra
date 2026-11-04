#nullable enable
namespace Instancium.Vectra.Tests.Unit.Search;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Search.Kernels;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Architectural guard tests for Phase P8 (Native Acceleration Seam).
/// 
/// WHY THIS SUITE EXISTS:
/// Guarantees that external, proprietary, or native P/Invoke similarity kernels (e.g. AVX-512 in Instancium.Vectra.Native)
/// can be seamlessly plugged into VectraEngine without modifying core engine binaries.
/// Prevents AI assistants and contributors from violating the Open-Closed Principle or dropping native readiness.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NativeKernelExtensibilityTests : IDisposable
{
    private readonly string _baseDir = Path.Combine(Path.GetTempPath(), "vectra-native-seam-tests", Guid.NewGuid().ToString("N"));

    public NativeKernelExtensibilityTests()
    {
        Directory.CreateDirectory(_baseDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_WhenKernelIsNative_WithoutResolver_MustThrowPlatformNotSupportedException()
    {
        // ARRANGE: Configure Native kernel execution without providing a resolver
        var options = new VectraOptions
        {
            BaseDirectory = _baseDir,
            Kernel = KernelProvider.Native,
            CustomKernelResolver = null
        };

        await using IVectraEngine engine = VectraEngineFactory.Create(options);
        var col = engine.GetOrOpenCollection("native_fail_col", new CollectionOptions(2, SimilarityMetric.DotProduct, "m1"));
        await col.AddAsync(new VectorEntry(1, new float[] { 1f, 0f }, "dummy payload"));

        // ACT & ASSERT: Must fail fast with clear actionable diagnostics pointing to Instancium.Vectra.Native
        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await col.SearchAsync(new SearchQuery(new float[] { 1f, 0f })));

        Assert.Contains("Instancium.Vectra.Native", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_WithCustomNativeResolver_MustExecuteThroughInjectedKernelAndPopulateDiagnostics()
    {
        // ARRANGE: Register an external mock native kernel
        var mockKernel = new MockNativeSimilarityKernel(SimilarityMetric.DotProduct);

        var options = new VectraOptions
        {
            BaseDirectory = _baseDir,
            Kernel = KernelProvider.Native,
            CustomKernelResolver = metric => metric == SimilarityMetric.DotProduct ? mockKernel : SimilarityKernelFactory.CreateManaged(metric)
        };

        await using IVectraEngine engine = VectraEngineFactory.Create(options);
        var col = engine.GetOrOpenCollection("native_ok_col", new CollectionOptions(2, SimilarityMetric.DotProduct, "m1"));
        await col.AddAsync(new VectorEntry(10, new float[] { 1f, 2f }, "Native Payload"));

        // ACT
        var hits = await col.SearchAsync(
            new SearchQuery(new float[] { 1f, 2f }),
            new SearchOptions(topK: 1, includeDiagnostics: true));

        // ASSERT: Query executed successfully and diagnostics confirm the external kernel was used
        var hit = Assert.Single(hits);
        Assert.Equal(10, hit.VectorId);
        Assert.True(mockKernel.InvocationCount > 0, "External native kernel must be invoked on search hot-path.");
        Assert.NotNull(hit.Diagnostics);
        Assert.Equal(nameof(MockNativeSimilarityKernel), hit.Diagnostics.KernelUsed);
    }

    [Fact]
    public void MockNativeSimilarityKernel_MustPassSimilarityKernelConformanceThreshold()
    {
        // Verifies that external kernels satisfy the universal precision invariant (relative error <= 1e-5)
        var reference = new ReferenceSimilarityKernel(SimilarityMetric.DotProduct);
        var mockNative = new MockNativeSimilarityKernel(SimilarityMetric.DotProduct);

        float[] v1 = new float[] { 0.123f, 0.456f, 0.789f };
        float[] v2 = new float[] { 0.987f, 0.654f, 0.321f };

        float expected = reference.Score(v1, v2);
        float actual = mockNative.Score(v1, v2);

        double relativeError = Math.Abs((actual - expected) / expected);
        Assert.True(relativeError <= 1e-5, $"Relative error {relativeError} exceeded tolerance.");
    }

    /// <summary>
    /// Emulates a proprietary P/Invoke SIMD kernel compiled in C++ (Instancium.Vectra.Native).
    /// </summary>
    private sealed class MockNativeSimilarityKernel : ISimilarityKernel
    {
        public SimilarityMetric Metric { get; }
        public int InvocationCount { get; private set; }

        public MockNativeSimilarityKernel(SimilarityMetric metric)
        {
            Metric = metric;
        }

        public float Score(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
        {
            InvocationCount++;
            // Emulates an AVX-512 / FMA native call with double accumulation
            double dot = 0.0;
            for (int i = 0; i < query.Length; i++)
            {
                dot += (double)query[i] * candidate[i];
            }
            return (float)dot;
        }
    }
}
