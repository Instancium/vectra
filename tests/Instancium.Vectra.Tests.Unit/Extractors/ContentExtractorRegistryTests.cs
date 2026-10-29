namespace Instancium.Vectra.Tests.Unit.Extractors;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ContentExtractorRegistryTests
{
    [Fact]
    public void ContentExtractorRegistry_ShouldResolvePdf()
    {
        var registry = new ContentExtractorRegistry(new IContentExtractor[] { new PdfContentExtractor() });

        IContentExtractor extractor = registry.Resolve(".pdf");

        Assert.IsType<PdfContentExtractor>(extractor);
        Assert.Equal("pdf", extractor.SourceKind);
        Assert.Same(extractor, registry.Resolve("PDF"));
        Assert.Same(extractor, registry.Resolve(".PDF"));
    }

    [Fact]
    public void ContentExtractorRegistry_ShouldExposeSupportedExtensions()
    {
        var registry = new ContentExtractorRegistry(new IContentExtractor[]
        {
            new PdfContentExtractor(),
            new StubExtractor("markdown", ".md", ".markdown")
        });

        Assert.Equal(new[] { ".markdown", ".md", ".pdf" }, registry.SupportedExtensions);
    }

    [Fact]
    public void ContentExtractorRegistry_ShouldThrowUnsupportedFormat_WhenExtensionUnknown()
    {
        var registry = new ContentExtractorRegistry(new IContentExtractor[] { new PdfContentExtractor() });

        Assert.Throws<UnsupportedFormatException>(() => registry.Resolve(".docx"));
    }

    [Fact]
    public void ContentExtractorRegistry_ShouldThrow_WhenTwoExtractorsClaimSameExtension()
    {
        Assert.Throws<InvalidOperationException>(() => new ContentExtractorRegistry(new IContentExtractor[]
        {
            new StubExtractor("pdf-a", ".pdf"),
            new StubExtractor("pdf-b", ".pdf")
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    public void ContentExtractorRegistry_ShouldThrow_OnInvalidExtension(string extension)
    {
        var registry = new ContentExtractorRegistry(new IContentExtractor[] { new PdfContentExtractor() });

        Assert.ThrowsAny<ArgumentException>(() => registry.Resolve(extension));
    }

    private sealed class StubExtractor : IContentExtractor
    {
        public StubExtractor(string sourceKind, params string[] extensions)
        {
            SourceKind = sourceKind;
            SupportedExtensions = extensions;
        }

        public string SourceKind { get; }

        public IReadOnlyList<string> SupportedExtensions { get; }

        public Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
