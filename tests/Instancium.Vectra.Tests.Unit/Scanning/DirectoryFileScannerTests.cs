namespace Instancium.Vectra.Tests.Unit.Scanning;

using System.Security.Cryptography;
using System.Text;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Indexing.Scanning;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DirectoryFileScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "vectra-stage4", Guid.NewGuid().ToString("N"));
    private readonly DirectoryFileScanner _scanner = new();

    public DirectoryFileScannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldFindAllPdfFilesRecursively()
    {
        CreateFile("a.pdf", "alpha");
        CreateFile(Path.Combine("sub", "c.pdf"), "charlie");
        CreateFile(Path.Combine("sub", "deep", "d.pdf"), "delta");
        CreateFile("b.txt", "bravo");

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(_root, new[] { ".pdf" });

        Assert.Equal(3, files.Count);
        Assert.Equal(
            new[] { "a.pdf", "sub/c.pdf", "sub/deep/d.pdf" },
            files.Select(file => file.RelativePath));
        Assert.All(files, file => Assert.True(File.Exists(file.FullPath)));
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldSkipUnsupportedExtensions()
    {
        CreateFile("a.pdf", "alpha");
        CreateFile("notes.md", "markdown");
        CreateFile("data.txt", "text");

        IReadOnlyList<ScannedFile> pdfOnly = await _scanner.ScanAsync(_root, new[] { ".pdf" });
        Assert.Equal(new[] { "a.pdf" }, pdfOnly.Select(file => file.RelativePath));

        IReadOnlyList<ScannedFile> pdfAndMarkdown = await _scanner.ScanAsync(_root, new[] { ".pdf", ".md" });
        Assert.Equal(new[] { "a.pdf", "notes.md" }, pdfAndMarkdown.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldComputeStreamedLowercaseSha256()
    {
        byte[] content = Encoding.UTF8.GetBytes("hash me");
        CreateFile("doc.pdf", content);

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(_root, new[] { ".pdf" });

        ScannedFile file = Assert.Single(files);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), file.Sha256);
        Assert.Equal(content.Length, file.SizeBytes);
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldReturnDeterministicOrder()
    {
        CreateFile("z.pdf", "zulu");
        CreateFile(Path.Combine("m", "k.pdf"), "kilo");
        CreateFile("a.pdf", "alpha");

        IReadOnlyList<ScannedFile> first = await _scanner.ScanAsync(_root, new[] { ".pdf" });
        IReadOnlyList<ScannedFile> second = await _scanner.ScanAsync(_root, new[] { ".pdf" });

        Assert.Equal(
            first.Select(file => (file.RelativePath, file.Sha256)),
            second.Select(file => (file.RelativePath, file.Sha256)));
        Assert.Equal(new[] { "a.pdf", "m/k.pdf", "z.pdf" }, first.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldScanSingleFile()
    {
        string fullPath = CreateFile("solo.pdf", "solo");

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(fullPath, new[] { ".pdf" });

        ScannedFile file = Assert.Single(files);
        Assert.Equal("solo.pdf", file.RelativePath);
        Assert.Equal(fullPath, file.FullPath);
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldReturnEmpty_WhenSingleFileExtensionUnsupported()
    {
        string fullPath = CreateFile("solo.txt", "solo");

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(fullPath, new[] { ".pdf" });

        Assert.Empty(files);
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldSkipHiddenAndInstanciumFolders()
    {
        CreateFile(Path.Combine(".instancium", "vectra", "index.pdf"), "derived");
        CreateFile(Path.Combine(".hidden", "secret.pdf"), "hidden");
        CreateFile(".hiddenfile.pdf", "hidden-file");
        CreateFile("visible.pdf", "visible");

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(_root, new[] { ".pdf" });

        Assert.Equal(new[] { "visible.pdf" }, files.Select(file => file.RelativePath));
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldThrowWhenRootDoesNotExist()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => _scanner.ScanAsync(Path.Combine(_root, "missing"), new[] { ".pdf" }));
    }

    [Fact]
    public async Task DirectoryFileScanner_ShouldReturnEmpty_WhenNoExtensionsProvided()
    {
        CreateFile("a.pdf", "alpha");

        IReadOnlyList<ScannedFile> files = await _scanner.ScanAsync(_root, Array.Empty<string>());

        Assert.Empty(files);
    }

    private string CreateFile(string relativePath, string content) =>
        CreateFile(relativePath, Encoding.UTF8.GetBytes(content));

    private string CreateFile(string relativePath, byte[] content)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }
}
