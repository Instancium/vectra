namespace Instancium.Vectra.Sdk.Configuration;

/// <summary>
/// Configuration options for the Instancium Vectra SDK engine.
/// </summary>
public sealed class VectraSdkOptions
{
    public const string SectionName = "Vectra:Sdk";

    /// <summary>
    /// Base directory where profile storage and indexes are maintained.
    /// Defaults to current working directory if left empty.
    /// </summary>
    public string BaseStorageDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Default model identifier to use for embeddings (e.g., "qwen3-embedding-0.6b").
    /// </summary>
    public string DefaultEmbeddingRepoId { get; set; } = "qwen3-embedding-0.6b";

    /// <summary>
    /// Subdirectory path pattern for profile index persistence.
    /// Defaults to ".instancium/vectra".
    /// </summary>
    public string EcosystemFolderName { get; set; } = ".instancium";

    /// <summary>
    /// Product folder name within ecosystem directory.
    /// </summary>
    public string ProductFolderName { get; set; } = "vectra";
}
