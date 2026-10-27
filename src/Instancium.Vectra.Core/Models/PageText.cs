#nullable enable

namespace Instancium.Vectra.Core.Models;

public sealed record PageText
{
    public PageText(int pageNumber, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentNullException.ThrowIfNull(text);

        PageNumber = pageNumber;
        Text = text;
    }

    public int PageNumber { get; }

    public string Text { get; }
}
