#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record TextChunk
{
    public TextChunk(string text, int pageNumber, string sourceFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        Text = text;
        PageNumber = pageNumber;
        SourceFile = sourceFile;
    }

    public string Text { get; }

    public int PageNumber { get; }

    public string SourceFile { get; }
}
