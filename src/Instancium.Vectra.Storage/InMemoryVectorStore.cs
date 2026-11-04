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
/// P4: <see cref="CreateSnapshot"/> wraps the immutable COW array with ZERO copy for
/// DotProduct/Euclidean and materializes a single normalized copy for Cosine, cached per
/// store version. Repeated searches therefore cost a pure scan, keeping the p95 ≤ 30 ms
/// bench budget at 5 000 × 1024.
///
/// Trade-off (recorded): Upsert is an O(n) copy. Acceptable for index-time batch writes;
/// segmentized mutable storage with lock-free readers arrives in P6/P7.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly object _sync = new();
    private float[] _vectors = Array.Empty<float>();
    private ChunkMetadata[] _metadata = Array.Empty<ChunkMetadata>();
    private int _dimension;
    private long _version;

    private InMemoryVectraSnapshot? _cachedSnapshot;
    private SimilarityMetric _cachedMetric;
    private long _cachedVersion = -1;

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
                _version++;
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
            _version++;
        }
    }

    public bool Remove(long chunkId)
    {
        lock (_sync)
        {
            ChunkMetadata[] oldMetadata = _metadata;
            int indexToRemove = -1;
            for (int i = 0; i < oldMetadata.Length; i++)
            {
                if (oldMetadata[i].ChunkId == chunkId)
                {
                    indexToRemove = i;
                    break;
                }
            }

            if (indexToRemove < 0)
            {
                return false;
            }

            int newCount = oldMetadata.Length - 1;
            float[] oldVectors = _vectors;

            float[] replacedVectors = new float[newCount * _dimension];
            ChunkMetadata[] replacedMetadata = new ChunkMetadata[newCount];

            if (indexToRemove > 0)
            {
                Array.Copy(oldMetadata, 0, replacedMetadata, 0, indexToRemove);
                Array.Copy(oldVectors, 0, replacedVectors, 0, indexToRemove * _dimension);
            }

            if (indexToRemove < newCount)
            {
                Array.Copy(oldMetadata, indexToRemove + 1, replacedMetadata, indexToRemove, newCount - indexToRemove);
                Array.Copy(oldVectors, (indexToRemove + 1) * _dimension, replacedVectors, indexToRemove * _dimension, (newCount - indexToRemove) * _dimension);
            }

            Volatile.Write(ref _vectors, replacedVectors);
            Volatile.Write(ref _metadata, replacedMetadata);
            _version++;
            _cachedSnapshot = null;
            _cachedVersion = -1;
            return true;
        }
    }

    public string? GetPayload(long chunkId)
    {
        ChunkMetadata[] currentMetadata = Volatile.Read(ref _metadata);
        foreach (ChunkMetadata meta in currentMetadata)
        {
            if (meta.ChunkId == chunkId)
            {
                return meta.Payload;
            }
        }

        return null;
    }

    public ReadOnlySpan<float> GetVectorsSpan() => Volatile.Read(ref _vectors);

    public IReadOnlyList<ChunkMetadata> GetMetadata() => Volatile.Read(ref _metadata);

    public IVectraSnapshot CreateSnapshot(SimilarityMetric metric)
    {
        lock (_sync)
        {
            if (_cachedSnapshot is not null && _cachedVersion == _version && _cachedMetric == metric)
            {
                return _cachedSnapshot;
            }

            long[] vectorIds = new long[_metadata.Length];
            for (int i = 0; i < _metadata.Length; i++)
            {
                vectorIds[i] = _metadata[i].ChunkId;
            }

            // Zero-copy for non-Cosine metrics: the COW array is immutable by construction,
            // so the snapshot may reference it directly without owning it.
            var snapshot = new InMemoryVectraSnapshot(_vectors, vectorIds, _dimension, metric);

            _cachedSnapshot = snapshot;
            _cachedMetric = metric;
            _cachedVersion = _version;
            return snapshot;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            Volatile.Write(ref _vectors, Array.Empty<float>());
            Volatile.Write(ref _metadata, Array.Empty<ChunkMetadata>());
            Volatile.Write(ref _dimension, 0);
            _version++;
            _cachedSnapshot = null;
            _cachedVersion = -1;
        }
    }
}