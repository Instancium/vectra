#nullable enable
namespace Instancium.Vectra.Core.Models;

using Instancium.Vectra.Core.Contracts;

/// <summary>
/// Algorithmic parameters of a single search call.
/// <see cref="Overfetch"/> is a lower bound request: the engine always uses
/// max(Overfetch, TopK, strategy default), guaranteeing candidate completeness.
/// </summary>
public sealed record SearchOptions
{
    public SearchOptions(int topK = 10, int? overfetch = null, IReranker? reranker = null, bool includeDiagnostics = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        TopK = topK;
        Overfetch = overfetch;
        Reranker = reranker;
        IncludeDiagnostics = includeDiagnostics;
    }

    public int TopK { get; }

    public int? Overfetch { get; }

    public IReranker? Reranker { get; }

    public bool IncludeDiagnostics { get; }
}
