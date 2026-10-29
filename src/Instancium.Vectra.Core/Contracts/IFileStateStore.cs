namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Persists the incremental indexing state (file.state.json).
/// </summary>
public interface IFileStateStore
{
    /// <summary>
    /// Loads the state. Returns an empty list when file.state.json does not exist yet.
    /// </summary>
    /// <exception cref="Instancium.Vectra.Core.Exceptions.IndexCorruptedException">
    /// The state file exists but cannot be parsed.
    /// </exception>
    Task<IReadOnlyList<FileState>> LoadAsync(
        string indexDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically overwrites file.state.json (temporary file + rename).
    /// </summary>
    Task SaveAsync(
        string indexDirectory,
        IReadOnlyList<FileState> states,
        CancellationToken cancellationToken = default);
}
