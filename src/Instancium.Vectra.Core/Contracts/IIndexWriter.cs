namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Writes a complete, self-contained vector index to disk.
/// Implementations must guarantee that readers never observe a partially written index.
/// </summary>
public interface IIndexWriter
{
    /// <summary>
    /// Serializes <paramref name="request"/> into the six index artifacts inside
    /// <paramref name="indexDirectory"/>: <c>manifest.json</c>, <c>file.state.json</c>,
    /// <c>chunk.map.json</c>, <c>vectors.bin</c>, <c>offsets.bin</c> and <c>chunks.bin</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="indexDirectory"/> is null or whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Thrown when <paramref name="indexDirectory"/> does not exist.
    /// Call <see cref="IInstanciumDirectoryResolver.EnsureDirectoryStructure"/> first.
    /// </exception>
    Task WriteAsync(IndexWriteRequest request, string indexDirectory, CancellationToken cancellationToken = default);
}
