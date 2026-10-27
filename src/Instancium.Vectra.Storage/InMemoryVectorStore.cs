namespace Instancium.Vectra.Storage;

using System.Threading;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<ChunkMetadata> _metadata = new();
    private readonly object _syncRoot = new();
    private float[] _vectors = Array.Empty<float>();
    private int _dimension;

    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _metadata.Count;
            }
        }
    }

    public int Dimension
    {
        get
        {
            lock (_syncRoot)
            {
                return _dimension;
            }
        }
    }

    public void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentNullException.ThrowIfNull(vector);

        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        lock (_syncRoot)
        {
            if (_dimension == 0)
            {
                _dimension = vector.Length;
            }
            else if (_dimension != vector.Length)
            {
                throw new ArgumentException($"Vector dimension mismatch. Expected {_dimension}, received {vector.Length}.", nameof(vector));
            }

            var metadata = new ChunkMetadata(chunkId, fileId, pageNumber, snippet);
            var index = _metadata.FindIndex(item => item.ChunkId == chunkId);

            if (index >= 0)
            {
                _metadata[index] = metadata;

                var offset = index * _dimension;
                vector.Span.CopyTo(_vectors.AsSpan(offset, _dimension));
                return;
            }

            var requiredCapacity = (_metadata.Count + 1) * _dimension;
            if (_vectors.Length < requiredCapacity)
            {
                var newCapacity = _vectors.Length == 0 ? Math.Max(_dimension, 64) : _vectors.Length;
                while (newCapacity < requiredCapacity)
                {
                    newCapacity *= 2;
                }

                var resized = new float[newCapacity];
                _vectors.CopyTo(resized, 0);
                _vectors = resized;
            }

            var insertOffset = _metadata.Count * _dimension;
            vector.Span.CopyTo(_vectors.AsSpan(insertOffset, _dimension));
            _metadata.Add(metadata);
        }
    }

    public ReadOnlySpan<float> GetVectorsSpan()
    {
        lock (_syncRoot)
        {
            return _vectors.AsSpan(0, _metadata.Count * _dimension);
        }
    }

    public IReadOnlyList<ChunkMetadata> GetMetadata()
    {
        lock (_syncRoot)
        {
            return _metadata.ToArray();
        }
    }
}
