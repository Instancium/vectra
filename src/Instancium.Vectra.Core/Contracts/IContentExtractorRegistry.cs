namespace Instancium.Vectra.Core.Contracts;

/// <summary>
/// Resolves the content extractor responsible for a file extension.
/// Adding a new format means registering one more IContentExtractor;
/// the index format itself does not change.
/// </summary>
public interface IContentExtractorRegistry
{
    /// <summary>
    /// All extensions supported by the registered extractors
    /// (lowercase, with a leading dot, ordinal-sorted).
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Resolves the extractor for the given extension.
    /// Accepts ".pdf", "pdf" and ".PDF" forms.
    /// </summary>
    /// <exception cref="Instancium.Vectra.Core.Exceptions.UnsupportedFormatException">
    /// No extractor is registered for the extension.
    /// </exception>
    IContentExtractor Resolve(string fileExtension);
}
