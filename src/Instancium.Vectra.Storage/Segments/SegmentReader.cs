#nullable enable
namespace Instancium.Vectra.Storage.Segments;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Provides high-throughput read operations and cryptographic verification over v2 segments.
/// Implements zero-copy memory-mapping for SIMD search and lazy 64-bit random access for text hydration.
/// </summary>
public sealed class SegmentReader
{
    public async Task VerifyChecksumsAsync(
        string directory,
        SegmentManifestEntry manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(manifest);

        string vecPath = Path.Combine(directory, SegmentFileNames.GetVectorsFileName(manifest.SegmentId));
        string mapPath = Path.Combine(directory, SegmentFileNames.GetMapFileName(manifest.SegmentId));
        string payPath = Path.Combine(directory, SegmentFileNames.GetPayloadFileName(manifest.SegmentId));

        await VerifyFileHashAsync(vecPath, manifest.VectorsSha256, "vectors", cancellationToken).ConfigureAwait(false);
        await VerifyFileHashAsync(mapPath, manifest.MapSha256, "map", cancellationToken).ConfigureAwait(false);
        await VerifyFileHashAsync(payPath, manifest.PayloadSha256, "payload", cancellationToken).ConfigureAwait(false);
    }

    public MemoryMappedFile OpenVectorsMemoryMapped(string directory, long segmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);

        string vecPath = Path.Combine(directory, SegmentFileNames.GetVectorsFileName(segmentId));
        if (!File.Exists(vecPath))
        {
            throw new IndexCorruptedException($"Vectors artifact '{vecPath}' is missing.");
        }

        return MemoryMappedFile.CreateFromFile(
            vecPath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);
    }

    public async Task<float[]> ReadVectorsAsync(
        string directory,
        long segmentId,
        int dimension,
        int vectorCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(vectorCount);

        if (vectorCount == 0)
        {
            return Array.Empty<float>();
        }

        string vecPath = Path.Combine(directory, SegmentFileNames.GetVectorsFileName(segmentId));
        using var handle = File.OpenHandle(vecPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);

        long expectedBytes = (long)vectorCount * dimension * sizeof(float);
        long actualLength = RandomAccess.GetLength(handle);
        if (actualLength != expectedBytes)
        {
            throw new IndexCorruptedException(
                $"Segment vectors artifact '{vecPath}' length {actualLength} bytes does not match expected {expectedBytes} bytes (vectors={vectorCount}, dim={dimension}).");
        }

        byte[] byteBuffer = new byte[expectedBytes];
        int read = await RandomAccess.ReadAsync(handle, byteBuffer.AsMemory(), 0, cancellationToken).ConfigureAwait(false);
        if (read != expectedBytes)
        {
            throw new IndexCorruptedException($"Unexpected end of file while reading '{vecPath}'. Read {read} of {expectedBytes} bytes.");
        }

        var result = new float[vectorCount * dimension];
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.Cast<byte, float>(byteBuffer).CopyTo(result);
        }
        else
        {
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = BinaryPrimitives.ReadSingleLittleEndian(byteBuffer.AsSpan(i * sizeof(float), sizeof(float)));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<SegmentMapEntry>> ReadMapAsync(
        string directory,
        long segmentId,
        int vectorCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);
        ArgumentOutOfRangeException.ThrowIfNegative(vectorCount);

        if (vectorCount == 0)
        {
            return Array.Empty<SegmentMapEntry>();
        }

        string mapPath = Path.Combine(directory, SegmentFileNames.GetMapFileName(segmentId));
        using var handle = File.OpenHandle(mapPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);

        long expectedBytes = (long)vectorCount * SegmentWriter.MapRecordSize;
        long actualLength = RandomAccess.GetLength(handle);
        if (actualLength != expectedBytes)
        {
            throw new IndexCorruptedException(
                $"Segment map artifact '{mapPath}' length {actualLength} bytes does not match expected {expectedBytes} bytes (vectors={vectorCount}).");
        }

        byte[] buffer = new byte[expectedBytes];
        int read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(), 0, cancellationToken).ConfigureAwait(false);
        if (read != expectedBytes)
        {
            throw new IndexCorruptedException($"Unexpected end of file while reading '{mapPath}'. Read {read} of {expectedBytes} bytes.");
        }

        var entries = new SegmentMapEntry[vectorCount];
        for (int i = 0; i < vectorCount; i++)
        {
            int offset = i * SegmentWriter.MapRecordSize;
            long vectorId = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset, sizeof(long)));
            long payloadOffset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + sizeof(long), sizeof(long)));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset + sizeof(long) * 2, sizeof(int)));
            entries[i] = new SegmentMapEntry(vectorId, payloadOffset, payloadLength);
        }

        return entries;
    }

    public async ValueTask<string> ReadPayloadAsync(
        string directory,
        long segmentId,
        long offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (length == 0)
        {
            return string.Empty;
        }

        string payPath = Path.Combine(directory, SegmentFileNames.GetPayloadFileName(segmentId));
        using var handle = File.OpenHandle(payPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);

        long fileLength = RandomAccess.GetLength(handle);
        if (offset + length > fileLength)
        {
            throw new IndexCorruptedException(
                $"Payload byte range [{offset}, {offset + length}) exceeds '{payPath}' size {fileLength}.");
        }

        byte[] buffer = new byte[length];
        int read = await RandomAccess.ReadAsync(handle, buffer.AsMemory(), offset, cancellationToken).ConfigureAwait(false);
        if (read != length)
        {
            throw new IndexCorruptedException($"Unexpected end of file while reading '{payPath}'. Read {read} of {length} bytes.");
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static async Task VerifyFileHashAsync(string filePath, string expectedHash, string artifactType, CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            throw new IndexCorruptedException($"Segment artifact '{artifactType}' missing at '{filePath}'.");
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] actualHashBytes = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        string actualHash = Convert.ToHexString(actualHashBytes).ToLowerInvariant();

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new IndexCorruptedException(
                $"Segment artifact '{artifactType}' at '{filePath}' is corrupted. Expected SHA-256 '{expectedHash}', but got '{actualHash}'.");
        }
    }
}
