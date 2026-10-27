namespace Instancium.Vectra.Tests.Integration.Chunking;

using System.Text;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Chunking;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Integration")]
public sealed class PageAwareFixedSizeChunkerIntegrationTests
{
    private const int ChunkSize = 1200;
    private const int Overlap = 200;

    [Fact]
    public async Task PageAwareChunker_ShouldChunkRealCorpusPagesWithinBoundaries()
    {
        var corpusPath = Environment.GetEnvironmentVariable("VECTRA_TEST_CORPUS_PDF");

        if (string.IsNullOrWhiteSpace(corpusPath))
        {
            return;
        }

        Assert.True(File.Exists(corpusPath), $"Corpus PDF not found: {corpusPath}");

        var extractor = new PdfTextExtractor();
        var pages = await extractor.ExtractAsync(new TextExtractionRequest(corpusPath, 1, 20));

        var chunker = new PageAwareFixedSizeChunker(new TextChunkingOptions(ChunkSize, Overlap));

        var chunks = chunker.Chunk(pages, corpusPath);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.InRange(chunk.PageNumber, 1, 20));
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= ChunkSize));
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
        Assert.All(chunks, chunk => Assert.Equal(corpusPath, chunk.SourceFile));

        foreach (var group in chunks.GroupBy(chunk => chunk.PageNumber))
        {
            var pageText = pages.Single(page => page.PageNumber == group.Key).Text;

            Assert.Equal(pageText, Reconstruct(group.ToList(), Overlap));
        }
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
