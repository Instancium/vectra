namespace Instancium.Vectra.Indexing.Chunking;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

public sealed class PageAwareFixedSizeChunker : ITextChunker
{
    private readonly TextChunkingOptions _options;

    public PageAwareFixedSizeChunker(TextChunkingOptions? options = null)
    {
        _options = options ?? new TextChunkingOptions();
    }

    public IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        var chunks = new List<TextChunk>();

        foreach (var page in pages.OrderBy(page => page.PageNumber))
        {
            if (string.IsNullOrWhiteSpace(page.Text))
            {
                continue;
            }

            var start = 0;

            while (start < page.Text.Length)
            {
                var end = Math.Min(start + _options.ChunkSizeChars, page.Text.Length);
                var chunkText = page.Text[start..end];

                if (!string.IsNullOrWhiteSpace(chunkText))
                {
                    chunks.Add(new TextChunk(chunkText, page.PageNumber, sourceFile));
                }

                if (end == page.Text.Length)
                {
                    break;
                }

                start = end - _options.ChunkOverlapChars;
            }
        }

        return chunks;
    }
}
