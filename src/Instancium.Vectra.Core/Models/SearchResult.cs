namespace Instancium.Vectra.Core.Models;

public sealed record SearchResult(
    long ChunkId,
    long FileId,
    int PageNumber,
    float Score,
    string Snippet,
    ReadOnlyMemory<float> Vector);
