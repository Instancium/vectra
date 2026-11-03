#nullable enable
namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Search;
using Instancium.Vectra.Storage;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Session;
using Xunit;

[Trait("Category", "Unit")]
public sealed class VectraIndexSessionTests : IDisposable
{
    private const int Dimension = 4;
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 29, 12, 30, 0, TimeSpan.Zero);
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "vectra-session-v2", Guid.NewGuid().ToString("N")));
    private readonly InstanciumDirectoryResolver _resolver = new();
    private readonly DiskIndexWriter _writer = new();

    public VectraIndexSessionTests()
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
    public async Task VectraIndexSession_ShouldSearchImmediatelyAfterOpen()
    {
        string indexDirectory = PrepareIndexDirectory();
        await _writer.WriteAsync(CreateSampleRequest(), indexDirectory);

        var session = new VectraIndexSession(new DiskIndexReader(), new InMemoryVectorStore());
        await session.OpenAsync(indexDirectory);

        var searchEngine = new InMemoryVectorSearchEngine(session.VectorStore);
        IReadOnlyList<VectorSearchHit> results = await searchEngine.SearchAsync(
            new float[] { 0.0f, 1.0f, 0.0f, 0.0f }, topK: 3);

        Assert.NotEmpty(results);
        VectorSearchHit top1 = results[0];
        Assert.Equal(2, top1.VectorId);
        Assert.InRange(top1.VectorScore, 0.999f, 1.001f);
        Assert.Equal(top1.VectorScore, top1.FinalScore);
    }

    private string PrepareIndexDirectory()
    {
        string indexDirectory = _resolver.ResolveIndexDirectory(_root, Path.Combine(_root, "idx"));
        _resolver.EnsureDirectoryStructure(indexDirectory);
        return indexDirectory;
    }

    private static IndexWriteRequest CreateSampleRequest()
    {
        var manifest = new DiskIndexManifest(
            DiskIndexManifest.CurrentVersion, Dimension, "stub-model", new[] { "pdf" },
            CreatedAt, UpdatedAt, fileCount: 2, chunkCount: 3);
        ChunkRecord[] chunks =
        {
            new(1, 1, 1, "chunk text 1", new float[] { 1.0f, 0.0f, 0.0f, 0.0f }),
            new(2, 1, 2, "chunk text 2", new float[] { 0.0f, 1.0f, 0.0f, 0.0f }),
            new(3, 2, 1, "chunk text 3", new float[] { 0.0f, 0.0f, 1.0f, 0.0f })
        };
        return new IndexWriteRequest(manifest, chunks);
    }
}

