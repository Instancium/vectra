namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Extracts text content from a source file of a specific format.
/// Currently only PDF is implemented; markdown, docx, html and OCR
/// are added later without changing the index format.
/// </summary>
public interface IContentExtractor
{
    /// <summary>
    /// Format discriminator stored in file.state and the index manifest.
    /// Examples: pdf, markdown, ocr-image, docx, html.
    /// </summary>
    string SourceKind { get; }

    /// <summary>
    /// File extensions this extractor handles (lowercase, with a leading dot).
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Extracts the full content of the file at <paramref name="filePath"/>.
    /// </summary>
    Task<ExtractedDocument> ExtractAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
