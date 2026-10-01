using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class MultiChatIsolationTests
{
    [Fact]
    public void DisposeSessionDoesNotDisposeConnection()
    {
        using var fixture = ConnectedConnection();
        Assert.True(fixture.Connection.IsConnected);

        fixture.Session.Dispose();

        Assert.True(fixture.Connection.IsConnected,
            "session.Dispose() must not tear down the connection it does not own.");
    }

    [Fact]
    public async Task ConnectionOutlivesADisposedSessionAndStillServes()
    {
        using var fixture = ConnectedConnection();
        await fixture.Session.SendAsync("first", CodexSessionFixture.Context()).ConfigureAwait(false);
        fixture.Session.Dispose();

        using var next = new CodexAssistantSession(fixture.Connection,
            Array.Empty<Microsoft.Extensions.AI.AIFunction>());
        AssistantReply reply = await next.SendAsync("after", CodexSessionFixture.Context()).ConfigureAwait(false);
        Assert.Equal("ok", reply.Status);
    }

    [Fact]
    public void RoutesToolCallsToSessionByThreadId()
    {
        using var fixture = new FakeConnection();
        int callsA = 0;
        int callsB = 0;
        var sessionA = new RecordingSession(fixture.Connection, _ =>
        {
            callsA++;
            return Task.FromResult(new CodexToolResult(true, "ok-a"));
        }, "thread-A");
        var sessionB = new RecordingSession(fixture.Connection, _ =>
        {
            callsB++;
            return Task.FromResult(new CodexToolResult(true, "ok-b"));
        }, "thread-B");

        fixture.Server.EmitServerRequest(
            CodexMethods.DynamicToolCall,
            "\"s:42\"",
            "{\"threadId\":\"thread-A\",\"turnId\":\"turn-A\",\"callId\":\"c1\",\"tool\":\"bgs.inspect_behavior\",\"arguments\":{}}");
        fixture.Server.EmitServerRequest(
            CodexMethods.DynamicToolCall,
            "\"s:43\"",
            "{\"threadId\":\"thread-B\",\"turnId\":\"turn-B\",\"callId\":\"c2\",\"tool\":\"bgs.inspect_behavior\",\"arguments\":{}}");

        Assert.Equal(1, callsA);
        Assert.Equal(1, callsB);

        sessionA.Dispose();
        sessionB.Dispose();
    }

    [Fact]
    public void UnknownThreadIdToolCallFailsClosed()
    {
        using var fixture = new FakeConnection();
        int threadACalls = 0;
        fixture.Connection.Client.RegisterToolHandler("thread-A",
            _ => { threadACalls++; return Task.FromResult(new CodexToolResult(true, "ok-a")); });

        fixture.Server.EmitServerRequest(
            CodexMethods.DynamicToolCall,
            "\"s:100\"",
            "{\"threadId\":\"thread-orphan\",\"turnId\":\"turn-or\",\"callId\":\"c1\",\"tool\":\"bgs.inspect_behavior\",\"arguments\":{}}");

        Assert.Equal(0, threadACalls);
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LateToolCallAfterTurnCompleteIsRejected()
    {
        using var fixture = ConnectedConnection();
        await fixture.Session.SendAsync("hi", CodexSessionFixture.Context()).ConfigureAwait(false);
        string firstThread = fixture.Server.LastThreadId;
        string firstTurn = fixture.Server.LastTurnId;
        Assert.False(string.IsNullOrEmpty(firstThread));

        fixture.Server.EmitServerRequest(
            CodexMethods.DynamicToolCall,
            "\"s:7\"",
            "{\"threadId\":\"" + firstThread +
            "\",\"turnId\":\"ghost\",\"callId\":\"c1\",\"tool\":\"bgs.inspect_behavior\",\"arguments\":{}}");

        Assert.Equal(1, fixture.Server.ToolResponses);
        Assert.Contains("\"success\":false", fixture.Server.LastToolResponse, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClearHistoryProducesFreshThreadIdOnNextSend()
    {
        using var fixture = ConnectedConnection();
        await fixture.Session.SendAsync("hello", CodexSessionFixture.Context()).ConfigureAwait(false);
        string firstThread = fixture.Server.LastThreadId;
        fixture.Session.ClearHistory();
        await fixture.Session.SendAsync("again", CodexSessionFixture.Context()).ConfigureAwait(false);
        string secondThread = fixture.Server.LastThreadId;

        Assert.False(string.IsNullOrEmpty(firstThread));
        Assert.NotEqual(firstThread, secondThread);
    }

    [Fact]
    public void RegisterThenUnregisterHidesHandlerFromNextCall()
    {
        using var fixture = new FakeConnection();
        int perThread = 0;
        fixture.Connection.Client.RegisterToolHandler("thread-A",
            _ => { perThread++; return Task.FromResult(new CodexToolResult(true, "ok")); });
        Assert.True(fixture.Connection.Client.UnregisterToolHandler("thread-A"));

        fixture.Server.EmitServerRequest(
            CodexMethods.DynamicToolCall,
            "\"s:1\"",
            "{\"threadId\":\"thread-A\",\"turnId\":\"t1\",\"callId\":\"c1\",\"tool\":\"bgs.inspect_behavior\",\"arguments\":{}}");

        Assert.Equal(0, perThread);
    }

    private static ConnectedConnectionFixture ConnectedConnection()
    {
        var fixture = new ConnectedConnectionFixture();
        fixture.ConnectIfNeeded();
        return fixture;
    }

    private static string ExtractThreadId(AssistantReply reply, ConnectedConnectionFixture fixture) =>
        ExtractThreadIdFromSent(fixture.Server.Sent);

    private static string ExtractThreadIdFromSent(IReadOnlyList<string> sent)
    {
        var firstThreadStart = sent.Select(line => CodexSessionFixture.Parse(line))
            .First(root => root.TryGetProperty("method", out JsonElement method) &&
                           method.GetString() == CodexMethods.ThreadStart);
        Assert.True(firstThreadStart.TryGetProperty("result", out JsonElement firstThreadResult));
        if (firstThreadResult.TryGetProperty("thread", out JsonElement thread))
            return thread.GetProperty("id").GetString() ?? "";
        return firstThreadResult.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "";
    }

    private sealed class ConnectedConnectionFixture : FakeConnection
    {
        public CodexAssistantSession Session { get; }

        public ConnectedConnectionFixture()
        {
            Session = new CodexAssistantSession(Connection,
                Array.Empty<Microsoft.Extensions.AI.AIFunction>());
        }

        public void ConnectIfNeeded()
        {
            if (!Connection.IsConnected) Connection.ConnectAsync().GetAwaiter().GetResult();
        }
    }

    private sealed class RecordingSession : IDisposable
    {
        private readonly CodexAssistantConnection _connection;
        private readonly string _threadId;
        private bool _disposed;

        public RecordingSession(
            CodexAssistantConnection connection,
            Func<CodexToolCall, Task<CodexToolResult>> handler,
            string threadId)
        {
            _connection = connection;
            _threadId = threadId;
            _connection.Client.RegisterToolHandler(threadId, handler);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _connection.Client.UnregisterToolHandler(_threadId);
        }
    }
}

internal class FakeConnection : IDisposable
{
    private readonly Func<string, bool>? _previousFileExists;
    public FakeConnection()
    {
        _previousFileExists = CodexLocator.FileExistsForTest;
        CodexLocator.FileExistsForTest = _ => true;
        Server = new CodexTestServer();
        Connection = new CodexAssistantConnection(
            new AssistantProviderOptions(AssistantProviderOptions.CodexBackend, "", "codex-test-path"),
            (_, _) => Server);
        Connection.ConnectAsync().GetAwaiter().GetResult();
    }

    public CodexTestServer Server { get; }
    public CodexAssistantConnection Connection { get; }

    public void Dispose()
    {
        Connection.Dispose();
        CodexLocator.FileExistsForTest = _previousFileExists ?? ((string path) => System.IO.File.Exists(path));
    }
}
