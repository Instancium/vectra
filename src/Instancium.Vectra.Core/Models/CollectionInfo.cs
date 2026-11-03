#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Read-only snapshot of a collection's persisted identity and size.
/// </summary>
public sealed record CollectionInfo
{
    public CollectionInfo(
        string name,
        int dimension,
        SimilarityMetric metric,
        string modelId,
        string? modelFingerprint,
        long vectorCount,
        int formatVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegative(vectorCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(formatVersion, 1);

        Name = name;
        Dimension = dimension;
        Metric = metric;
        ModelId = modelId;
        ModelFingerprint = modelFingerprint;
        VectorCount = vectorCount;
        FormatVersion = formatVersion;
    }

    public string Name { get; }

    public int Dimension { get; }

    public SimilarityMetric Metric { get; }

    public string ModelId { get; }

    public string? ModelFingerprint { get; }

    public long VectorCount { get; }

    public int FormatVersion { get; }
}
