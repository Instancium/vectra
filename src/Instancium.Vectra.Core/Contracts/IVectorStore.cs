namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Write/read abstraction over the vector corpus.
/// </summary>
public interface IVectorStore
{
    void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector);

    int Count { get; }

    int Dimension { get; }

    ReadOnlySpan<float> GetVectorsSpan();

    IReadOnlyList<ChunkMetadata> GetMetadata();

    /// <summary>
    /// Creates an immutable, point-in-time <see cref="IVectraSnapshot"/> for search.
    ///
    /// Contract:
    ///   - Implementations own the snapshot lifecycle and MAY cache instances; callers must
    ///     therefore never <see cref="IDisposable.Dispose"/> the returned snapshot.
    ///   - Repeated calls without intervening writes should be O(1) (cached), so the search
    ///     hot path never re-copies or re-normalizes the corpus.
    ///   - The snapshot isolates readers from concurrent writes (single-writer / many-readers,
    ///     TZ §5.8): writes after this call are invisible to the returned instance.
    /// </summary>
    IVectraSnapshot CreateSnapshot(SimilarityMetric metric);
}