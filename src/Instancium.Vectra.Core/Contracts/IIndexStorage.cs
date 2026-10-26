namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

public interface IIndexStorage : IAsyncDisposable
{
    Task<IndexManifest> ReadManifestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileState>> ReadFileStatesAsync(CancellationToken cancellationToken = default);

    Task<ReadOnlyMemory<float>> ReadVectorAsync(long vectorId, CancellationToken cancellationToken = default);

    Task<string> ReadChunkTextAsync(long chunkId, CancellationToken cancellationToken = default);
}
