#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// State of one source file tracked by the incremental index (a <c>file.state.json</c> entry, TZ-2 section 4.3).
/// </summary>
public sealed record FileState
{
    public FileState(
        long fileId,
        string path,
        string sha256,
        long sizeBytes,
        string format,
        IReadOnlyList<long> chunkIds,
        DateTimeOffset indexedAt,
        IndexFileStatus status)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fileId, 1L);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(chunkIds);
        foreach (long chunkId in chunkIds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chunkId, 1L);
        }

        FileId = fileId;
        Path = path;
        Sha256 = sha256;
        SizeBytes = sizeBytes;
        Format = format;
        ChunkIds = chunkIds.ToArray();
        IndexedAt = indexedAt;
        Status = status;
    }

    public long FileId { get; }
    public string Path { get; }
    public string Sha256 { get; }
    public long SizeBytes { get; }
    public string Format { get; }
    public IReadOnlyList<long> ChunkIds { get; }
    public DateTimeOffset IndexedAt { get; }
    public IndexFileStatus Status { get; }
}
