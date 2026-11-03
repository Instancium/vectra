namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Convenience extensions for <see cref="IVectorSearchEngine"/>. Lives alongside the
/// interface so that any implementation automatically gains the overload.
///
/// The extension wraps the primary contract into a <see cref="SearchQuery"/> +
/// <see cref="SearchOptions"/> pair. Intended for interim consumers (tests, P4 code paths);
/// new code should call the primary overload directly.
/// </summary>
public static class VectorSearchEngineExtensions
{
    /// <summary>
    /// Convenience overload: wraps <paramref name="queryVector"/> and <paramref name="topK"/>
    /// into the primary contract types and delegates.
    /// </summary>
    public static Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        this IVectorSearchEngine engine,
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.SearchAsync(
            new SearchQuery(queryVector),
            new SearchOptions(topK: topK),
            cancellationToken);
    }
}