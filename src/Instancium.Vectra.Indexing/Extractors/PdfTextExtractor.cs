namespace Instancium.Vectra.Indexing.Extraction;

using System.Text;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;

public sealed class PdfTextExtractor : ITextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<PdfTextExtractor>.Instance;
    }

    public Task<IReadOnlyList<PageText>> ExtractAsync(
        TextExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => ExtractCore(request, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<PageText> ExtractCore(
        TextExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(request.FilePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("PDF file not found.", fullPath);
        }

        var result = new List<PageText>();
        var emptyPages = 0;
        var failedPages = 0;

        try
        {
            using var document = PdfDocument.Open(fullPath);

            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.Number < request.PageFrom)
                {
                    continue;
                }

                if (page.Number > request.PageTo)
                {
                    break;
                }

                try
                {
                    var text = NormalizeWhitespace(page.Text);

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        emptyPages++;
                        continue;
                    }

                    result.Add(new PageText(page.Number, text));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedPages++;

                    _logger.LogWarning(
                        "PDF page extraction failed. Path={Path}, PageNumber={PageNumber}, Error={Error}",
                        fullPath,
                        page.Number,
                        ex.Message);
                }
            }

            _logger.LogInformation(
                "PDF text extraction completed. Path={Path}, PageFrom={PageFrom}, PageTo={PageTo}, ExtractedPages={ExtractedPages}, EmptyPages={EmptyPages}, FailedPages={FailedPages}",
                fullPath,
                request.PageFrom,
                request.PageTo,
                result.Count,
                emptyPages,
                failedPages);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF text extraction failed. Path={Path}", fullPath);

            throw new VectraException(
                $"PDF text extraction failed for file '{fullPath}'.",
                ex);
        }
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
