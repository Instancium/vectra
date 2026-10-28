namespace Instancium.Vectra.Infrastructure.Chat;

using System.Runtime.CompilerServices;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Core.Contracts;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class InstantAIGateChatAdapter : IChatCompletionClient, IDisposable
{
    private readonly IChatTransport _transport;
    private readonly ChatGatewayOptions _options;
    private readonly ILogger<InstantAIGateChatAdapter> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private volatile bool _initialized;

    internal InstantAIGateChatAdapter(
        IChatTransport transport,
        IOptions<ChatGatewayOptions> options,
        ILogger<InstantAIGateChatAdapter> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        GatewayStatusDetails status = await _transport.GetGatewayStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.Status != GatewayOperationalStatus.Ready)
        {
            throw new VectraException(
                $"Chat gateway is not ready (status={status.Status}, model={status.ActiveModelId ?? "none"}, " +
                $"detail={status.StageDescription ?? "n/a"}). Wait for Ready before asking.");
        }

        string sessionId = Guid.NewGuid().ToString("N");
        await _transport.JoinSessionAsync(sessionId, _options.RepoId, cancellationToken).ConfigureAwait(false);

        // Removed MarkSessionAsEphemeralAsync: ephemeral sessions are destroyed on disconnect,
        // which can happen during implicit SignalR reconnects (connectionId changes).
        // We rely on explicit DestroySession in finally instead.

        _logger.LogInformation(
            "Chat session joined (non-ephemeral). Session={SessionId}, Repo={RepoId}",
            sessionId,
            _options.RepoId);

        try
        {
            await foreach (SessionTokenDelta delta in _transport
                .StreamAnswerAsync(sessionId, new ChatMessage("user", prompt), cancellationToken)
                .ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(delta.Content))
                {
                    yield return delta.Content;
                }
            }
        }
        finally
        {
            await DestroySessionBestEffortAsync(sessionId).ConfigureAwait(false);
        }
    }

    public void Dispose() => _connectionLock.Dispose();

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task DestroySessionBestEffortAsync(string sessionId)
    {
        try
        {
            await _transport.DestroySessionAsync(sessionId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to destroy chat session {SessionId}; session reaper will collect it after 2 minutes.",
                sessionId);
        }
    }
}