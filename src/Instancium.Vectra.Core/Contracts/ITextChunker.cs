namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface ITextChunker
{
    IReadOnlyList<TextChunk> Chunk(IReadOnlyList<PageText> pages, string sourceFile);
}
