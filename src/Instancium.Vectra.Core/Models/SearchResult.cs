namespace Instancium.Vectra.Core.Models;

public sealed record SearchResult(
    long ChunkId,
    long FileId,
    float Score,
    string Snippet,
    ReadOnlyMemory<float> Vector);
