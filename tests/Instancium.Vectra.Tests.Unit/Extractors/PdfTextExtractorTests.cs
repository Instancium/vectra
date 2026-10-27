namespace Instancium.Vectra.Tests.Unit.Extractors;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Unit")]
public sealed class PdfTextExtractorTests
{
    private static readonly string SamplePdfPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "pdf", "sample-2pages.pdf");

    private readonly PdfTextExtractor _extractor = new();

    [Fact]
    public async Task PdfTextExtractor_ShouldExtractTextFromPageRange()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        var request = new TextExtractionRequest(SamplePdfPath, 1, 2);

        var result = await _extractor.ExtractAsync(request);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].PageNumber);
        Assert.Equal(2, result[1].PageNumber);
        Assert.Contains("alpha", result[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", result[1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PdfTextExtractor_ShouldClampRangeToExistingPages()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        var request = new TextExtractionRequest(SamplePdfPath, 1, 10);

        var result = await _extractor.ExtractAsync(request);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task PdfTextExtractor_ShouldReturnEmptyWhenPageFromIsBeyondDocument()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        var request = new TextExtractionRequest(SamplePdfPath, 10, 20);

        var result = await _extractor.ExtractAsync(request);

        Assert.Empty(result);
    }

    [Fact]
    public async Task PdfTextExtractor_ShouldThrowWhenFileNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");

        var request = new TextExtractionRequest(missingPath, 1, 1);

        await Assert.ThrowsAsync<FileNotFoundException>(() => _extractor.ExtractAsync(request));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void PdfTextExtractor_ShouldThrowWhenRangeIsInvalid(int pageFrom, int pageTo)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TextExtractionRequest("file.pdf", pageFrom, pageTo));
    }
}
