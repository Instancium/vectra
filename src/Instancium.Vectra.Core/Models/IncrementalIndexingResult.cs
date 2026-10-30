namespace Instancium.Vectra.Core.Models;

public sealed record IncrementalIndexingResult(
    ChangeSet Changes,
    DiskIndexManifest Manifest,
    int ChunksAdded,
    int ChunksDeactivated,
    bool IndexWritten);
