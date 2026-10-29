namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Lazily reads chunk texts from the on-disk index (TZ-2 stage 3).
/// Implementations must read only the requested byte ranges, never the whole chunk store.
/// </summary>
public interface IChunkTextReader
{
    /// <summary>Reads a single chunk text by its byte range inside <c>chunks.bin</c>.</summary>
    /// <exception cref="Instancium.Vectra.Core.Exceptions.IndexCorruptedException">
    /// Thrown when the range exceeds the chunk store bounds.
    /// </exception>
    ValueTask<string> ReadAsync(ChunkTextRange range, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads multiple chunk texts in a single call. Results are returned in the same order
    /// as the input ranges. Intended for hydrating only the final Top-K results.
    /// </summary>
    Task<IReadOnlyList<string>> ReadBatchAsync(
        IReadOnlyList<ChunkTextRange> ranges,
        CancellationToken cancellationToken = default);
}
