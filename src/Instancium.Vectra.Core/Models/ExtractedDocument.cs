#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Result of content extraction. For PDF the pages are physical pages;
/// for pageless formats an extractor partitions content into logical
/// segments numbered from 1.
/// </summary>
public sealed record ExtractedDocument
{
    public ExtractedDocument(string sourcePath, string sourceKind, IReadOnlyList<PageText> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKind);
        ArgumentNullException.ThrowIfNull(pages);

        SourcePath = sourcePath;
        SourceKind = sourceKind;
        Pages = pages.ToArray();
    }

    public string SourcePath { get; }

    public string SourceKind { get; }

    public IReadOnlyList<PageText> Pages { get; }
}
