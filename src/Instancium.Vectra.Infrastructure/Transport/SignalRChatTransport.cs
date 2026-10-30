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
    private const int MaxPromptAttempts = 2;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

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
            .WithAutomaticReconnect()
            .Build();
    }

    public Task LeaveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _connection.InvokeAsync(_options.Rpc.LeaveSession, sessionId, cancellationToken);
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

    public Task DestroySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _connection.InvokeAsync(_options.Rpc.DestroySession, sessionId, cancellationToken);
    }

    // Non-iterator wrapper: argument validation must fail eagerly, not on first MoveNextAsync.
    public IAsyncEnumerable<SessionTokenDelta> StreamAnswerAsync(
        string sessionId,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        return StreamAnswerCoreAsync(sessionId, message, cancellationToken);
    }

    // Iterator body: yield lives in a try/finally WITHOUT catch (CS1626-compliant).
    // All failure/retry handling is offloaded to the producer task below.
    private async IAsyncEnumerable<SessionTokenDelta> StreamAnswerCoreAsync(
        string sessionId,
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<SessionTokenDelta> outputChannel = Channel.CreateUnbounded<SessionTokenDelta>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        using CancellationTokenSource pipelineCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task pipeline = ExecuteStreamingPipelineAsync(sessionId, message, outputChannel.Writer, pipelineCts.Token);

        try
        {
            await foreach (SessionTokenDelta delta in outputChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return delta;
            }

            // Success path: observe producer completion.
            await pipeline.ConfigureAwait(false);
        }
        finally
        {
            // Consumer abandoned enumeration early: stop the producer, dispose subscriptions.
            pipelineCts.Cancel();
        }
    }

    // Producer: owns try/catch, retry loop and hub subscriptions. Never yields, never throws out.
    private async Task ExecuteStreamingPipelineAsync(
        string sessionId,
        ChatMessage message,
        ChannelWriter<SessionTokenDelta> outputWriter,
        CancellationToken cancellationToken)
    {
        Exception? terminalException = null;

        try
        {
            for (int attempt = 1; attempt <= MaxPromptAttempts; attempt++)
            {
                Channel<SessionTokenDelta> attemptChannel = Channel.CreateUnbounded<SessionTokenDelta>(
                    new UnboundedChannelOptions { SingleReader = true });

                IDisposable tokenSubscription = _connection.On<SessionTokenDelta>(
                    _options.Rpc.ReceiveTokenDelta,
                    delta =>
                    {
                        if (!string.Equals(delta.SessionId, sessionId, StringComparison.Ordinal))
                        {
                            return;
                        }

                        attemptChannel.Writer.TryWrite(delta);
                        if (delta.IsDone)
                        {
                            attemptChannel.Writer.TryComplete();
                        }
                    });

                IDisposable errorSubscription = _connection.On<string>(
                    _options.Rpc.ReceiveError,
                    error => attemptChannel.Writer.TryComplete(
                        new VectraException($"Chat gateway error during session '{sessionId}': {error}")));

                // Propagate the canonical exception as-is so upper layers can apply mitigation.
                IDisposable overflowSubscription = _connection.On<ContextOverflowException>(
                    _options.Rpc.ReceiveContextOverflow,
                    ex => attemptChannel.Writer.TryComplete(ex));

                IDisposable closedSubscription = _connection.On<string>(
                    _options.Rpc.ReceiveSessionClosed,
                    closedSessionId =>
                    {
                        if (string.Equals(closedSessionId, sessionId, StringComparison.Ordinal))
                        {
                            attemptChannel.Writer.TryComplete(
                                new VectraException($"Chat session '{sessionId}' was closed by the gateway before completion."));
                        }
                    });

                bool retryNeeded = false;
                bool streamedAny = false;

                try
                {
                    // Retry path: the gateway lost the session state, re-register it before re-sending.
                    if (attempt > 1)
                    {
                        await _connection.InvokeAsync(_options.Rpc.JoinSession, sessionId, _options.RepoId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // Invoke the send RPC so gateway failures are surfaced as exceptions instead of being silently dropped.
                    await _connection.InvokeAsync(_options.Rpc.SendPromptDelta, sessionId, message, cancellationToken)
                        .ConfigureAwait(false);

                    await foreach (SessionTokenDelta delta in attemptChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        streamedAny = true;
                        await outputWriter.WriteAsync(delta, cancellationToken).ConfigureAwait(false);
                    }

                    return;
                }
                catch (Exception ex) when (
                    !streamedAny &&
                    attempt < MaxPromptAttempts &&
                    IsSessionNotActive(ex))
                {
                    _logger.LogWarning(
                        "Session {SessionId} was not active on attempt {Attempt}: {Reason}. Re-joining and retrying.",
                        sessionId,
                        attempt,
                        ex.Message);
                    retryNeeded = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    terminalException = ex;
                    return;
                }
                finally
                {
                    tokenSubscription.Dispose();
                    errorSubscription.Dispose();
                    overflowSubscription.Dispose();
                    closedSubscription.Dispose();
                    attemptChannel.Writer.TryComplete();
                }

                if (!retryNeeded)
                {
                    break;
                }

                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is already observed by the consumer through its own token.
            terminalException = null;
        }
        catch (Exception ex)
        {
            terminalException = ex;
        }
        finally
        {
            // Surface the terminal failure (if any) to the enumerator; never throw from the producer.
            outputWriter.TryComplete(terminalException);
        }
    }

    private static bool IsSessionNotActive(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("was not found", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("not active", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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