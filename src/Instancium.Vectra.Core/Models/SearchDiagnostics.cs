namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Optional telemetry of a search call: candidate scan volume, recall and rerank timings,
/// and the similarity kernel actually used. Populated only when
/// <see cref="SearchOptions.IncludeDiagnostics"/> is true.
/// </summary>
public sealed record SearchDiagnostics
{
    public SearchDiagnostics(int scannedVectors, double recallTimeMs, double rerankTimeMs, string? kernelUsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scannedVectors);
        ArgumentOutOfRangeException.ThrowIfNegative(recallTimeMs);
        ArgumentOutOfRangeException.ThrowIfNegative(rerankTimeMs);

        ScannedVectors = scannedVectors;
        RecallTimeMs = recallTimeMs;
        RerankTimeMs = rerankTimeMs;
        KernelUsed = kernelUsed;
    }

    public int ScannedVectors { get; }

    public double RecallTimeMs { get; }

    public double RerankTimeMs { get; }

    public string? KernelUsed { get; }
}
