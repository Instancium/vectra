namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DiskFileStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset IndexedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _indexDirectory = Path.Combine(
        Path.GetTempPath(), "vectra-stage4-state", Guid.NewGuid().ToString("N"));
    private readonly DiskFileStateStore _store = new();
    private readonly DiskIndexReader _indexReader = new();

    public DiskFileStateStoreTests()
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
    public async Task DiskFileStateStore_ShouldReturnEmpty_WhenFileDoesNotExist()
    {
        IReadOnlyList<FileState> states = await _store.LoadAsync(_indexDirectory);

        Assert.Empty(states);
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldRoundTrip()
    {
        FileState[] original =
        {
            new(2, "docs/guide.pdf", "bbbb", 200, "pdf", new long[] { 3 }, IndexedAt, IndexFileStatus.Indexed),
            new(1, "docs/manual.pdf", "aaaa", 100, "pdf", new long[] { 1, 2 }, IndexedAt, IndexFileStatus.Failed)
        };

        await _store.SaveAsync(_indexDirectory, original);
        IReadOnlyList<FileState> loaded = await _store.LoadAsync(_indexDirectory);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new long[] { 1, 2 }, loaded.Select(state => state.FileId));

        Assert.Equal("docs/manual.pdf", loaded[0].Path);
        Assert.Equal(IndexFileStatus.Failed, loaded[0].Status);
        Assert.Equal(new long[] { 1, 2 }, loaded[0].ChunkIds);

        Assert.Equal("docs/guide.pdf", loaded[1].Path);
        Assert.Equal(new long[] { 3 }, loaded[1].ChunkIds);

        Assert.DoesNotContain(
            Directory.EnumerateFiles(_indexDirectory),
            file => file.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldBeReadableByDiskIndexReader()
    {
        FileState original = new(
            1, "docs/manual.pdf", "aaaa", 100, "pdf", new long[] { 1 }, IndexedAt, IndexFileStatus.Indexed);

        await _store.SaveAsync(_indexDirectory, new[] { original });
        IReadOnlyList<FileState> viaReader = await _indexReader.ReadFileStatesAsync(_indexDirectory);

        FileState actual = Assert.Single(viaReader);
        Assert.Equal(original.FileId, actual.FileId);
        Assert.Equal(original.Path, actual.Path);
        Assert.Equal(original.Sha256, actual.Sha256);
        Assert.Equal(original.SizeBytes, actual.SizeBytes);
        Assert.Equal(original.Format, actual.Format);
        Assert.Equal(original.ChunkIds, actual.ChunkIds);
        Assert.Equal(original.IndexedAt, actual.IndexedAt);
        Assert.Equal(original.Status, actual.Status);
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldOverwritePreviousState()
    {
        await _store.SaveAsync(_indexDirectory, new[]
        {
            new FileState(1, "a.pdf", "h1", 1, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed)
        });
        await _store.SaveAsync(_indexDirectory, new[]
        {
            new FileState(7, "b.pdf", "h2", 2, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed)
        });

        IReadOnlyList<FileState> loaded = await _store.LoadAsync(_indexDirectory);

        FileState state = Assert.Single(loaded);
        Assert.Equal(7, state.FileId);
        Assert.Equal("b.pdf", state.Path);
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldThrowIndexCorrupted_OnMalformedJson()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_indexDirectory, IndexArtifactNames.FileState),
            "{ not json");

        await Assert.ThrowsAsync<IndexCorruptedException>(() => _store.LoadAsync(_indexDirectory));
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldThrow_OnDuplicateFileIds()
    {
        FileState[] states =
        {
            new(1, "a.pdf", "h1", 1, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed),
            new(1, "b.pdf", "h2", 2, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(_indexDirectory, states));
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldThrow_OnDuplicatePaths()
    {
        FileState[] states =
        {
            new(1, "a.pdf", "h1", 1, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed),
            new(2, "a.pdf", "h2", 2, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(_indexDirectory, states));
    }

    [Fact]
    public async Task DiskFileStateStore_ShouldCreateDirectory_WhenMissing()
    {
        string nested = Path.Combine(_indexDirectory, "deep", "index");

        await _store.SaveAsync(nested, new[]
        {
            new FileState(1, "a.pdf", "h1", 1, "pdf", Array.Empty<long>(), IndexedAt, IndexFileStatus.Indexed)
        });

        Assert.True(File.Exists(Path.Combine(nested, IndexArtifactNames.FileState)));
    }
}
