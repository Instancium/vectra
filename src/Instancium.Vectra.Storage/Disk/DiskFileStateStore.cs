namespace Instancium.Vectra.Storage.Disk;

using System.Text.Json;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Models;

/// <summary>
/// Reads and writes file.state.json. The on-disk shape is byte-compatible
/// with DiskIndexWriter/DiskIndexReader (SortedDictionary keyed by file id).
/// </summary>
public sealed class DiskFileStateStore : IFileStateStore
{
    public async Task<IReadOnlyList<FileState>> LoadAsync(
        string indexDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);

        string path = ResolveStatePath(indexDirectory);
        if (!File.Exists(path))
        {
            return Array.Empty<FileState>();
        }

        byte[] utf8 = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

        SortedDictionary<long, FileStateDto> dtos;
        try
        {
            dtos = DiskIndexJson.DeserializeFromUtf8Bytes<SortedDictionary<long, FileStateDto>>(utf8);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new IndexCorruptedException(
                $"'{IndexArtifactNames.FileState}' in '{Path.GetFullPath(indexDirectory)}' is malformed.",
                ex);
        }

        return dtos
            .Select(pair => new FileState(
                pair.Key,
                pair.Value.Path,
                pair.Value.Sha256,
                pair.Value.SizeBytes,
                pair.Value.Format,
                pair.Value.ChunkIds,
                pair.Value.IndexedAt,
                pair.Value.Status))
            .ToArray();
    }

    public async Task SaveAsync(
        string indexDirectory,
        IReadOnlyList<FileState> states,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectory);
        ArgumentNullException.ThrowIfNull(states);

        SortedDictionary<long, FileStateDto> dto = BuildDto(states);

        string fullDirectory = Path.GetFullPath(indexDirectory);
        Directory.CreateDirectory(fullDirectory);

        await AtomicFileWriter.WriteAllBytesAtomicAsync(
            Path.Combine(fullDirectory, IndexArtifactNames.FileState),
            DiskIndexJson.SerializeToUtf8Bytes(dto),
            cancellationToken).ConfigureAwait(false);
    }

    private static SortedDictionary<long, FileStateDto> BuildDto(IReadOnlyList<FileState> states)
    {
        var dto = new SortedDictionary<long, FileStateDto>();
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (FileState state in states)
        {
            ArgumentNullException.ThrowIfNull(state);

            if (!dto.TryAdd(state.FileId, new FileStateDto(
                state.Path,
                state.Sha256,
                state.SizeBytes,
                state.Format,
                state.ChunkIds,
                state.IndexedAt,
                state.Status)))
            {
                throw new ArgumentException($"Duplicate file id {state.FileId}.", nameof(states));
            }

            if (!paths.Add(state.Path))
            {
                throw new ArgumentException($"Duplicate path '{state.Path}'.", nameof(states));
            }
        }

        return dto;
    }

    private static string ResolveStatePath(string indexDirectory) =>
        Path.Combine(Path.GetFullPath(indexDirectory), IndexArtifactNames.FileState);
}
