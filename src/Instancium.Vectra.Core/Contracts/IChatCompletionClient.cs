namespace Instancium.Vectra.Core.Contracts;

/// <summary>
/// Vectra-owned port for streamed answer generation.
/// Implementations live in Infrastructure; Core stays free of gateway types.
/// </summary>
public interface IChatCompletionClient
{
    IAsyncEnumerable<string> StreamAnswerAsync(string prompt, CancellationToken cancellationToken = default);
}
