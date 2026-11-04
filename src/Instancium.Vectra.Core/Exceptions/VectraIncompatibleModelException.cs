#nullable enable
namespace Instancium.Vectra.Core.Exceptions;

/// <summary>
/// Thrown when attempting to open a collection with an embedding model (or fingerprint)
/// that differs from the one it was originally created with.
/// </summary>
public sealed class VectraIncompatibleModelException : VectraException
{
    public VectraIncompatibleModelException(string expectedModelId, string actualModelId)
        : base($"Incompatible embedding model: collection was created with '{expectedModelId}', but opened with '{actualModelId}'.")
    {
        ExpectedModelId = expectedModelId;
        ActualModelId = actualModelId;
    }

    public string ExpectedModelId { get; }
    public string ActualModelId { get; }
}
