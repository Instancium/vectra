namespace Instancium.Vectra.Storage.Disk;

/// <summary>
/// On-disk artifact names of the v1 index format (file_state.json removed in P3).
/// </summary>
public static class IndexArtifactNames
{
    public const string Manifest = "manifest.json";
    public const string ChunkMap = "chunk.map.json";
    public const string Vectors = "vectors.bin";
    public const string Offsets = "offsets.bin";
    public const string Chunks = "chunks.bin";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Manifest,
        ChunkMap,
        Vectors,
        Offsets,
        Chunks
    };
}

