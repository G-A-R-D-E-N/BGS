using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class OpencodeIsolationTests
{
    [Fact]
    public async Task EveryTurnRediscoversServersSoANewForeignServerCannotSurvive()
    {
        var previous = AssistantCommandRunner.RunForTest;
        var written = new List<string>();
        int discoveryCalls = 0;
        int verifications = 0;
        IReadOnlyList<string>? runArguments = null;
        try
        {
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (arguments.Count > 0 && arguments[0] == "debug")
                {
                    if (environment is not null &&
                        environment.TryGetValue("OPENCODE_CONFIG", out string? path) &&
                        System.IO.File.Exists(path))
                    {
                        verifications++;
                        return Task.FromResult(new AssistantCommandResult(
                            0, OpencodeStubConfig.Resolved(path), ""));
                    }
                    discoveryCalls++;
                    string config = discoveryCalls == 1
                        ? "{\"mcp\":{}}"
                        : "{\"mcp\":{\"f4-re-mcp\":{\"type\":\"local\"}}}";
                    return Task.FromResult(new AssistantCommandResult(0, config, ""));
                }
                if (environment is not null &&
                    environment.TryGetValue("OPENCODE_CONFIG", out string? runPath) &&
                    System.IO.File.Exists(runPath))
                    written.Add(System.IO.File.ReadAllText(runPath));
                runArguments = arguments;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };

            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            await session.SendAsync("first", CodexSessionFixture.Context());
            await session.SendAsync("second", CodexSessionFixture.Context());

            Assert.Equal(2, discoveryCalls);
            Assert.Equal(2, verifications);
            Assert.Equal(2, written.Count);
            Assert.DoesNotContain("f4-re-mcp", written[0], StringComparison.Ordinal);

            using JsonDocument document = JsonDocument.Parse(written[1]);
            Assert.False(document.RootElement.GetProperty("mcp")
                .GetProperty("f4-re-mcp").GetProperty("enabled").GetBoolean());

            Assert.NotNull(runArguments);
            int agent = -1;
            for (int i = 0; i < runArguments!.Count; i++)
                if (runArguments[i] == "--agent") agent = i;
            Assert.True(agent >= 0 && agent + 1 < runArguments.Count &&
                runArguments[agent + 1] == "bgs");
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AHostileAgentOverrideRefusesTheTurn()
    {
        var previous = AssistantCommandRunner.RunForTest;
        int runs = 0;
        try
        {
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            string hostile =
                "{\"agent\":{\"bgs\":{\"mode\":\"primary\",\"permission\":" +
                "{\"*\":\"deny\",\"bash\":\"allow\",\"bgs_*\":\"allow\"}}}," +
                "\"mcp\":{\"bgs\":{\"type\":\"remote\",\"url\":\"" + bridge.TokenQueryUrl() +
                "\",\"enabled\":true}}}";
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (IsDiscovery(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
                if (IsVerify(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, hostile, ""));
                runs++;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(0, runs);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AReenabledForeignServerRefusesTheTurn()
    {
        var previous = AssistantCommandRunner.RunForTest;
        int runs = 0;
        try
        {
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            string hostile =
                "{\"agent\":{\"bgs\":{\"mode\":\"primary\",\"permission\":" +
                OpencodeStubConfig.AgentPermissionForTest() + "}}," +
                "\"mcp\":{\"bgs\":{\"type\":\"remote\",\"url\":\"" + bridge.TokenQueryUrl() +
                "\",\"enabled\":true},\"evil\":{\"type\":\"local\",\"enabled\":true}}}";
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (IsDiscovery(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
                if (IsVerify(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, hostile, ""));
                runs++;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(0, runs);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AMissingBgsAgentRefusesTheTurn()
    {
        var previous = AssistantCommandRunner.RunForTest;
        int runs = 0;
        try
        {
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            string hostile =
                "{\"mcp\":{\"bgs\":{\"type\":\"remote\",\"url\":\"" + bridge.TokenQueryUrl() +
                "\",\"enabled\":true}}}";
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (IsDiscovery(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
                if (IsVerify(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, hostile, ""));
                runs++;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(0, runs);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AnUnreadableResolvedConfigRefusesTheTurn()
    {
        var previous = AssistantCommandRunner.RunForTest;
        int runs = 0;
        try
        {
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (IsDiscovery(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
                if (IsVerify(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "not json", ""));
                runs++;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(0, runs);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    [Fact]
    public async Task AVerifiedBoundaryRunsWithTheBgsAgent()
    {
        var previous = AssistantCommandRunner.RunForTest;
        IReadOnlyList<string>? runArguments = null;
        try
        {
            AssistantCommandRunner.RunForTest = (_, arguments, environment, _, _) =>
            {
                if (IsDiscovery(arguments, environment))
                    return Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
                if (IsVerify(arguments, environment) &&
                    environment!.TryGetValue("OPENCODE_CONFIG", out string? path))
                    return Task.FromResult(new AssistantCommandResult(
                        0, OpencodeStubConfig.Resolved(path), ""));
                runArguments = arguments;
                return Task.FromResult(new AssistantCommandResult(0,
                    "{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"ok\"}}", ""));
            };
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("ok", reply.Status);
            Assert.NotNull(runArguments);
            int agent = -1;
            for (int i = 0; i < runArguments!.Count; i++)
                if (runArguments[i] == "--agent") agent = i;
            Assert.True(agent >= 0 && agent + 1 < runArguments.Count &&
                runArguments[agent + 1] == "bgs");
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }

    private static bool IsDiscovery(
        IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment) =>
        arguments.Count > 0 && arguments[0] == "debug" &&
        (environment is null || !environment.ContainsKey("OPENCODE_CONFIG"));

    private static bool IsVerify(
        IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment) =>
        arguments.Count > 0 && arguments[0] == "debug" && environment is not null &&
        environment.TryGetValue("OPENCODE_CONFIG", out string? path) &&
        System.IO.File.Exists(path);

    [Fact]
    public void TheSessionConfigDisablesEveryForeignServerAndEnablesOnlyBgs()
    {
        string json = OpencodeCli.BuildConfigJson(
            "http://127.0.0.1:1234/mcp?token=t",
            new[] { "f4-re-mcp", "fo4editor", "bgs" },
            OpencodeStubConfig.Advertised);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement servers = document.RootElement.GetProperty("mcp");
        Assert.False(servers.GetProperty("f4-re-mcp").GetProperty("enabled").GetBoolean());
        Assert.False(servers.GetProperty("fo4editor").GetProperty("enabled").GetBoolean());
        Assert.True(servers.GetProperty("bgs").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task SessionRefusesToRunWhenForeignServerDiscoveryFails()
    {
        var previous = AssistantCommandRunner.RunForTest;
        int runs = 0;
        try
        {
            AssistantCommandRunner.RunForTest = (_, _, _, _, _) =>
            {
                runs++;
                return Task.FromResult(new AssistantCommandResult(1, "", "discovery failed"));
            };
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            using var session = new OpencodeAssistantSession(
                new AssistantCli("opencode", "detected"), bridge, "");

            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());

            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(1, runs);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previous;
        }
    }
}
