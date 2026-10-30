#nullable enable
namespace Instancium.Vectra.Core.Models;

/// <summary>
/// A file that exists in both the current scan and the previous state,
/// but must be reindexed.
/// </summary>
public sealed record ModifiedFile
{
    public ModifiedFile(ScannedFile current, FileState previous)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previous);

        if (!string.Equals(current.RelativePath, previous.Path, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Scan entry '{current.RelativePath}' and previous state '{previous.Path}' refer to different paths.",
                nameof(previous));
        }

        Current = current;
        Previous = previous;
    }

    public ScannedFile Current { get; }

    public FileState Previous { get; }
}
