namespace Instancium.Vectra.Infrastructure.Configuration;

public sealed class ChatGatewayOptions
{
    public const string SectionName = "Vectra:Gateways:Chat";

    public string Url { get; set; } = "http://localhost:5000";

    public string HubPath { get; set; } = "/hub/gateway";

    public string RepoId { get; set; } = "qwen3-4b-instruct";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Wire names of the chat hub, pinned to InstantAIGate client-integration-guide v1.4
    /// (sections: Session Lifecycle, Chat Streaming, Ephemeral Sessions, Gateway Status Monitoring).
    /// Overridable per deployment without recompilation.
    /// </summary>
    public ChatHubRpcNames Rpc { get; set; } = new();
}

public sealed class ChatHubRpcNames
{
    /// <summary>Guide v1.4, Session Lifecycle: JoinSession(sessionId, repoId).</summary>
    public string JoinSession { get; set; } = "JoinSession";

    /// <summary>Guide v1.4, Ephemeral Sessions: MarkSessionAsEphemeral(sessionId).</summary>
    public string MarkSessionAsEphemeral { get; set; } = "MarkSessionAsEphemeral";

    /// <summary>Guide v1.4, Chat Streaming: SendPromptDelta(sessionId, message); returns before generation completes.</summary>
    public string SendPromptDelta { get; set; } = "SendPromptDelta";

    /// <summary>Guide v1.4, Chat Streaming: server push of SessionTokenDelta; IsDone marks stream end.</summary>
    public string ReceiveTokenDelta { get; set; } = "ReceiveTokenDelta";

    /// <summary>Guide v1.4, Error Handling: general gateway errors pushed as string.</summary>
    public string ReceiveError { get; set; } = "ReceiveError";

    /// <summary>Guide v1.4, Error Handling: context window exceeded push.</summary>
    public string ReceiveContextOverflow { get; set; } = "ReceiveContextOverflow";

    /// <summary>Guide v1.4, Connection Setup: session ended push.</summary>
    public string ReceiveSessionClosed { get; set; } = "ReceiveSessionClosed";

    /// <summary>Guide v1.4, Session Lifecycle: DestroySession(sessionId) — hard release, frees VRAM immediately.</summary>
    public string DestroySession { get; set; } = "DestroySession";

    /// <summary>Guide v1.4, Gateway Status Monitoring: GetGatewayStatus readiness RPC.</summary>
    public string GetGatewayStatus { get; set; } = "GetGatewayStatus";
}