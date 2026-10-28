namespace Instancium.Vectra.Tests.Unit.Rag;

using System.Runtime.CompilerServices;
using Instancium.Vectra.Core.Exceptions;
using Instancium.Vectra.Infrastructure.Chat;
using Instancium.Vectra.Infrastructure.Configuration;
using Instancium.Vectra.Infrastructure.Transport;
using InstantAIGate.Core.Dtos.Inference;
using InstantAIGate.Core.Dtos.Session;
using InstantAIGate.Core.Dtos.Status;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

[Trait("Category", "Unit")]
public sealed class InstantAIGateChatAdapterTests
{

    [Fact]
    public async Task StreamAnswerAsync_ShouldJoinSessionStreamTokensAndDestroySession()
    {
        var transport = new StubChatTransport(script: new[]
        {
        new SessionTokenDelta("script", "Hello "),
        new SessionTokenDelta("script", ""),
        new SessionTokenDelta("script", "world", IsDone: true),
        new SessionTokenDelta("script", "must never be yielded")
    });
        var adapter = CreateAdapter(transport);

        var tokens = new List<string>();
        await foreach (string token in adapter.StreamAnswerAsync("prompt text"))
        {
            tokens.Add(token);
        }

        Assert.Equal(new[] { "Hello ", "world" }, tokens);
        Assert.Equal(1, transport.ConnectCalls);
        Assert.Equal(1, transport.StatusCalls);
        Assert.NotNull(transport.JoinedSessionId);
        Assert.Equal("qwen3-4b-instruct", transport.JoinedRepoId);
        Assert.Null(transport.EphemeralSessionId); // Changed: no longer ephemeral
        Assert.Equal(transport.JoinedSessionId, transport.DestroyedSessionId);
        Assert.Equal(transport.JoinedSessionId, transport.StreamedSessionId);
        Assert.Equal("user", transport.LastMessage!.Role);
        Assert.Contains("prompt text", transport.LastMessage.Content);

        // Sequence no longer includes "ephemeral"
        Assert.Equal(
            new[] { "connect", "status", "join", "stream" },
            transport.Sequence.Take(4));
        Assert.Equal("destroy", transport.Sequence[^1]);
    }

    [Fact]
    public async Task StreamAnswerAsync_ShouldDestroySessionWhenStreamFails()
    {
        var transport = new StubChatTransport(
            script: new[] { new SessionTokenDelta("script", "partial") },
            streamFailure: new VectraException("gateway pushed ReceiveError"));
        var adapter = CreateAdapter(transport);

        await Assert.ThrowsAsync<VectraException>(async () =>
        {
            await foreach (string token in adapter.StreamAnswerAsync("prompt"))
            {
                Assert.Equal("partial", token);
            }
        });

        Assert.Equal(1, transport.DestroyCalls);
    }

    [Fact]
    public async Task StreamAnswerAsync_ShouldThrowWhenGatewayNotReady()
    {
        var transport = new StubChatTransport(status: GatewayOperationalStatus.ModelLoading);
        var adapter = CreateAdapter(transport);

        var ex = await Assert.ThrowsAsync<VectraException>(async () =>
        {
            await foreach (string token in adapter.StreamAnswerAsync("prompt"))
            {
                Assert.Fail($"Unexpected token '{token}'.");
            }
        });

        Assert.Contains("not ready", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.JoinCalls);
        Assert.Equal(0, transport.DestroyCalls);
    }

    private static InstantAIGateChatAdapter CreateAdapter(StubChatTransport transport) =>
        new(
            transport,
            Options.Create(new ChatGatewayOptions { RepoId = "qwen3-4b-instruct" }),
            NullLogger<InstantAIGateChatAdapter>.Instance);

    private sealed class StubChatTransport : IChatTransport
    {
        private readonly GatewayOperationalStatus _status;
        private readonly IReadOnlyList<SessionTokenDelta> _script;
        private readonly Exception? _streamFailure;
        private readonly List<string> _sequence = new();
        private int _connectCalls;
        private int _statusCalls;
        private int _joinCalls;
        private int _destroyCalls;

        public StubChatTransport(
            IReadOnlyList<SessionTokenDelta>? script = null,
            GatewayOperationalStatus status = GatewayOperationalStatus.Ready,
            Exception? streamFailure = null)
        {
            _script = script ?? Array.Empty<SessionTokenDelta>();
            _status = status;
            _streamFailure = streamFailure;
        }

        public int ConnectCalls => Volatile.Read(ref _connectCalls);
        public int StatusCalls => Volatile.Read(ref _statusCalls);
        public int JoinCalls => Volatile.Read(ref _joinCalls);
        public int DestroyCalls => Volatile.Read(ref _destroyCalls);
        public string? JoinedSessionId { get; private set; }
        public string? JoinedRepoId { get; private set; }
        public string? EphemeralSessionId { get; private set; }
        public string? DestroyedSessionId { get; private set; }
        public string? StreamedSessionId { get; private set; }
        public ChatMessage? LastMessage { get; private set; }

        public IReadOnlyList<string> Sequence
        {
            get
            {
                lock (_sequence)
                {
                    return _sequence.ToArray();
                }
            }
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectCalls);
            lock (_sequence)
            {
                _sequence.Add("connect");
            }

            return Task.CompletedTask;
        }

        public Task<GatewayStatusDetails> GetGatewayStatusAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _statusCalls);
            lock (_sequence)
            {
                _sequence.Add("status");
            }

            return Task.FromResult(new GatewayStatusDetails { Status = _status, ActiveModelId = "stub-chat" });
        }

        public Task JoinSessionAsync(string sessionId, string repoId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _joinCalls);
            JoinedSessionId = sessionId;
            JoinedRepoId = repoId;
            lock (_sequence)
            {
                _sequence.Add("join");
            }

            return Task.CompletedTask;
        }

        public Task MarkSessionAsEphemeralAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            EphemeralSessionId = sessionId;
            lock (_sequence)
            {
                _sequence.Add("ephemeral");
            }

            return Task.CompletedTask;
        }

        public Task DestroySessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _destroyCalls);
            DestroyedSessionId = sessionId;
            lock (_sequence)
            {
                _sequence.Add("destroy");
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<SessionTokenDelta> StreamAnswerAsync(
            string sessionId,
            ChatMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamedSessionId = sessionId;
            LastMessage = message;
            lock (_sequence)
            {
                _sequence.Add("stream");
            }

            foreach (SessionTokenDelta delta in _script)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta with { SessionId = sessionId };
                if (delta.IsDone)
                {
                    yield break;
                }
            }

            if (_streamFailure is not null)
            {
                throw _streamFailure;
            }
        }

        public ValueTask DisposeAsync() => default;
    }
}