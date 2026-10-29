namespace Instancium.Vectra.Storage.Disk;

using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Default <see cref="IChunkTextReader"/> implementation (TZ-2 stage 3).
/// Reads chunk texts from <c>chunks.bin</c> by byte range, seeking straight to the requested
/// offset and reading only the requested number of bytes. The rest of the file is never touched.
/// </summary>
public sealed class DiskChunkTextReader : IChunkTextReader
{
    private readonly string _chunksPath;
    private readonly Func<string, Stream> _openStream;

    public DiskChunkTextReader(string indexDirectory)
        : this(indexDirectory, OpenFileStream)
    {
    }

    /// <summary>Test seam: allows substituting the underlying stream.</summary>
    internal DiskChunkTextReader(string indexDirectory, Func<string, Stream> openStream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        ArgumentNullException.ThrowIfNull(openStream);
        _chunksPath = Path.Combine(Path.GetFullPath(indexDirectory), IndexArtifactNames.Chunks);
        _openStream = openStream;
    }

    /// <inheritdoc />
    public async ValueTask<string> ReadAsync(ChunkTextRange range, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        using Stream stream = _openStream(_chunksPath);
        return await ReadRangeAsync(stream, range, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReadBatchAsync(
        IReadOnlyList<ChunkTextRange> ranges,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (ranges.Count == 0)
        {
            return Array.Empty<string>();
        }

        var texts = new string[ranges.Count];
        using Stream stream = _openStream(_chunksPath);
        for (int i = 0; i < ranges.Count; i++)
        {
            texts[i] = await ReadRangeAsync(stream, ranges[i], cancellationToken).ConfigureAwait(false);
        }
        return texts;
    }

    private static async Task<string> ReadRangeAsync(Stream stream, ChunkTextRange range, CancellationToken cancellationToken)
    {
        // Overflow-safe bounds check.
        if (range.Offset > stream.Length || range.Length > stream.Length - range.Offset)
        {
            throw new IndexCorruptedException(
                $"Chunk text range [{range.Offset}, {range.Offset + range.Length}) is out of range; " +
                $"chunks.bin size is {stream.Length}.");
        }

        stream.Seek(range.Offset, SeekOrigin.Begin);
        var buffer = new byte[range.Length];
        int totalRead = 0;
        while (totalRead < range.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(totalRead, range.Length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                throw new IndexCorruptedException("Unexpected end of chunks.bin while reading chunk text.");
            }
            totalRead += read;
        }
        return Encoding.UTF8.GetString(buffer);
    }

    private static Stream OpenFileStream(string path) =>
        new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
}
