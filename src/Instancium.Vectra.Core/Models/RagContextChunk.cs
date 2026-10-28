namespace Instancium.Vectra.Core.Models;

public sealed record RagContextChunk(long ChunkId, int PageNumber, float Score, string Snippet);
