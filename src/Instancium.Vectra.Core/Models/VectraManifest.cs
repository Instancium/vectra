#nullable enable
namespace Instancium.Vectra.Core.Models;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Root manifest descriptor of a v2 segmented vector collection (<see cref="CurrentFormatVersion"/> = 2).
/// Replaces the monolithic v1 layout with an append-only segment catalog, enabling incremental persistence
/// and sub-segment checksum verification without rewriting existing vector data.
/// </summary>
public sealed record VectraManifest
{
    public const int CurrentFormatVersion = 2;

    public VectraManifest(
        int formatVersion,
        int dimension,
        SimilarityMetric metric,
        string modelId,
        string? modelFingerprint,
        QuantizationMode quantization,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IReadOnlyList<SegmentManifestEntry> segments,
        long totalVectorCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formatVersion, CurrentFormatVersion);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentOutOfRangeException.ThrowIfNegative(totalVectorCount);

        FormatVersion = formatVersion;
        Dimension = dimension;
        Metric = metric;
        ModelId = modelId;
        ModelFingerprint = modelFingerprint;
        Quantization = quantization;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Segments = segments.ToArray();
        TotalVectorCount = totalVectorCount;
    }

    public int FormatVersion { get; }
    public int Dimension { get; }
    public SimilarityMetric Metric { get; }
    public string ModelId { get; }
    public string? ModelFingerprint { get; }
    public QuantizationMode Quantization { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public IReadOnlyList<SegmentManifestEntry> Segments { get; }
    public long TotalVectorCount { get; }
}
