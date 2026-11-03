namespace Instancium.Vectra.Core.Contracts;

/// <summary>
/// Extension seam for text → vector embedding. Lives at the engine boundary and is
/// consumed by <c>VectraTextExtensions</c> (TZ §5.2); the engine core never depends on
/// any concrete provider.
///
/// Contract:
///   - <see cref="ModelId"/> and <see cref="Dimension"/> are stable for the provider's
///     lifetime; the engine uses them to detect mismatch against
///     <see cref="CollectionOptions"/> at <c>Open</c> time.
///   - The returned vector MUST have length equal to <see cref="Dimension"/>.
///   - Providers are expected to be thread-safe; the engine may call
///     <see cref="EmbedAsync"/> concurrently.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Stable identifier of the embedding model (e.g. "qwen3-embedding-0.6b").</summary>
    string ModelId { get; }

    /// <summary>Dimensionality of every vector this provider emits.</summary>
    int Dimension { get; }

    /// <summary>Embeds <paramref name="text"/> into a vector of length <see cref="Dimension"/>.</summary>
    ValueTask<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
