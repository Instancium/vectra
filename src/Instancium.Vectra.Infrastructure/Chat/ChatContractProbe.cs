namespace Instancium.Vectra.Infrastructure.Chat;

using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed record ChatProbeStep(
    string Operation,
    string WireName,
    int ArgumentCount,
    bool Succeeded,
    string? ServerError,
    string? Detail = null);

/// <summary>
/// Live handshake diagnostic for the chat hub wire contract (guide v1.4 sequence).
/// Identifies mismatched hub method names/arity in one run.
/// </summary>
public sealed class ChatContractProbe
{
    private readonly IChatTransport _transport;
    private readonly ChatGatewayOptions _options;
    private readonly ILogger<ChatContractProbe> _logger;

    internal ChatContractProbe(
        IChatTransport transport,
        IOptions<ChatGatewayOptions> options,
        ILogger<ChatContractProbe> logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ChatProbeStep>> RunAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<ChatProbeStep>();

        try
        {
            await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
            steps.Add(new ChatProbeStep("Connect", $"{_options.Url}{_options.HubPath}", 0, true, null));
        }
        catch (Exception ex)
        {
            steps.Add(new ChatProbeStep("Connect", $"{_options.Url}{_options.HubPath}", 0, false, ex.Message));
            return steps;
        }

        try
        {
            GatewayStatusDetails status = await _transport.GetGatewayStatusAsync(cancellationToken).ConfigureAwait(false);
            steps.Add(new ChatProbeStep(
                "GetGatewayStatus",
                _options.Rpc.GetGatewayStatus,
                0,
                true,
                null,
                $"status={status.Status}, model={status.ActiveModelId ?? "none"}"));
        }
        catch (Exception ex)
        {
            steps.Add(new ChatProbeStep("GetGatewayStatus", _options.Rpc.GetGatewayStatus, 0, false, ex.Message));
        }

        string probeSessionId = $"vectra-probe-{Guid.NewGuid():N}";

        steps.Add(await TryAsync(
            "JoinSession",
            _options.Rpc.JoinSession,
            2,
            () => _transport.JoinSessionAsync(probeSessionId, _options.RepoId, cancellationToken),
            cancellationToken).ConfigureAwait(false));

        steps.Add(await TryAsync(
            "MarkSessionAsEphemeral",
            _options.Rpc.MarkSessionAsEphemeral,
            1,
            () => _transport.MarkSessionAsEphemeralAsync(probeSessionId, cancellationToken),
            cancellationToken).ConfigureAwait(false));

        steps.Add(await TryAsync(
            "DestroySession",
            _options.Rpc.DestroySession,
            1,
            () => _transport.DestroySessionAsync(probeSessionId, cancellationToken),
            cancellationToken).ConfigureAwait(false));

        return steps;
    }

    private async Task<ChatProbeStep> TryAsync(
        string operation,
        string wireName,
        int argumentCount,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action().ConfigureAwait(false);
            return new ChatProbeStep(operation, wireName, argumentCount, true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Chat probe step {Operation} failed for wire name {WireName}.", operation, wireName);
            return new ChatProbeStep(operation, wireName, argumentCount, false, ex.Message);
        }
    }
}