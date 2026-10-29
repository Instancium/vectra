namespace Instancium.Vectra.Tests.Unit.Extraction;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Unit")]
public sealed class PdfPaginationGuardTests
{
    private static readonly string TestDocPath =
        Path.Combine(AppContext.BaseDirectory, "assets", "pdf", "test-doc.pdf");

    private static readonly Dictionary<string, (int From, int To)> SectionPages = new()
    {
        ["SEC-TC"] = (3, 8),
        ["SEC-RTD"] = (9, 14),
        ["SEC-PTX"] = (15, 20),
        ["SEC-DPTX"] = (21, 26),
        ["SEC-MAG"] = (27, 32),
        ["SEC-VOR"] = (33, 38),
        ["SEC-CV"] = (39, 44),
        ["SEC-SV"] = (45, 50),
        ["SEC-GWR"] = (51, 56),
        ["SEC-CAP"] = (57, 62),
        ["SEC-PH"] = (63, 68),
        ["SEC-ORP"] = (69, 74),
        ["SEC-PIEZO"] = (75, 80),
        ["SEC-EDDY"] = (81, 86),
        ["SEC-2W"] = (87, 92),
        ["SEC-4W"] = (93, 98),
    };

    [Fact]
    public async Task TestDoc_ShouldContain100Pages()
    {
        Assert.True(File.Exists(TestDocPath), $"Missing test asset: {TestDocPath}");
        var extractor = new PdfContentExtractor();
        var result = await extractor.ExtractAsync(new TextExtractionRequest(TestDocPath, 1, 100));
        Assert.True(result.Count >= 95, $"Expected ≥95 non-empty pages, got {result.Count}.");
    }

    [Fact]
    public async Task TestDoc_ShouldContainSectionMarkersOnExpectedPages()
    {
        Assert.True(File.Exists(TestDocPath), $"Missing test asset: {TestDocPath}");
        var extractor = new PdfContentExtractor();
        var pages = await extractor.ExtractAsync(new TextExtractionRequest(TestDocPath, 1, 100));

        foreach (var (section, (from, to)) in SectionPages)
        {
            var firstPage = pages.FirstOrDefault(p => p.PageNumber == from);
            Assert.NotNull(firstPage);
            Assert.Contains(section, firstPage.Text, StringComparison.OrdinalIgnoreCase);

            var lastPage = pages.FirstOrDefault(p => p.PageNumber == to);
            Assert.NotNull(lastPage);
            Assert.Contains(section, lastPage.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TestDoc_ShouldContainPageHeaders()
    {
        Assert.True(File.Exists(TestDocPath), $"Missing test asset: {TestDocPath}");
        var extractor = new PdfContentExtractor();
        var pages = await extractor.ExtractAsync(new TextExtractionRequest(TestDocPath, 1, 100));

        foreach (var page in pages)
        {
            Assert.Contains($"page {page.PageNumber} of 100", page.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}