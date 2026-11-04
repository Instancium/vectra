#nullable enable
namespace Instancium.Vectra.Storage.Segments;

/// <summary>
/// In-memory projection of a 20-byte record in seg-NNNNNN.map.
/// 64-bit <see cref="PayloadOffset"/> eliminates the legacy 2 GiB text storage barrier.
/// </summary>
public readonly record struct SegmentMapEntry(long VectorId, long PayloadOffset, int PayloadLength);
