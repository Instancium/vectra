namespace Instancium.Vectra.Storage.Disk;

/// <summary>
/// Atomic write primitive shared by all disk artifacts:
/// write to a temporary file, then rename over the target (risk R-1).
/// </summary>
internal static class AtomicFileWriter
{
    private const string TemporarySuffix = ".tmp";

    public static async Task WriteAllBytesAtomicAsync(
        string targetPath,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        string temporaryPath = targetPath + TemporarySuffix;
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, payload, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
