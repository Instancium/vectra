namespace Instancium.Vectra.Storage.Disk;

/// <summary>
/// Stable names of the six on-disk index artifacts (TZ-2 section 3.2).
/// Writer, reader and doctor must all agree on these names.
/// </summary>
public static class IndexArtifactNames
{
    public const string Manifest = "manifest.json";
    public const string FileState = "file.state.json";
    public const string ChunkMap = "chunk.map.json";
    public const string Vectors = "vectors.bin";
    public const string Offsets = "offsets.bin";
    public const string Chunks = "chunks.bin";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Manifest,
        FileState,
        ChunkMap,
        Vectors,
        Offsets,
        Chunks
    };
}
