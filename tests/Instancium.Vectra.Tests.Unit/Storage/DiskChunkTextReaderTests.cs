namespace Instancium.Vectra.Tests.Unit.Storage;

using System.Text;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DiskChunkTextReaderTests : IDisposable
{
    private readonly string _indexDirectory = Path.Combine(
        Path.GetTempPath(), "vectra-chunktext", Guid.NewGuid().ToString("N"));

    public DiskChunkTextReaderTests()
    {
        Directory.CreateDirectory(_indexDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_indexDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task DiskChunkTextReader_ShouldReadExactChunkByOffset()
    {
        string chunkOne = "first chunk text";
        string chunkTwo = "second chunk text";
        string chunkThree = "third chunk text";
        WriteChunks(chunkOne, chunkTwo, chunkThree);

        var reader = new DiskChunkTextReader(_indexDirectory);
        int offset = Encoding.UTF8.GetByteCount(chunkOne);
        int length = Encoding.UTF8.GetByteCount(chunkTwo);

        string text = await reader.ReadAsync(new ChunkTextRange(offset, length));

        Assert.Equal(chunkTwo, text);
    }

    [Fact]
    public async Task DiskChunkTextReader_ShouldNotReadFullFile()
    {
        string chunkOne = "aaaa";
        string chunkTwo = "bbbb";
        string chunkThree = new string('c', 4096);
        byte[] full = WriteChunks(chunkOne, chunkTwo, chunkThree);

        var countingStream = new CountingReadStream(full);
        var reader = new DiskChunkTextReader(_indexDirectory, _ => countingStream);

        int offset = Encoding.UTF8.GetByteCount(chunkOne);
        int length = Encoding.UTF8.GetByteCount(chunkTwo);

        string text = await reader.ReadAsync(new ChunkTextRange(offset, length));

        Assert.Equal(chunkTwo, text);
        Assert.Equal(length, checked((int)countingStream.BytesRead));
        Assert.True(countingStream.BytesRead < full.Length);
    }

    [Fact]
    public async Task DiskChunkTextReader_ShouldThrowIndexCorruptedException_OnOffsetOutOfRange()
    {
        WriteChunks("small");
        var reader = new DiskChunkTextReader(_indexDirectory);

        await Assert.ThrowsAsync<IndexCorruptedException>(async () =>
            await reader.ReadAsync(new ChunkTextRange(0, 1000)));
    }

    [Fact]
    public async Task DiskChunkTextReader_ShouldReadBatchInRequestOrder()
    {
        string chunkOne = "one";
        string chunkTwo = "two";
        string chunkThree = "three";
        WriteChunks(chunkOne, chunkTwo, chunkThree);
        var reader = new DiskChunkTextReader(_indexDirectory);

        int offsetOne = 0;
        int lengthOne = Encoding.UTF8.GetByteCount(chunkOne);
        int offsetTwo = offsetOne + lengthOne;
        int lengthTwo = Encoding.UTF8.GetByteCount(chunkTwo);
        int offsetThree = offsetTwo + lengthTwo;
        int lengthThree = Encoding.UTF8.GetByteCount(chunkThree);

        IReadOnlyList<string> texts = await reader.ReadBatchAsync(new[]
        {
            new ChunkTextRange(offsetThree, lengthThree),
            new ChunkTextRange(offsetOne, lengthOne),
            new ChunkTextRange(offsetTwo, lengthTwo)
        });

        Assert.Equal(new[] { chunkThree, chunkOne, chunkTwo }, texts);
    }

    private byte[] WriteChunks(params string[] chunks)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(chunks));
        File.WriteAllBytes(Path.Combine(_indexDirectory, IndexArtifactNames.Chunks), bytes);
        return bytes;
    }

    private sealed class CountingReadStream : Stream
    {
        private readonly byte[] _data;
        private long _position;

        public CountingReadStream(byte[] data) => _data = data;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = (int)Math.Min(count, _data.Length - _position);
            Array.Copy(_data, _position, buffer, offset, toRead);
            _position += toRead;
            BytesRead += toRead;
            return toRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int toRead = (int)Math.Min(buffer.Length, _data.Length - _position);
            _data.AsSpan((int)_position, toRead).CopyTo(buffer.Span);
            _position += toRead;
            BytesRead += toRead;
            return ValueTask.FromResult(toRead);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _data.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
