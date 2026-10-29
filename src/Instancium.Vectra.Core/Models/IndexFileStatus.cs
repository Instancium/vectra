namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Processing status of a source file recorded in <c>file.state.json</c>.
/// </summary>
public enum IndexFileStatus
{
    /// <summary>File was extracted, chunked and embedded successfully.</summary>
    Indexed = 0,

    /// <summary>
    /// File could not be processed (e.g. corrupt content). It is kept in the state so that
    /// 'doctor' can report it and future indexing runs can retry it.
    /// </summary>
    Failed = 1,

    /// <summary>
    /// File was deleted from the content directory. Kept in state with inactive chunks (stage 5).
    /// </summary>
    Removed = 2
}
