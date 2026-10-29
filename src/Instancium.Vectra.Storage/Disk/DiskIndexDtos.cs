namespace Instancium.Vectra.Storage.Disk;

using Instancium.Vectra.Core.Models;

internal sealed record ManifestDto(
    int Version,
    int Dimension,
    string ModelId,
    IReadOnlyList<string> Formats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int FileCount,
    int ChunkCount);

internal sealed record FileStateDto(
    string Path,
    string Sha256,
    long SizeBytes,
    string Format,
    IReadOnlyList<long> ChunkIds,
    DateTimeOffset IndexedAt,
    IndexFileStatus Status);

internal sealed record ChunkMapEntryDto(
    long ChunkId,
    long FileId,
    int PageNumber,
    long Offset,
    int Length,
    bool IsActive);
