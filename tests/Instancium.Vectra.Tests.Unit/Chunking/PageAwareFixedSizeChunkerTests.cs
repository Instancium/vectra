namespace Instancium.Vectra.Tests.Unit.Chunking;

using System.Text;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Chunking;
using Xunit;

[Trait("Category", "Unit")]
public sealed class PageAwareFixedSizeChunkerTests
{
    private const int ChunkSize = 50;
    private const int Overlap = 10;

    private static readonly TextChunkingOptions Options = new(ChunkSize, Overlap);

    [Fact]
    public void PageAwareChunker_ShouldRespectPageBoundariesAndOverlap()
    {
        var pageOneText = CreateText(120, 0);
        var pageTwoText = CreateText(30, 7);
        var pages = new[]
        {
            new PageText(1, pageOneText),
            new PageText(2, pageTwoText),
        };
        var chunker = new PageAwareFixedSizeChunker(Options);

        var chunks = chunker.Chunk(pages, "sample.pdf");

        Assert.Equal(4, chunks.Count);
        Assert.Equal(new[] { 1, 1, 1, 2 }, chunks.Select(chunk => chunk.PageNumber));
        Assert.All(chunks, chunk => Assert.Equal("sample.pdf", chunk.SourceFile));
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= ChunkSize));

        var pageOneChunks = chunks.Where(chunk => chunk.PageNumber == 1).ToList();
        var pageTwoChunks = chunks.Where(chunk => chunk.PageNumber == 2).ToList();

        Assert.Equal(3, pageOneChunks.Count);
        Assert.Equal(ChunkSize, pageOneChunks[0].Text.Length);
        Assert.Equal(ChunkSize, pageOneChunks[1].Text.Length);
        Assert.Equal(40, pageOneChunks[2].Text.Length);
        Assert.Single(pageTwoChunks);

        Assert.Equal(pageOneText[..ChunkSize], pageOneChunks[0].Text);
        Assert.Equal(
            pageOneChunks[0].Text[^Overlap..],
            pageOneChunks[1].Text[..Overlap]);
        Assert.Equal(
            pageOneChunks[1].Text[^Overlap..],
            pageOneChunks[2].Text[..Overlap]);

        Assert.Contains(pageOneChunks[0].Text, pageOneText);
        Assert.Contains(pageOneChunks[1].Text, pageOneText);
        Assert.Contains(pageOneChunks[2].Text, pageOneText);
        Assert.Contains(pageTwoChunks[0].Text, pageTwoText);

        Assert.Equal(pageOneText, Reconstruct(pageOneChunks, Overlap));
        Assert.Equal(pageTwoText, Reconstruct(pageTwoChunks, 0));
    }

    [Fact]
    public void PageAwareChunker_ShouldProduceSingleChunkWhenPageFitsChunkSize()
    {
        var text = CreateText(ChunkSize, 3);
        var chunker = new PageAwareFixedSizeChunker(Options);

        var chunks = chunker.Chunk(new[] { new PageText(7, text) }, "doc.pdf");

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Text);
        Assert.Equal(7, chunk.PageNumber);
    }

    [Fact]
    public void PageAwareChunker_ShouldTileWithoutOverlapWhenOverlapIsZero()
    {
        var text = CreateText(120, 1);
        var chunker = new PageAwareFixedSizeChunker(new TextChunkingOptions(ChunkSize, 0));

        var chunks = chunker.Chunk(new[] { new PageText(1, text) }, "doc.pdf");

        Assert.Equal(3, chunks.Count);
        Assert.Equal(text, string.Concat(chunks.Select(chunk => chunk.Text)));
    }

    [Fact]
    public void PageAwareChunker_ShouldReturnEmptyForNoPages()
    {
        var chunker = new PageAwareFixedSizeChunker(Options);

        var chunks = chunker.Chunk(Array.Empty<PageText>(), "doc.pdf");

        Assert.Empty(chunks);
    }

    [Fact]
    public void PageAwareChunker_ShouldSkipWhitespacePages()
    {
        var chunker = new PageAwareFixedSizeChunker(Options);

        var chunks = chunker.Chunk(
            new[] { new PageText(1, "   "), new PageText(2, CreateText(10, 0)) },
            "doc.pdf");

        var chunk = Assert.Single(chunks);
        Assert.Equal(2, chunk.PageNumber);
    }

    [Fact]
    public void PageAwareChunker_ShouldUseDefaultOptionsWhenNotProvided()
    {
        var text = CreateText(TextChunkingOptions.DefaultChunkSizeChars + 10, 0);
        var chunker = new PageAwareFixedSizeChunker();

        var chunks = chunker.Chunk(new[] { new PageText(1, text) }, "doc.pdf");

        Assert.Equal(2, chunks.Count);
        Assert.Equal(TextChunkingOptions.DefaultChunkSizeChars, chunks[0].Text.Length);
        Assert.Equal(
            chunks[0].Text[^TextChunkingOptions.DefaultChunkOverlapChars..],
            chunks[1].Text[..TextChunkingOptions.DefaultChunkOverlapChars]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 10)]
    [InlineData(10, 11)]
    public void PageAwareChunker_ShouldThrowWhenOptionsAreInvalid(int chunkSize, int overlap)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new TextChunkingOptions(chunkSize, overlap));
    }

    [Fact]
    public void PageAwareChunker_ShouldThrowWhenSourceFileIsInvalid()
    {
        var chunker = new PageAwareFixedSizeChunker(Options);

        Assert.ThrowsAny<ArgumentException>(() => chunker.Chunk(new[] { new PageText(1, "text") }, "  "));
    }

    private static string CreateText(int length, int seed)
    {
        var builder = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            builder.Append((char)('a' + (i + seed) % 26));
        }

        return builder.ToString();
    }

    private static string Reconstruct(IReadOnlyList<TextChunk> chunks, int overlap)
    {
        var builder = new StringBuilder(chunks[0].Text);

        for (var i = 1; i < chunks.Count; i++)
        {
            builder.Append(chunks[i].Text[overlap..]);
        }

        return builder.ToString();
    }
}
