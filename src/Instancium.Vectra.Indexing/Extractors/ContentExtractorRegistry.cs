namespace Instancium.Vectra.Indexing.Extraction;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;

public sealed class ContentExtractorRegistry : IContentExtractorRegistry
{
    private readonly Dictionary<string, IContentExtractor> _extractorsByExtension;
    private readonly IReadOnlyList<string> _supportedExtensions;

    public ContentExtractorRegistry(IEnumerable<IContentExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);

        _extractorsByExtension = new Dictionary<string, IContentExtractor>(StringComparer.Ordinal);

        foreach (IContentExtractor extractor in extractors)
        {
            ArgumentNullException.ThrowIfNull(extractor);

            foreach (string rawExtension in extractor.SupportedExtensions)
            {
                string extension = NormalizeExtension(rawExtension);
                if (!_extractorsByExtension.TryAdd(extension, extractor))
                {
                    throw new InvalidOperationException(
                        $"Multiple content extractors are registered for extension '{extension}'.");
                }
            }
        }

        _supportedExtensions = _extractorsByExtension.Keys
            .OrderBy(extension => extension, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> SupportedExtensions => _supportedExtensions;

    public IContentExtractor Resolve(string fileExtension)
    {
        string extension = NormalizeExtension(fileExtension);

        if (_extractorsByExtension.TryGetValue(extension, out IContentExtractor? extractor))
        {
            return extractor;
        }

        throw new UnsupportedFormatException(
            $"No content extractor is registered for extension '{extension}'.");
    }

    /// <summary>
    /// Normalizes a file extension to lowercase form with a leading dot: "PDF" -> ".pdf".
    /// </summary>
    public static string NormalizeExtension(string fileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);

        string trimmed = fileExtension.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        if (trimmed.Length < 2 || trimmed.IndexOf('.', 1) >= 0)
        {
            throw new ArgumentException(
                $"'{fileExtension}' is not a valid file extension.",
                nameof(fileExtension));
        }

        return trimmed;
    }
}
