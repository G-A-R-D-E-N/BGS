using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class ClaudeCliTests
{
    [Fact]
    public void TheMcpConfigUsesHttpWithABearerToken()
    {
        string json = ClaudeCli.BuildMcpConfigJson("http://127.0.0.1:4321/mcp", "secret-token");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement server = document.RootElement.GetProperty("mcpServers")
            .GetProperty(ClaudeCli.ServerName);

        Assert.Equal("http", server.GetProperty("type").GetString());
        Assert.Equal("http://127.0.0.1:4321/mcp", server.GetProperty("url").GetString());
        Assert.Equal("Bearer secret-token",
            server.GetProperty("headers").GetProperty("Authorization").GetString());
    }

    [Fact]
    public void TheArgumentsConfineClaudeToTheBridgeTools()
    {
        IReadOnlyList<string> arguments = ClaudeCli.Arguments("cfg.json", "claude-sonnet-4-5", "hello");

        Assert.Contains("-p", arguments);
        Assert.Contains("stream-json", arguments);
        Assert.Contains("--verbose", arguments);
        Assert.Contains("--strict-mcp-config", arguments);
        Assert.Equal("cfg.json", arguments[arguments.ToList().IndexOf("--mcp-config") + 1]);
        Assert.Equal("mcp__bgs", arguments[arguments.ToList().IndexOf("--allowedTools") + 1]);
        string denied = arguments[arguments.ToList().IndexOf("--disallowedTools") + 1];
        Assert.Contains("Bash", denied.Split(','), StringComparer.Ordinal);
        Assert.Contains("Write", denied.Split(','), StringComparer.Ordinal);
        Assert.Equal("claude-sonnet-4-5", arguments[arguments.ToList().IndexOf("--model") + 1]);
        Assert.Equal("hello", arguments[^1]);
    }

    [Fact]
    public void AnUnsetModelIsOmittedSoClaudeKeepsItsConfiguredDefault()
    {
        IReadOnlyList<string> arguments = ClaudeCli.Arguments("cfg.json", "", "hello");

        Assert.DoesNotContain("--model", arguments);
    }

    [Fact]
    public void ASuccessfulStreamYieldsTheResultText()
    {
        const string stream =
            "{\"type\":\"system\",\"subtype\":\"init\",\"mcp_servers\":[{\"name\":\"bgs\",\"status\":\"connected\"}]}\n" +
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"}]}}\n" +
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"text\",\"text\":\"pong\"}]}}\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"pong\"}\n";

        ClaudeTurn turn = ClaudeCli.Parse(stream, 0, "");

        Assert.True(turn.Completed);
        Assert.False(turn.Failed);
        Assert.Equal("pong", turn.Text);
    }

    [Fact]
    public void ARunThatReportsAnApiErrorIsAFailureCarryingClaudesOwnExplanation()
    {
        const string stream =
            "{\"type\":\"assistant\",\"message\":{\"model\":\"<synthetic>\",\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"text\",\"text\":\"There's an issue with the selected model.\"}]," +
            "\"error\":\"model_not_found\"}}\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":true,\"terminal_reason\":\"api_error\"}\n";

        ClaudeTurn turn = ClaudeCli.Parse(stream, 1, "");

        Assert.True(turn.Failed);
        Assert.Contains("selected model", turn.Error, StringComparison.Ordinal);
        Assert.Empty(turn.Text);
    }

    [Fact]
    public void ACrashWithNoTerminalEventStillReportsTheScrubbedDiagnostic()
    {
        string syntheticKey = "sk-" + new string('a', 22);
        ClaudeTurn turn = ClaudeCli.Parse("", 2, "fatal: " + syntheticKey);

        Assert.True(turn.Failed);
        Assert.Contains("fatal", turn.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(syntheticKey, turn.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompletedRunWithoutAResultFieldFallsBackToTheLastAssistantText()
    {
        const string stream =
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\"," +
            "\"content\":[{\"type\":\"text\",\"text\":\"fallback answer\"}]}}\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false}\n";

        ClaudeTurn turn = ClaudeCli.Parse(stream, 0, "");

        Assert.True(turn.Completed);
        Assert.Equal("fallback answer", turn.Text);
    }

    [Fact]
    public void ToolUseBlocksAreNotMistakenForTheAnswer()
    {
        const string stream =
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":" +
            "[{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"mcp__bgs__bgs_inspect_object\"," +
            "\"input\":{\"objectId\":\"7\"}}]}}\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"done\"}\n";

        ClaudeTurn turn = ClaudeCli.Parse(stream, 0, "");

        Assert.Equal("done", turn.Text);
    }

    [Fact]
    public void CredentialShapedTextIsRedactedBeforeItReachesTheTranscript()
    {
        string syntheticKey = "sk-" + new string('a', 22);
        ClaudeTurn turn = ClaudeCli.Parse(
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false," +
            "\"result\":\"key " + syntheticKey + " here\"}\n", 0, "");

        Assert.DoesNotContain(syntheticKey, turn.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoiseAndMalformedLinesAreIgnored()
    {
        const string stream =
            "not json\n\n[1,2]\n{\"broken\"\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"kept\"}\n";

        ClaudeTurn turn = ClaudeCli.Parse(stream, 0, "");

        Assert.True(turn.Completed);
        Assert.Equal("kept", turn.Text);
    }
}

public sealed class ClaudeAssistantSessionTests
{
    private static readonly AssistantCli Cli = new("claude.cmd", "detected");

    private static Func<AssistantCli, IReadOnlyList<string>, IReadOnlyDictionary<string, string>?,
        TimeSpan, CancellationToken, Task<AssistantCommandResult>>? _previous;

    private static void Stub(
        Func<IReadOnlyList<string>, Task<AssistantCommandResult>> handler)
    {
        _previous = AssistantCommandRunner.RunForTest;
        AssistantCommandRunner.RunForTest = (_, arguments, _, _, _) => handler(arguments);
    }

    private static void Restore()
    {
        if (_previous is not null) AssistantCommandRunner.RunForTest = _previous;
        _previous = null;
    }

    private static string Success(string text) =>
        "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"" + text + "\"}\n";

    [Fact]
    public async Task ASuccessfulTurnReturnsTheAnswerAndPointsClaudeAtTheBridge()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub(arguments =>
        {
            string configPath = arguments[arguments.ToList().IndexOf("--mcp-config") + 1];
            string config = File.ReadAllText(configPath);
            AssistantPrivateFileTests.AssertOwnerOnly(Path.GetDirectoryName(configPath)!, true);
            AssistantPrivateFileTests.AssertOwnerOnly(configPath, false);
            Assert.Contains(bridge.Url, config, StringComparison.Ordinal);
            Assert.Contains(bridge.Token, config, StringComparison.Ordinal);
            Assert.Contains("mcp__bgs", arguments, StringComparer.Ordinal);
            return Task.FromResult(new AssistantCommandResult(0, Success("pong"), ""));
        });
        try
        {
            using var session = new ClaudeAssistantSession(Cli, bridge, "claude-sonnet-4-5");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("ok", reply.Status);
            Assert.Equal("pong", reply.Text);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task ToolUseIsReportedAsActivityFromTheBridge()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub(_ =>
        {
            bridge.ToolInvoked?.Invoke("bgs.inspect_object");
            return Task.FromResult(new AssistantCommandResult(0, Success("done"), ""));
        });
        try
        {
            using var session = new ClaudeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("bgs.inspect_object", Assert.Single(reply.Tools).Name);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task AnApiErrorBecomesAProviderFailure()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub(_ => Task.FromResult(new AssistantCommandResult(1,
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":" +
            "[{\"type\":\"text\",\"text\":\"There's an issue with the selected model.\"}]}}\n" +
            "{\"type\":\"message\",\"subtype\":\"success\",\"is_error\":true}\n", "")));
        try
        {
            using var session = new ClaudeAssistantSession(Cli, bridge, "bogus");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Contains("selected model", reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task ACancelledRequestIsReportedAsCancelled()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        using var cancellation = new CancellationTokenSource();
        Stub(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(new AssistantCommandResult(-1, "", "The command timed out."));
        });
        try
        {
            using var session = new ClaudeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync(
                "hi", CodexSessionFixture.Context(), cancellation.Token);

            Assert.Equal("cancelled", reply.Status);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task AnEmptyPromptNeverReachesClaude()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub(_ => throw new InvalidOperationException("must not run"));
        try
        {
            using var session = new ClaudeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("  ", CodexSessionFixture.Context());

            Assert.Equal("error", reply.Status);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task DisposingTheSessionRemovesItsScratchConfiguration()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        string captured = "";
        Stub(arguments =>
        {
            captured = arguments[arguments.ToList().IndexOf("--mcp-config") + 1];
            return Task.FromResult(new AssistantCommandResult(0, Success("ok"), ""));
        });
        try
        {
            var session = new ClaudeAssistantSession(Cli, bridge, "");
            await session.SendAsync("hi", CodexSessionFixture.Context());
            Assert.True(File.Exists(captured));
            session.Dispose();
            Assert.False(File.Exists(captured));
        }
        finally
        {
            Restore();
        }
    }
}
