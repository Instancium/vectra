// src/Instancium.Vectra.Storage/InMemoryVectorStore.cs
#nullable enable
namespace Instancium.Vectra.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly object _sync = new();
    private readonly List<ChunkMetadata> _metadata = new();
    private readonly Dictionary<long, int> _chunkIdToIndex = new();
    private float[] _vectors = Array.Empty<float>();
    private int _count;
    private int _dimension;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    public int Dimension
    {
        get
        {
            lock (_sync)
            {
                return _dimension;
            }
        }
    }

    public ReadOnlySpan<float> GetVectorsSpan()
    {
        lock (_sync)
        {
            return _vectors.AsSpan(0, _count * _dimension);
        }
    }

    public IReadOnlyList<ChunkMetadata> GetMetadata()
    {
        lock (_sync)
        {
            return _metadata.ToArray();
        }
    }

    public void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        if (vector.Length == 0)
        {
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        }

        if (snippet.Length == 0)
        {
            snippet = string.Empty;
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
                    $"Dimension mismatch: store uses {_dimension} but received {vector.Length}.",
                    nameof(vector));
            }

            if (_chunkIdToIndex.TryGetValue(chunkId, out int existingIndex))
            {
                vector.Span.CopyTo(_vectors.AsSpan(existingIndex * _dimension, _dimension));
                _metadata[existingIndex] = new ChunkMetadata(chunkId, fileId, pageNumber, snippet);
            }
            else
            {
                int requiredLength = (_count + 1) * _dimension;
                if (_vectors.Length < requiredLength)
                {
                    Array.Resize(ref _vectors, Math.Max(requiredLength, _vectors.Length * 2));
                }

                vector.Span.CopyTo(_vectors.AsSpan(_count * _dimension, _dimension));
                _metadata.Add(new ChunkMetadata(chunkId, fileId, pageNumber, snippet));
                _chunkIdToIndex[chunkId] = _count;
                _count++;
            }
        }
    }
}