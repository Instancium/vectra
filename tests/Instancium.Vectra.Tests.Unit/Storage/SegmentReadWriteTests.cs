#nullable enable
namespace Instancium.Vectra.Tests.Unit.Storage;

using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;
using Instancium.Vectra.Storage.Disk;
using Instancium.Vectra.Storage.Segments;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading.Tasks;
using Xunit;

[Trait("Category", "Unit")]
public sealed class SegmentReadWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vectra-seg-tests", Guid.NewGuid().ToString("N"));
    private readonly SegmentWriter _writer = new();
    private readonly SegmentReader _reader = new();

    public SegmentReadWriteTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAndReadSegment_RoundTrip_PreservesVectorsMapAndPayloads()
    {
        const int dimension = 3;
        const long segmentId = 1L;

        var entries = new[]
        {
            new SegmentEntry(100L, new float[] { 1f, 2f, 3f }, "First segment document"),
            new SegmentEntry(200L, new float[] { 0.1f, 0.2f, 0.3f }, "Second payload with UTF-8: Тестовый текст"),
            new SegmentEntry(300L, new float[] { -1f, 0f, 1f }, string.Empty)
        };

        SegmentManifestEntry manifest = await _writer.WriteSegmentAsync(_dir, segmentId, dimension, entries);

        Assert.Equal(segmentId, manifest.SegmentId);
        Assert.Equal(entries.Length, manifest.VectorCount);
        Assert.False(string.IsNullOrWhiteSpace(manifest.VectorsSha256));
        Assert.False(string.IsNullOrWhiteSpace(manifest.MapSha256));
        Assert.False(string.IsNullOrWhiteSpace(manifest.PayloadSha256));

        await _reader.VerifyChecksumsAsync(_dir, manifest);

        float[] vectors = await _reader.ReadVectorsAsync(_dir, segmentId, dimension, entries.Length);
        Assert.Equal(entries.Length * dimension, vectors.Length);
        Assert.Equal(1f, vectors[0]);
        Assert.Equal(3f, vectors[2]);
        Assert.Equal(-1f, vectors[6]);

        var map = await _reader.ReadMapAsync(_dir, segmentId, entries.Length);
        Assert.Equal(entries.Length, map.Count);
        Assert.Equal(100L, map[0].VectorId);
        Assert.Equal(200L, map[1].VectorId);
        Assert.Equal(300L, map[2].VectorId);

        string p1 = await _reader.ReadPayloadAsync(_dir, segmentId, map[0].PayloadOffset, map[0].PayloadLength);
        string p2 = await _reader.ReadPayloadAsync(_dir, segmentId, map[1].PayloadOffset, map[1].PayloadLength);
        string p3 = await _reader.ReadPayloadAsync(_dir, segmentId, map[2].PayloadOffset, map[2].PayloadLength);

        Assert.Equal("First segment document", p1);
        Assert.Equal("Second payload with UTF-8: Тестовый текст", p2);
        Assert.Equal(string.Empty, p3);
    }

    [Fact]
    public async Task VerifyChecksumsAsync_ShouldThrowIndexCorruptedException_WhenFileIsMutated()
    {
        const int dimension = 2;
        const long segmentId = 2L;

        var entries = new[]
        {
            new SegmentEntry(1L, new float[] { 0.5f, 0.5f }, "Integrity test document")
        };

        SegmentManifestEntry manifest = await _writer.WriteSegmentAsync(_dir, segmentId, dimension, entries);

        string vecPath = Path.Combine(_dir, SegmentFileNames.GetVectorsFileName(segmentId));
        await using (var fs = new FileStream(vecPath, FileMode.Open, FileAccess.ReadWrite))
        {
            fs.Seek(0, SeekOrigin.Begin);
            fs.WriteByte(0xFF);
        }

        await Assert.ThrowsAsync<IndexCorruptedException>(async () =>
            await _reader.VerifyChecksumsAsync(_dir, manifest));
    }

    [Fact]
    public async Task OpenVectorsMemoryMapped_ShouldEnableDirectMemoryMapping()
    {
        const int dimension = 2;
        const long segmentId = 3L;

        var entries = new[]
        {
            new SegmentEntry(42L, new float[] { 10.5f, 20.5f }, "MMF document")
        };

        await _writer.WriteSegmentAsync(_dir, segmentId, dimension, entries);

        using MemoryMappedFile mmf = _reader.OpenVectorsMemoryMapped(_dir, segmentId);
        using MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        float val1 = accessor.ReadSingle(0);
        float val2 = accessor.ReadSingle(sizeof(float));

        Assert.Equal(10.5f, val1);
        Assert.Equal(20.5f, val2);
    }

    [Fact]
    public void VectraManifest_ShouldSerializeAndDeserializeCorrectly()
    {
        var seg1 = new SegmentManifestEntry(1L, 100, "abc1", "map1", "pay1");
        var seg2 = new SegmentManifestEntry(2L, 250, "abc2", "map2", "pay2");

        var manifest = new VectraManifest(
            VectraManifest.CurrentFormatVersion,
            128,
            SimilarityMetric.Cosine,
            "qwen-embedding",
            "fp-1234",
            QuantizationMode.None,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new[] { seg1, seg2 },
            350L);

        byte[] json = DiskIndexJson.SerializeToUtf8Bytes(manifest);
        VectraManifest parsed = DiskIndexJson.DeserializeFromUtf8Bytes<VectraManifest>(json);

        Assert.Equal(2, parsed.FormatVersion);
        Assert.Equal(128, parsed.Dimension);
        Assert.Equal(SimilarityMetric.Cosine, parsed.Metric);
        Assert.Equal("qwen-embedding", parsed.ModelId);
        Assert.Equal("fp-1234", parsed.ModelFingerprint);
        Assert.Equal(350L, parsed.TotalVectorCount);
        Assert.Equal(2, parsed.Segments.Count);
        Assert.Equal(100, parsed.Segments[0].VectorCount);
        Assert.Equal("abc2", parsed.Segments[1].VectorsSha256);
    }
}
