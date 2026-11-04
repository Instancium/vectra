namespace Instancium.Vectra.Storage.Disk;

internal sealed record ManifestDto(
    int Version,
    int Dimension,
    string ModelId,
    IReadOnlyList<string> Formats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int FileCount,
    int ChunkCount);

internal sealed record ChunkMapEntryDto(
    long ChunkId,
    long FileId,
    int PageNumber,
    long Offset,
    int Length,
    bool IsActive);
