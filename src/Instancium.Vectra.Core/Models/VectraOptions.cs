#nullable enable
namespace Instancium.Vectra.Core.Models;

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
}
