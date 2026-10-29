namespace Instancium.Vectra.Tests.Unit.Incremental;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Extraction;
using Instancium.Vectra.Indexing.Incremental;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ChangeClassifierTests
{
    private static readonly DateTimeOffset IndexedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly ChangeClassifier _classifier =
        new(new StubRegistry(("pdf", ".pdf"), ("markdown", ".md")));

    [Fact]
    public void ChangeClassifier_ShouldDetectNewModifiedAndRemovedFiles()
    {
        IReadOnlyList<FileState> previous = new[]
        {
            State(1, "docs/a.pdf", "hash-a"),
            State(2, "docs/b.pdf", "hash-b"),
            State(3, "docs/c.pdf", "hash-c")
        };

        IReadOnlyList<ScannedFile> scanned = new[]
        {
            Scan("docs/a.pdf", "hash-a"),
            Scan("docs/b.pdf", "hash-b-changed"),
            Scan("docs/d.pdf", "hash-d")
        };

        ChangeSet changes = _classifier.Classify(scanned, previous);

        Assert.True(changes.HasChanges);
        Assert.Equal(new[] { "docs/d.pdf" }, changes.Added.Select(file => file.RelativePath));
        Assert.Equal(new[] { "docs/b.pdf" }, changes.Modified.Select(change => change.Current.RelativePath));
        Assert.Equal("hash-b", changes.Modified[0].Previous.Sha256);
        Assert.Equal(new[] { "docs/c.pdf" }, changes.Removed.Select(state => state.Path));
        Assert.Equal(new[] { "docs/a.pdf" }, changes.Unchanged.Select(state => state.Path));
    }

    [Fact]
    public void ChangeClassifier_ShouldDetectFormatChange_WhenHashIsUnchanged()
    {
        var classifier = new ChangeClassifier(new StubRegistry(("pdf-v2", ".pdf")));

        IReadOnlyList<FileState> previous = new[] { State(1, "docs/a.pdf", "hash-a") };
        IReadOnlyList<ScannedFile> scanned = new[] { Scan("docs/a.pdf", "hash-a") };

        ChangeSet changes = classifier.Classify(scanned, previous);

        Assert.Equal("docs/a.pdf", Assert.Single(changes.Modified).Current.RelativePath);
        Assert.Empty(changes.Unchanged);
    }

    [Fact]
    public void ChangeClassifier_ShouldRetryPreviouslyFailedFiles()
    {
        IReadOnlyList<FileState> previous = new[]
        {
            State(1, "docs/a.pdf", "hash-a", status: IndexFileStatus.Failed)
        };
        IReadOnlyList<ScannedFile> scanned = new[] { Scan("docs/a.pdf", "hash-a") };

        ChangeSet changes = _classifier.Classify(scanned, previous);

        Assert.Equal("docs/a.pdf", Assert.Single(changes.Modified).Current.RelativePath);
    }

    [Fact]
    public void ChangeClassifier_ShouldReportNoChanges()
    {
        IReadOnlyList<FileState> previous = new[] { State(1, "docs/a.pdf", "hash-a") };
        IReadOnlyList<ScannedFile> scanned = new[] { Scan("docs/a.pdf", "hash-a") };

        ChangeSet changes = _classifier.Classify(scanned, previous);

        Assert.False(changes.HasChanges);
        Assert.Empty(changes.Added);
        Assert.Empty(changes.Modified);
        Assert.Empty(changes.Removed);
        Assert.Single(changes.Unchanged);
    }

    [Fact]
    public void ChangeClassifier_ShouldThrow_OnDuplicateScannedPaths()
    {
        IReadOnlyList<ScannedFile> scanned = new[]
        {
            Scan("docs/a.pdf", "hash-1"),
            Scan("docs/a.pdf", "hash-2")
        };

        Assert.ThrowsAny<ArgumentException>(
            () => _classifier.Classify(scanned, Array.Empty<FileState>()));
    }

    [Fact]
    public void ChangeClassifier_ShouldThrow_OnDuplicatePreviousPaths()
    {
        IReadOnlyList<FileState> previous = new[]
        {
            State(1, "docs/a.pdf", "hash-a"),
            State(2, "docs/a.pdf", "hash-a2")
        };

        Assert.ThrowsAny<ArgumentException>(
            () => _classifier.Classify(Array.Empty<ScannedFile>(), previous));
    }

    [Fact]
    public void ChangeClassifier_ShouldThrow_OnUnsupportedExtension()
    {
        IReadOnlyList<ScannedFile> scanned = new[] { Scan("docs/a.docx", "hash-a") };

        Assert.Throws<UnsupportedFormatException>(
            () => _classifier.Classify(scanned, Array.Empty<FileState>()));
    }

    private static FileState State(
        long fileId,
        string path,
        string sha256,
        IndexFileStatus status = IndexFileStatus.Indexed) =>
        new(
            fileId,
            path,
            sha256,
            sizeBytes: 100,
            format: path[^3..], // ".pdf" -> "pdf"
            chunkIds: Array.Empty<long>(),
            IndexedAt,
            status);

    private static ScannedFile Scan(string relativePath, string sha256) =>
        new("/corpus/" + relativePath, relativePath, sha256, sizeBytes: 100);

    private sealed class StubRegistry : IContentExtractorRegistry
    {
        private readonly Dictionary<string, string> _kindByExtension;

        public StubRegistry(params (string Kind, string Extension)[] mappings)
        {
            _kindByExtension = mappings.ToDictionary(
                mapping => ContentExtractorRegistry.NormalizeExtension(mapping.Extension),
                mapping => mapping.Kind,
                StringComparer.Ordinal);
        }

        public IReadOnlyList<string> SupportedExtensions =>
            _kindByExtension.Keys.OrderBy(extension => extension, StringComparer.Ordinal).ToArray();

        public IContentExtractor Resolve(string fileExtension)
        {
            string normalized = ContentExtractorRegistry.NormalizeExtension(fileExtension);
            if (_kindByExtension.TryGetValue(normalized, out string? kind))
            {
                return new StubExtractor(kind, normalized);
            }

            throw new UnsupportedFormatException($"No extractor for '{normalized}'.");
        }
    }

    private sealed class StubExtractor : IContentExtractor
    {
        public StubExtractor(string sourceKind, string extension)
        {
            SourceKind = sourceKind;
            SupportedExtensions = new[] { extension };
        }

        public string SourceKind { get; }

        public IReadOnlyList<string> SupportedExtensions { get; }

        public Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
