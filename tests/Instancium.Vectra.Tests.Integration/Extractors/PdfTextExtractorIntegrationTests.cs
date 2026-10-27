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
        Assert.Contains("Lessons", pageOne.Text, StringComparison.OrdinalIgnoreCase);
    }
}
