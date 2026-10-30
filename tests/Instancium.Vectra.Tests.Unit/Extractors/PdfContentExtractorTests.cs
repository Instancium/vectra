namespace Instancium.Vectra.Tests.Unit.Extractors;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Unit")]
public sealed class PdfContentExtractorTests
{
    private static readonly string SamplePdfPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "pdf", "sample-2pages.pdf");

    private readonly PdfContentExtractor _extractor = new();

    [Fact]
    public async Task PdfContentExtractor_ShouldSetSourceKindToPdf()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        Assert.Equal("pdf", _extractor.SourceKind);
        Assert.Equal(new[] { ".pdf" }, _extractor.SupportedExtensions);

        ExtractedDocument document = await _extractor.ExtractAsync(SamplePdfPath);

        Assert.Equal("pdf", document.SourceKind);
        Assert.Equal(Path.GetFullPath(SamplePdfPath), document.SourcePath);
        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(new[] { 1, 2 }, document.Pages.Select(page => page.PageNumber));
        Assert.Contains("alpha", document.Pages[0].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta", document.Pages[1].Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PdfContentExtractor_ShouldExtractTextFromPageRange()
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
    public async Task PdfContentExtractor_ShouldClampRangeToExistingPages()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        var request = new TextExtractionRequest(SamplePdfPath, 1, 10);
        var result = await _extractor.ExtractAsync(request);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task PdfContentExtractor_ShouldReturnEmptyWhenPageFromIsBeyondDocument()
    {
        Assert.True(File.Exists(SamplePdfPath), $"Missing test asset: {SamplePdfPath}");

        var request = new TextExtractionRequest(SamplePdfPath, 10, 20);
        var result = await _extractor.ExtractAsync(request);

        Assert.Empty(result);
    }

    [Fact]
    public async Task PdfContentExtractor_ShouldThrowWhenFileNotFound()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _extractor.ExtractAsync(new TextExtractionRequest(missingPath, 1, 1)));

        await Assert.ThrowsAsync<FileNotFoundException>(() => _extractor.ExtractAsync(missingPath));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    public void PdfContentExtractor_ShouldThrowWhenRangeIsInvalid(int pageFrom, int pageTo)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TextExtractionRequest("file.pdf", pageFrom, pageTo));
    }
}
