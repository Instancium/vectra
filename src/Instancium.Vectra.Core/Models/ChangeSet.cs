#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// Result of comparing a scan with the previous indexing state.
/// </summary>
public sealed record ChangeSet
{
    public ChangeSet(
        IReadOnlyList<ScannedFile> added,
        IReadOnlyList<ModifiedFile> modified,
        IReadOnlyList<FileState> removed,
        IReadOnlyList<FileState> unchanged)
    {
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(modified);
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(unchanged);

        Added = added.ToArray();
        Modified = modified.ToArray();
        Removed = removed.ToArray();
        Unchanged = unchanged.ToArray();
    }

    public IReadOnlyList<ScannedFile> Added { get; }

    public IReadOnlyList<ModifiedFile> Modified { get; }

    public IReadOnlyList<FileState> Removed { get; }

    public IReadOnlyList<FileState> Unchanged { get; }

    public bool HasChanges => Added.Count > 0 || Modified.Count > 0 || Removed.Count > 0;
}
