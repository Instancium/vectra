namespace Instancium.Vectra.Core.Models;

public sealed record FileState(
    long FileId,
    string FilePath,
    string FileHash,
    DateTimeOffset LastIndexedAt,
    int ChunkCount,
    bool IsActive);
