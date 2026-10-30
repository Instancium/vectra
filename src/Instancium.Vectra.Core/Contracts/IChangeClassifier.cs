namespace Instancium.Vectra.Core.Contracts;

using Instancium.Vectra.Core.Models;

/// <summary>
/// Compares the current scan result with the previous indexing state.
/// </summary>
public interface IChangeClassifier
{
    /// <summary>
    /// Classifies files as added, modified, removed or unchanged.
    /// A file is modified when its SHA-256 changed, its expected format changed,
    /// or the previous indexing attempt failed.
    /// </summary>
    ChangeSet Classify(
        IReadOnlyList<ScannedFile> scannedFiles,
        IReadOnlyList<FileState> previousStates);
}
