#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record TextExtractionRequest
{
    public TextExtractionRequest(string filePath, int pageFrom, int pageTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageFrom, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageTo, pageFrom);

        FilePath = filePath;
        PageFrom = pageFrom;
        PageTo = pageTo;
    }

    public string FilePath { get; }

    public int PageFrom { get; }

    public int PageTo { get; }
}
