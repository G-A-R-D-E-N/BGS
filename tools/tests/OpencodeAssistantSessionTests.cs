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

public sealed class OpencodeAssistantSessionTests
{
    private static readonly AssistantCli Cli = new("opencode", "detected");

    private static Func<AssistantCli, IReadOnlyList<string>, IReadOnlyDictionary<string, string>?,
        TimeSpan, CancellationToken, Task<AssistantCommandResult>>? _previous;

    private static void Stub(
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, string>?, Task<AssistantCommandResult>> handler)
    {
        _previous = AssistantCommandRunner.RunForTest;
        AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            arguments.Contains("debug") && environment is not null &&
            environment.TryGetValue("OPENCODE_CONFIG", out string? path) && File.Exists(path)
                ? Task.FromResult(new AssistantCommandResult(0, OpencodeStubConfig.Resolved(path), ""))
                : arguments.Contains("debug")
                    ? Task.FromResult(new AssistantCommandResult(0,
                        "{\"mcp\":{\"f4-re-mcp\":{\"type\":\"local\"}}}", ""))
                    : handler(arguments, environment);
    }

    private static void Restore()
    {
        if (_previous is not null) AssistantCommandRunner.RunForTest = _previous;
        _previous = null;
    }

    private static AssistantCommandResult Text(string body) =>
        new(0, "{\"type\":\"text\",\"part\":{\"text\":\"" + body + "\"}}\n", "");

    [Fact]
    public async Task ASuccessfulTurnReturnsTheAnswerAndPointsOpencodeAtTheBridge()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((arguments, environment) =>
        {
            AssistantPrivateFileTests.AssertOwnerOnly(Path.GetDirectoryName(environment!["OPENCODE_CONFIG"])!, true);
            AssistantPrivateFileTests.AssertOwnerOnly(environment["OPENCODE_CONFIG"], false);
            Assert.Contains("run", arguments);
            Assert.Contains("--format", arguments);
            Assert.Contains("json", arguments);
            Assert.Contains("--pure", arguments);
            string model = arguments.ToList()[arguments.ToList().IndexOf("--model") + 1];
            Assert.Equal("opencode-go/gpt-5.6-luna", model);
            string config = File.ReadAllText(environment!["OPENCODE_CONFIG"]);
            Assert.Contains(bridge.TokenQueryUrl(), config, StringComparison.Ordinal);
            Assert.Contains("deny", config, StringComparison.Ordinal);
            return Task.FromResult(Text("pong"));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "opencode-go/gpt-5.6-luna");
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
    public async Task ADefaultModelIsOmittedSoOpencodeUsesItsOwnConfiguredDefault()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((arguments, _) =>
        {
            Assert.DoesNotContain("--model", arguments);
            return Task.FromResult(Text("fine"));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("ok", reply.Status);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task ToolUseIsReportedAsActivity()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((_, _) =>
        {
            bridge.ToolInvoked?.Invoke("bgs.inspect_object");
            bridge.ToolInvoked?.Invoke("bgs.inspect_object");
            return Task.FromResult(Text("done"));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            AssistantToolActivity activity = Assert.Single(reply.Tools);
            Assert.Equal("bgs.inspect_object", activity.Name);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task AnErrorEventBecomesAProviderFailureRatherThanAnEmptyAnswer()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((_, _) => Task.FromResult(new AssistantCommandResult(1,
            "{\"type\":\"error\",\"error\":{\"name\":\"UnknownError\"," +
            "\"data\":{\"message\":\"Unexpected server error.\"}}}\n", "")));
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal("Unexpected server error.", reply.Text);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task AFailureWithNoParsableOutputReportsTheScrubbedDiagnostic()
    {
        string syntheticKey = "sk-" + new string('a', 22);
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((_, _) => Task.FromResult(new AssistantCommandResult(
            1, "", "fatal: " + syntheticKey)));
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.DoesNotContain(syntheticKey, reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task ASuccessfulRunWithNoAnswerIsNotReportedAsOk()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((_, _) => Task.FromResult(new AssistantCommandResult(0, "", "")));
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("hi", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
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
        Stub((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new AssistantCommandResult(-1, "", "The command timed out."));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
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
    public async Task AnEmptyPromptNeverReachesTheProvider()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((_, _) => throw new InvalidOperationException("must not run"));
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("   ", CodexSessionFixture.Context());

            Assert.Equal("error", reply.Status);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public async Task TheUsersOwnMcpServersAreDisabledInTheRunConfiguration()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        string captured = "";
        Stub((_, environment) =>
        {
            captured = environment!["OPENCODE_CONFIG"];
            return Task.FromResult(Text("ok"));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            await session.SendAsync("hi", CodexSessionFixture.Context());

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(captured));
            JsonElement servers = document.RootElement.GetProperty("mcp");
            Assert.False(servers.GetProperty("f4-re-mcp").GetProperty("enabled").GetBoolean());
            Assert.True(servers.GetProperty("bgs").GetProperty("enabled").GetBoolean());
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
        Stub((_, environment) =>
        {
            captured = environment!["OPENCODE_CONFIG"];
            return Task.FromResult(Text("ok"));
        });
        try
        {
            var session = new OpencodeAssistantSession(Cli, bridge, "");
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

    [Fact]
    public async Task ProviderSlashCommandsAreNeverForwarded()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        Stub((arguments, _) =>
        {
            if (arguments.Contains("--command"))
                return Task.FromResult(Text("SHELL EXECUTED"));
            return Task.FromResult(Text("ordinary answer"));
        });
        try
        {
            using var session = new OpencodeAssistantSession(Cli, bridge, "");
            AssistantReply reply = await session.SendAsync("/evil !`whoami`", CodexSessionFixture.Context());

            Assert.Equal("ok", reply.Status);
            Assert.DoesNotContain("SHELL EXECUTED", reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            Restore();
        }
    }
}
