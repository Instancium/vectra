namespace Instancium.Vectra.Core.Models;

public sealed record VectorIndexEntry(
    long ChunkId,
    long FileId,
    int PageNumber,
    string Snippet,
    ReadOnlyMemory<float> Vector);
