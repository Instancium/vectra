namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Immutable, point-in-time view over a collection's vector data.
///
/// Contract:
///   - <see cref="VectorCount"/> and slot layout never change for the snapshot's lifetime.
///   - <see cref="GetVector"/> returns a span into snapshot-owned memory; the span is valid
///     until <see cref="IDisposable.Dispose"/> is called.
///   - Payload text is intentionally NOT part of the snapshot: consumers hydrate it lazily
///     through <see cref="IVectraCollection.GetPayloadTextAsync"/> after scoring.
///   - Implementations must be safe for concurrent <see cref="ISearchKernel.SearchAsync"/>
///     calls (single-writer / many-readers model, see TZ §5.8).
/// </summary>
public interface IVectraSnapshot : IDisposable
{
    /// <summary>Total number of live vectors in the snapshot.</summary>
    long VectorCount { get; }

    /// <summary>Dimensionality of every vector in the snapshot.</summary>
    int Dimension { get; }

    /// <summary>Metric declared by the owning collection.</summary>
    SimilarityMetric Metric { get; }

    /// <summary>
    /// Returns the vector stored at slot <paramref name="slot"/> (0-based).
    /// The returned span is valid until <see cref="IDisposable.Dispose"/>.
    /// </summary>
    ReadOnlySpan<float> GetVector(long slot);

    /// <summary>
    /// Returns the persistent vector identifier stored at slot <paramref name="slot"/>.
    /// </summary>
    long GetVectorId(long slot);
}
