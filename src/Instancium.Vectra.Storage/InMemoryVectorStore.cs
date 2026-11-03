#nullable enable
namespace Instancium.Vectra.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Thread-safe in-memory vector store built on copy-on-write immutable snapshots.
///
/// Defect #4 fix: the backing float array is never mutated in place; it is replaced
/// atomically under a writer lock. <see cref="GetVectorsSpan"/> therefore returns a span
/// over an immutable array that no writer can tear, and readers need no locks at all.
///
/// Trade-off (recorded): Upsert is an O(n) copy. Acceptable for index-time batch writes;
/// segmentized mutable storage with lock-free readers arrives in P6.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly object _sync = new();
    private float[] _vectors = Array.Empty<float>();
    private ChunkMetadata[] _metadata = Array.Empty<ChunkMetadata>();
    private int _dimension;

    public int Count
    {
        get
        {
            int dimension = Volatile.Read(ref _dimension);
            return dimension == 0 ? 0 : Volatile.Read(ref _vectors).Length / dimension;
        }
    }

    public int Dimension => Volatile.Read(ref _dimension);

    public void Upsert(long chunkId, long fileId, int pageNumber, string? snippet, ReadOnlyMemory<float> vector)
    {
        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        lock (_sync)
        {
            if (_dimension == 0)
            {
                _dimension = vector.Length;
            }
            else if (_dimension != vector.Length)
            {
                throw new ArgumentException(
                    "Dimension mismatch: store=" + _dimension + ", vector=" + vector.Length + ".",
                    nameof(vector));
            }

            float[] oldVectors = _vectors;
            ChunkMetadata[] oldMetadata = _metadata;

            for (int i = 0; i < oldMetadata.Length; i++)
            {
                if (oldMetadata[i].ChunkId != chunkId)
                {
                    continue;
                }

                float[] replaced = new float[oldVectors.Length];
                Array.Copy(oldVectors, replaced, oldVectors.Length);
                vector.Span.CopyTo(replaced.AsSpan(i * _dimension, _dimension));

                ChunkMetadata[] replacedMetadata = (ChunkMetadata[])oldMetadata.Clone();
                replacedMetadata[i] = new ChunkMetadata(chunkId, fileId, pageNumber, snippet ?? string.Empty);

                Volatile.Write(ref _vectors, replaced);
                Volatile.Write(ref _metadata, replacedMetadata);
                return;
            }

            float[] appended = new float[oldVectors.Length + _dimension];
            Array.Copy(oldVectors, appended, oldVectors.Length);
            vector.Span.CopyTo(appended.AsSpan(oldVectors.Length, _dimension));

            ChunkMetadata[] appendedMetadata = new ChunkMetadata[oldMetadata.Length + 1];
            Array.Copy(oldMetadata, appendedMetadata, oldMetadata.Length);
            appendedMetadata[^1] = new ChunkMetadata(chunkId, fileId, pageNumber, snippet ?? string.Empty);

            Volatile.Write(ref _vectors, appended);
            Volatile.Write(ref _metadata, appendedMetadata);
        }
    }

    public ReadOnlySpan<float> GetVectorsSpan() => Volatile.Read(ref _vectors);

    public IReadOnlyList<ChunkMetadata> GetMetadata() => Volatile.Read(ref _metadata);

    public void Clear()
    {
        lock (_sync)
        {
            Volatile.Write(ref _vectors, Array.Empty<float>());
            Volatile.Write(ref _metadata, Array.Empty<ChunkMetadata>());
            Volatile.Write(ref _dimension, 0);
        }
    }
}
