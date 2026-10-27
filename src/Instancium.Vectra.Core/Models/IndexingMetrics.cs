#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record IndexingMetrics
{
    public int TotalPages { get; set; }

    public int TotalChunks { get; set; }

    public int EmbeddingsGenerated { get; set; }

    public TimeSpan ExtractionDuration { get; set; }

    public TimeSpan ChunkingDuration { get; set; }

    public TimeSpan EmbeddingDuration { get; set; }

    public TimeSpan TotalDuration { get; set; }

    public double PagesPerSecond { get; set; }

    public double ChunksPerSecond { get; set; }

    public long PeakWorkingSetMb { get; set; }
}
