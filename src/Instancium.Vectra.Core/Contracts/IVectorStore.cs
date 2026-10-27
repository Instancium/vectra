namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IVectorStore
{
    void Upsert(long chunkId, long fileId, int pageNumber, string snippet, ReadOnlyMemory<float> vector);

    int Count { get; }

    int Dimension { get; }

    ReadOnlySpan<float> GetVectorsSpan();

    IReadOnlyList<ChunkMetadata> GetMetadata();
}
