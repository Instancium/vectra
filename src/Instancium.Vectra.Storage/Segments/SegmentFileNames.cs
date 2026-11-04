#nullable enable
namespace Instancium.Vectra.Storage.Segments;

/// <summary>
/// Canonical naming convention for segmented physical storage artifacts.
/// Fixed 6-digit zero-padding ensures lexicographical directory alignment up to 1,000,000 segments.
/// </summary>
public static class SegmentFileNames
{
    public static string GetVectorsFileName(long segmentId) => $"seg-{segmentId:D6}.vec";
    public static string GetMapFileName(long segmentId) => $"seg-{segmentId:D6}.map";
    public static string GetPayloadFileName(long segmentId) => $"seg-{segmentId:D6}.pay";
    public static string GetQuantizedFileName(long segmentId) => $"seg-{segmentId:D6}.vec.q";
}
