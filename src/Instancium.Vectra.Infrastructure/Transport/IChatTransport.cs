namespace Instancium.Vectra.Infrastructure.Transport;

using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;

/// <summary>
/// Internal transport seam over the chat gateway hub (guide v1.4).
/// The only component aware of SignalR chat wire protocol.
/// </summary>
internal interface IChatTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default);

    Task JoinSessionAsync(string sessionId, string repoId, CancellationToken cancellationToken = default);

    Task LeaveSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    Task DestroySessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to token/error pushes, invokes SendPromptDelta, and streams deltas until completion.
    /// Subscription is established BEFORE the prompt invoke so early tokens cannot be lost.
    /// </summary>
    IAsyncEnumerable<SessionTokenDelta> StreamAnswerAsync(
        string sessionId,
        ChatMessage message,
        CancellationToken cancellationToken = default);
}

