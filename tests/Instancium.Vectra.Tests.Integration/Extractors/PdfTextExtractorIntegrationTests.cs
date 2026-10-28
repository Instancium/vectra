namespace Instancium.Vectra.Tests.Integration.Extractors;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Integration")]
public sealed class PdfTextExtractorIntegrationTests
{
    [Fact]
    public async Task PdfTextExtractor_ShouldExtractFirstTwentyPagesFromRealCorpus()
    {
        var corpusPath = Environment.GetEnvironmentVariable("VECTRA_TEST_CORPUS_PDF");
        if (string.IsNullOrWhiteSpace(corpusPath))
        {
            // Allow graceful skip if the environment variable is not set (e.g. local dev without corpus)
            return;
        }

        Assert.True(File.Exists(corpusPath), $"Corpus PDF not found: {corpusPath}");

        var extractor = new PdfTextExtractor();
        var request = new TextExtractionRequest(corpusPath, 1, 20);
        var result = await extractor.ExtractAsync(request);

        Assert.True(
            result.Count >= 15,
            $"Expected at least 15 non-empty pages, but got {result.Count}.");

        Assert.All(result, page => Assert.InRange(page.PageNumber, 1, 20));
        Assert.All(result, page => Assert.False(string.IsNullOrWhiteSpace(page.Text)));

        var pageOne = result.FirstOrDefault(page => page.PageNumber == 1);
        Assert.NotNull(pageOne);

        // The test corpus (test-doc.pdf) is a synthetic document used for deterministic benchmarking.
        // Its first page contains the section marker 'SEC-FRONT' and the handbook orientation header,
        // rather than the word 'Lessons' from the original industrial instrumentation book.
        Assert.Contains("SEC-FRONT", pageOne.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page 1 of 100", pageOne.Text, StringComparison.OrdinalIgnoreCase);
    }
}