#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Immutable data properties of a vector collection: dimension, metric, model identity,
/// quantization. These are fixed at creation time and never change for the collection lifetime.
/// Algorithmic properties (brute force / HNSW / IVF) live in search strategies, not here.
/// </summary>
public sealed record CollectionOptions
{
    public CollectionOptions(
        int dimension,
        SimilarityMetric metric,
        string modelId,
        string? modelFingerprint = null,
        QuantizationMode quantization = QuantizationMode.None)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        Dimension = dimension;
        Metric = metric;
        ModelId = modelId;
        ModelFingerprint = modelFingerprint;
        Quantization = quantization;
    }

    public int Dimension { get; }

    public SimilarityMetric Metric { get; }

    public string ModelId { get; }

    public string? ModelFingerprint { get; }

    public QuantizationMode Quantization { get; }
}
