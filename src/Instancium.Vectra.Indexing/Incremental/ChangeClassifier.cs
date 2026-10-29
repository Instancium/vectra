namespace Instancium.Vectra.Indexing.Incremental;

using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Core.Models;

public sealed class ChangeClassifier : IChangeClassifier
{
    private readonly IContentExtractorRegistry _registry;

    public ChangeClassifier(IContentExtractorRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ChangeSet Classify(
        IReadOnlyList<ScannedFile> scannedFiles,
        IReadOnlyList<FileState> previousStates)
    {
        ArgumentNullException.ThrowIfNull(scannedFiles);
        ArgumentNullException.ThrowIfNull(previousStates);

        Dictionary<string, FileState> previousByPath = BuildPreviousIndex(previousStates);
        EnsureUniqueScannedPaths(scannedFiles);

        var added = new List<ScannedFile>();
        var modified = new List<ModifiedFile>();
        var unchanged = new List<FileState>();

        foreach (ScannedFile scanned in scannedFiles)
        {
            string expectedFormat = _registry.Resolve(Path.GetExtension(scanned.FullPath)).SourceKind;

            if (!previousByPath.TryGetValue(scanned.RelativePath, out FileState? previous))
            {
                added.Add(scanned);
                continue;
            }

            bool hashChanged = !string.Equals(previous.Sha256, scanned.Sha256, StringComparison.OrdinalIgnoreCase);
            bool formatChanged = !string.Equals(previous.Format, expectedFormat, StringComparison.Ordinal);
            bool retryAfterFailure = previous.Status == IndexFileStatus.Failed;
            bool reappearedAfterRemoval = previous.Status == IndexFileStatus.Removed;

            if (hashChanged || formatChanged || retryAfterFailure || reappearedAfterRemoval)
            {
                modified.Add(new ModifiedFile(scanned, previous));
            }
            else
            {
                unchanged.Add(previous);
            }
        }

        HashSet<string> scannedPaths = scannedFiles
            .Select(file => file.RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        FileState[] removed = previousStates
            .Where(state => !scannedPaths.Contains(state.Path) && state.Status != IndexFileStatus.Removed)
            .ToArray();

        return new ChangeSet(
            added.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray(),
            modified.OrderBy(change => change.Current.RelativePath, StringComparer.Ordinal).ToArray(),
            removed.OrderBy(state => state.Path, StringComparer.Ordinal).ToArray(),
            unchanged.OrderBy(state => state.Path, StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, FileState> BuildPreviousIndex(IReadOnlyList<FileState> previousStates)
    {
        var index = new Dictionary<string, FileState>(StringComparer.Ordinal);
        foreach (FileState state in previousStates)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (!index.TryAdd(state.Path, state))
            {
                throw new ArgumentException(
                    $"Duplicate previous state for path '{state.Path}'.",
                    nameof(previousStates));
            }
        }

        return index;
    }

    private static void EnsureUniqueScannedPaths(IReadOnlyList<ScannedFile> scannedFiles)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ScannedFile file in scannedFiles)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (!seen.Add(file.RelativePath))
            {
                throw new ArgumentException(
                    $"Duplicate scanned path '{file.RelativePath}'.",
                    nameof(scannedFiles));
            }
        }
    }
}
