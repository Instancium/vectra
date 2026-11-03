#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A single vector submitted for storage.
/// <see cref="VectorId"/> is null when the engine must auto-assign the identifier.
/// <see cref="Payload"/> is the opaque text stored alongside the vector and served lazily.
/// </summary>
public sealed record VectorEntry
{
    public VectorEntry(long? vectorId, ReadOnlyMemory<float> vector, string? payload = null)
    {
        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        VectorId = vectorId;
        Vector = vector;
        Payload = payload;
    }

    public long? VectorId { get; }

    public ReadOnlyMemory<float> Vector { get; }

    public string? Payload { get; }
}
