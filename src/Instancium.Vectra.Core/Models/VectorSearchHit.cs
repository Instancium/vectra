#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A single search result.
/// <see cref="VectorScore"/> is the raw kernel score and never changes after recall.
/// <see cref="FinalScore"/> equals <see cref="VectorScore"/> when no reranker ran,
/// or the reranker's score otherwise. The vector itself is intentionally NOT part of the hit:
/// payloads are served lazily via the collection facade.
/// </summary>
public sealed record VectorSearchHit
{
    public VectorSearchHit(long vectorId, float vectorScore, float finalScore, string? payload, SearchDiagnostics? diagnostics = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vectorId, 1L);

        VectorId = vectorId;
        VectorScore = vectorScore;
        FinalScore = finalScore;
        Payload = payload;
        Diagnostics = diagnostics;
    }

    public long VectorId { get; }

    public float VectorScore { get; }

    public float FinalScore { get; }

    public string? Payload { get; }

    public SearchDiagnostics? Diagnostics { get; }
}
