#nullable enable
namespace Instancium.Vectra.Search.Kernels;

using System;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Factory and resolution seam for metric-specific similarity kernels.
/// Supports default managed SIMD kernels, external pluggable kernels, and native acceleration (TZ §3.2, P8).
/// </summary>
public static class SimilarityKernelFactory
{
    /// <summary>
    /// Resolves a similarity kernel based on the requested metric and provider configuration.
    /// </summary>
    /// <param name="metric">Vector similarity metric.</param>
    /// <param name="provider">Target execution provider (Managed or Native).</param>
    /// <param name="customResolver">Optional external resolver delegate injected via VectraOptions.</param>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when <paramref name="provider"/> is <see cref="KernelProvider.Native"/> but <paramref name="customResolver"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="customResolver"/> produces a null kernel or a kernel with mismatched metric.
    /// </exception>
    public static ISimilarityKernel Create(
        SimilarityMetric metric,
        KernelProvider provider = KernelProvider.Managed,
        Func<SimilarityMetric, ISimilarityKernel>? customResolver = null)
    {
        if (provider == KernelProvider.Native)
        {
            if (customResolver is null)
            {
                throw new PlatformNotSupportedException(
                    "Native acceleration is configured (KernelProvider.Native), but no native kernel resolver is registered. " +
                    "To enable native acceleration, reference the 'Instancium.Vectra.Native' package and assign VectraOptions.CustomKernelResolver.");
            }

            ISimilarityKernel nativeKernel = customResolver(metric)
                ?? throw new InvalidOperationException($"Custom kernel resolver returned null for metric '{metric}'.");

            if (nativeKernel.Metric != metric)
            {
                throw new InvalidOperationException(
                    $"Resolved native kernel metric '{nativeKernel.Metric}' does not match requested metric '{metric}'.");
            }

            return nativeKernel;
        }

        if (customResolver is not null)
        {
            return customResolver(metric);
        }

        return CreateManaged(metric);
    }

    /// <summary>
    /// Instantiates the default managed SIMD kernel for the given metric.
    /// </summary>
    public static ISimilarityKernel CreateManaged(SimilarityMetric metric) => metric switch
    {
        SimilarityMetric.Cosine => new CosineKernel(),
        SimilarityMetric.DotProduct => new DotProductKernel(),
        SimilarityMetric.Euclidean => new EuclideanKernel(),
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported similarity metric.")
    };
}
