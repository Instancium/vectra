namespace Instancium.Vectra.Infrastructure.Transport;

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Configuration;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using InstantAIGate.Core.Exceptions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class SignalRChatTransport : IChatTransport
{
    private readonly HubConnection _connection;
    private readonly ChatGatewayOptions _options;
    private readonly ILogger<SignalRChatTransport> _logger;
    private bool _disposed;

    public SignalRChatTransport(
        IOptions<ChatGatewayOptions> options,
        ILogger<SignalRChatTransport> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connection = new HubConnectionBuilder()
            .WithUrl($"{_options.Url}{_options.HubPath}", httpOptions =>
            {
                httpOptions.AccessTokenProvider = () => Task.FromResult<string?>(_options.ApiKey);
            })
            // Removed WithAutomaticReconnect() to prevent implicit reconnections that change
            // connectionId and trigger EphemeralSessionRegistry.NotifyConnectionDisconnected,
            // which destroys sessions mid-stream.
            .Build();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State == HubConnectionState.Connected)
        {
            return;
        }

        await _connection.StartAsync(cancellationToken);
    }

    public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default)
        => _connection.InvokeAsync<GatewayStatusDetails>(_options.Rpc.GetGatewayStatus, cancellationToken);

    public Task JoinSessionAsync(string sessionId, string repoId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(repoId);
        return _connection.InvokeAsync(_options.Rpc.JoinSession, sessionId, repoId, cancellationToken);
    }

    public Task MarkSessionAsEphemeralAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _connection.InvokeAsync(_options.Rpc.MarkSessionAsEphemeral, sessionId, cancellationToken);
    }

    public Task DestroySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _connection.InvokeAsync(_options.Rpc.DestroySession, sessionId, cancellationToken);
    }

    public async IAsyncEnumerable<SessionTokenDelta> StreamAnswerAsync(
        string sessionId,
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        Channel<SessionTokenDelta> channel = Channel.CreateUnbounded<SessionTokenDelta>(
            new UnboundedChannelOptions { SingleReader = true });

        IDisposable tokenSubscription = _connection.On<SessionTokenDelta>(
            _options.Rpc.ReceiveTokenDelta,
            delta =>
            {
                if (!string.Equals(delta.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return;
                }

                channel.Writer.TryWrite(delta);
                if (delta.IsDone)
                {
                    channel.Writer.TryComplete();
                }
            });

        IDisposable errorSubscription = _connection.On<string>(
            _options.Rpc.ReceiveError,
            error => channel.Writer.TryComplete(
                new VectraException($"Chat gateway error during session '{sessionId}': {error}")));

        IDisposable overflowSubscription = _connection.On<ContextOverflowException>(
            _options.Rpc.ReceiveContextOverflow,
            ex => channel.Writer.TryComplete(
                new VectraException(
                    $"Context overflow in chat session '{sessionId}': {ex.Message} " +
                    "Reduce Top-K or the per-snippet char budget in RagPromptBuilder.",
                    ex)));

        IDisposable closedSubscription = _connection.On<string>(
            _options.Rpc.ReceiveSessionClosed,
            closedSessionId =>
            {
                if (string.Equals(closedSessionId, sessionId, StringComparison.Ordinal))
                {
                    channel.Writer.TryComplete(
                        new VectraException($"Chat session '{sessionId}' was closed by the gateway before completion."));
                }
            });

        try
        {
            await _connection.InvokeAsync(_options.Rpc.SendPromptDelta, sessionId, message, cancellationToken)
                .ConfigureAwait(false);

            await foreach (SessionTokenDelta delta in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return delta;
            }
        }
        finally
        {
            tokenSubscription.Dispose();
            errorSubscription.Dispose();
            overflowSubscription.Dispose();
            closedSubscription.Dispose();
            channel.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _connection.DisposeAsync();
    }
}