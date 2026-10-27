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

    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
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
            throw new ArgumentException(
                $"Query dimension mismatch. Expected {_vectorStore.Dimension} but received {queryVector.Length}.",
                nameof(queryVector));
        }

        IReadOnlyList<ChunkMetadata> metadata = _vectorStore.GetMetadata();
        ReadOnlySpan<float> vectors = _vectorStore.GetVectorsSpan();
        int dimension = _vectorStore.Dimension;
        var heap = new PriorityQueue<SearchCandidate, float>();

        for (int index = 0; index < metadata.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = index * dimension;
            float score = ComputeDotProduct(queryVector.Span, vectors.Slice(offset, dimension));
            var candidate = new SearchCandidate(index, score);
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

        List<SearchCandidate> ordered = heap.UnorderedItems
            .OrderByDescending(item => item.Element.Score)
            .Select(item => item.Element)
            .ToList();

        var results = new List<SearchResult>(ordered.Count);
        foreach (SearchCandidate candidate in ordered)
        {
            ChunkMetadata meta = metadata[candidate.Index];
            int offset = candidate.Index * dimension;
            results.Add(new SearchResult(
                meta.ChunkId,
                meta.FileId,
                meta.PageNumber,
                candidate.Score,
                meta.Snippet,
                vectors.Slice(offset, dimension).ToArray()));
        }

        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }

    private static float ComputeDotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        float sum = 0f;
        if (Vector.IsHardwareAccelerated && left.Length >= Vector<float>.Count)
        {
            int vectorLength = left.Length - (left.Length % Vector<float>.Count);
            var accumulator = Vector<float>.Zero;
            for (int index = 0; index < vectorLength; index += Vector<float>.Count)
            {
                accumulator += new Vector<float>(left.Slice(index, Vector<float>.Count))
                               * new Vector<float>(right.Slice(index, Vector<float>.Count));
            }

            sum += Vector.Sum(accumulator);
        }

        for (int index = left.Length - (left.Length % Vector<float>.Count); index < left.Length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }

    private sealed record SearchCandidate(int Index, float Score);
}