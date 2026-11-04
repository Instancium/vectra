#nullable enable
namespace Instancium.Vectra.Core.Models;

using Instancium.Vectra.Core.Contracts;
using System;

/// <summary>
/// Global configuration for the vector engine.
/// Eliminates dependency on external configuration files and IConfiguration,
/// allowing zero-configuration initialization with sensible defaults.
/// </summary>
public sealed class VectraOptions
{
    public string BaseDirectory { get; set; } = ".instancium/vectra";
    public SimilarityMetric DefaultMetric { get; set; } = SimilarityMetric.Cosine;
    public int CompactionSegmentThreshold { get; set; } = 1_000_000;
    public bool ValidateChecksumsOnOpen { get; set; } = true;
    public KernelProvider Kernel { get; set; } = KernelProvider.Managed;

    /// <summary>
    /// Architectural Seam (TZ §3.2, §7 Phase P8): pluggable factory for external or native similarity kernels.
    /// 
    /// WHY: Decouples the core managed engine from platform-specific C/C++ compilation toolchains and native binaries.
    /// Enterprise acceleration packages (e.g. 'Instancium.Vectra.Native' providing AVX-512, OpenMP, or GPU kernels)
    /// inject their scoring kernels via this delegate without requiring modifications to the engine's public API.
    /// When <see cref="Kernel"/> is set to <see cref="KernelProvider.Native"/>, this resolver must not be null.
    /// </summary>
    public Func<SimilarityMetric, ISimilarityKernel>? CustomKernelResolver { get; set; }
}
