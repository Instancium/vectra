namespace Instancium.Vectra.Search;

using System.Numerics;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

public sealed class InMemoryVectorSearchEngine : IVectorSearchEngine
{
    private readonly IVectorStore _vectorStore;

    public InMemoryVectorSearchEngine(IVectorStore vectorStore)
    {
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);

        if (queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be empty.", nameof(queryVector));
        }

        if (_vectorStore.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
        }

        if (queryVector.Length != _vectorStore.Dimension)
        {
            throw new ArgumentException($"Query dimension mismatch. Expected {_vectorStore.Dimension} but received {queryVector.Length}.", nameof(queryVector));
        }

        var metadata = _vectorStore.GetMetadata();
        var vectors = _vectorStore.GetVectorsSpan();
        var heap = new PriorityQueue<SearchCandidate, float>();

        for (var index = 0; index < metadata.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = metadata[index];
            var offset = index * _vectorStore.Dimension;
            var score = ComputeDotProduct(queryVector.Span, vectors.Slice(offset, _vectorStore.Dimension));
            var candidate = new SearchCandidate(item.ChunkId, item.FileId, score, item.Snippet);

            if (heap.Count < topK)
            {
                heap.Enqueue(candidate, candidate.Score);
                continue;
            }

            if (candidate.Score <= heap.Peek().Score)
            {
                continue;
            }

            heap.Dequeue();
            heap.Enqueue(candidate, candidate.Score);
        }

        var result = heap.UnorderedItems
            .OrderByDescending(item => item.Element.Score)
            .Select(item => new SearchResult(item.Element.ChunkId, item.Element.FileId, item.Element.Score, item.Element.Snippet, ReadOnlyMemory<float>.Empty))
            .ToList();

        return Task.FromResult<IReadOnlyList<SearchResult>>(result);
    }

    private static float ComputeDotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var sum = 0f;

        if (Vector.IsHardwareAccelerated && left.Length >= Vector<float>.Count)
        {
            var vectorLength = left.Length - (left.Length % Vector<float>.Count);
            var acc = Vector<float>.Zero;

            for (var index = 0; index < vectorLength; index += Vector<float>.Count)
            {
                var leftVector = new Vector<float>(left.Slice(index, Vector<float>.Count));
                var rightVector = new Vector<float>(right.Slice(index, Vector<float>.Count));
                acc += leftVector * rightVector;
            }

            sum += Vector.Sum(acc);
        }

        var tailStart = left.Length - (left.Length % Vector<float>.Count);
        for (var index = tailStart; index < left.Length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }

    private sealed record SearchCandidate(long ChunkId, long FileId, float Score, string Snippet);
}
