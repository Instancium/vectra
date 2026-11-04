#nullable enable
namespace Instancium.Vectra.Storage.Segments;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Compiles an immutable storage segment (v2 format):
/// seg-NNNNNN.vec, seg-NNNNNN.map, and seg-NNNNNN.pay with 64-bit offsets.
/// Employs atomic write renames and computes SHA-256 digests in-flight without secondary disk reads.
/// </summary>
public sealed class SegmentWriter
{
    /// <summary>
    /// Binary record layout: VectorId (8 bytes) + PayloadOffset (8 bytes) + PayloadLength (4 bytes).
    /// </summary>
    public const int MapRecordSize = sizeof(long) + sizeof(long) + sizeof(int);

    public async Task<SegmentManifestEntry> WriteSegmentAsync(
        string directory,
        long segmentId,
        int dimension,
        IReadOnlyList<SegmentEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentId, 1L);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        ArgumentNullException.ThrowIfNull(entries);

        Directory.CreateDirectory(directory);

        string vecFile = Path.Combine(directory, SegmentFileNames.GetVectorsFileName(segmentId));
        string mapFile = Path.Combine(directory, SegmentFileNames.GetMapFileName(segmentId));
        string payFile = Path.Combine(directory, SegmentFileNames.GetPayloadFileName(segmentId));

        string vecTmp = vecFile + ".tmp";
        string mapTmp = mapFile + ".tmp";
        string payTmp = payFile + ".tmp";

        try
        {
            string vecHash;
            string mapHash;
            string payHash;

            // 1. Write Payload (.pay) and calculate 64-bit stream offsets
            var offsets = new (long VectorId, long Offset, int Length)[entries.Count];
            await using (var payStream = new FileStream(payTmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            using (var payHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long currentOffset = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SegmentEntry entry = entries[i];
                    byte[] payloadBytes = string.IsNullOrEmpty(entry.Payload)
                        ? Array.Empty<byte>()
                        : Encoding.UTF8.GetBytes(entry.Payload);

                    if (payloadBytes.Length > 0)
                    {
                        await payStream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);
                        payHasher.AppendData(payloadBytes);
                    }

                    offsets[i] = (entry.VectorId, currentOffset, payloadBytes.Length);
                    currentOffset += payloadBytes.Length;
                }

                await payStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                payHash = Convert.ToHexString(payHasher.GetHashAndReset()).ToLowerInvariant();
            }

            // 2. Write Map (.map): 20 bytes per record
            await using (var mapStream = new FileStream(mapTmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            using (var mapHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] mapBuffer = new byte[MapRecordSize];
                for (int i = 0; i < offsets.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (vId, pOffset, pLength) = offsets[i];
                    BinaryPrimitives.WriteInt64LittleEndian(mapBuffer.AsSpan(0, sizeof(long)), vId);
                    BinaryPrimitives.WriteInt64LittleEndian(mapBuffer.AsSpan(sizeof(long), sizeof(long)), pOffset);
                    BinaryPrimitives.WriteInt32LittleEndian(mapBuffer.AsSpan(sizeof(long) * 2, sizeof(int)), pLength);

                    await mapStream.WriteAsync(mapBuffer, cancellationToken).ConfigureAwait(false);
                    mapHasher.AppendData(mapBuffer);
                }

                await mapStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                mapHash = Convert.ToHexString(mapHasher.GetHashAndReset()).ToLowerInvariant();
            }

            // 3. Write Vectors (.vec): packed float32 IEEE little-endian
            await using (var vecStream = new FileStream(vecTmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            using (var vecHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int vectorByteSize = dimension * sizeof(float);
                byte[] vecBuffer = new byte[vectorByteSize];

                for (int i = 0; i < entries.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SegmentEntry entry = entries[i];
                    if (entry.Vector.Length != dimension)
                    {
                        throw new ArgumentException(
                            $"Entry {entry.VectorId} vector dimension {entry.Vector.Length} does not match expected {dimension}.",
                            nameof(entries));
                    }

                    WriteLittleEndian(entry.Vector.Span, vecBuffer);
                    await vecStream.WriteAsync(vecBuffer, cancellationToken).ConfigureAwait(false);
                    vecHasher.AppendData(vecBuffer);
                }

                await vecStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                vecHash = Convert.ToHexString(vecHasher.GetHashAndReset()).ToLowerInvariant();
            }

            // Atomic rename of completed artifacts
            File.Move(payTmp, payFile, overwrite: true);
            File.Move(mapTmp, mapFile, overwrite: true);
            File.Move(vecTmp, vecFile, overwrite: true);

            return new SegmentManifestEntry(
                segmentId,
                entries.Count,
                vectorsSha256: vecHash,
                mapSha256: mapHash,
                payloadSha256: payHash);
        }
        catch
        {
            TryDelete(payTmp);
            TryDelete(mapTmp);
            TryDelete(vecTmp);
            throw;
        }
    }

    private static void WriteLittleEndian(ReadOnlySpan<float> source, Span<byte> destination)
    {
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(source).CopyTo(destination);
            return;
        }

        for (int i = 0; i < source.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                destination.Slice(i * sizeof(float), sizeof(float)),
                source[i]);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
