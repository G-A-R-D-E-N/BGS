using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class CodexProtocolTests
{
    [Fact]
    public void ReadsResponseNotificationAndServerRequest()
    {
        Assert.True(CodexProtocol.TryRead(
            "{\"id\":7,\"result\":{\"ok\":true}}", out CodexEnvelope response));
        Assert.Equal(CodexMessageKind.Response, response.Kind);
        Assert.Equal("7", response.IdKey);
        Assert.Equal("", response.Method);

        Assert.True(CodexProtocol.TryRead(
            "{\"method\":\"item/agentMessage/delta\",\"params\":{\"delta\":\"hi\"},\"emittedAtMs\":1}",
            out CodexEnvelope notification));
        Assert.Equal(CodexMessageKind.Notification, notification.Kind);
        Assert.Equal("item/agentMessage/delta", notification.Method);
        Assert.Equal("hi", CodexProtocol.String(notification.Parameters, "delta"));

        Assert.True(CodexProtocol.TryRead(
            "{\"id\":3,\"method\":\"item/tool/call\",\"params\":{\"tool\":\"bgs_inspect_behavior\"}}",
            out CodexEnvelope request));
        Assert.Equal(CodexMessageKind.ServerRequest, request.Kind);
        Assert.Equal("bgs_inspect_behavior", CodexProtocol.String(request.Parameters, "tool"));
    }

    [Fact]
    public void ReadsStringRequestIds()
    {
        Assert.True(CodexProtocol.TryRead(
            "{\"id\":\"abc\",\"result\":{}}", out CodexEnvelope envelope));
        Assert.Equal("s:abc", envelope.IdKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"method\":\"thread/started\",\"params\":{}}")]
    public void RejectsMalformedOrAmbiguousMessages(string line)
    {
        if (CodexProtocol.TryRead(line, out CodexEnvelope envelope))
            Assert.NotEqual(CodexMessageKind.Invalid, envelope.Kind);
        else
            Assert.Equal(CodexMessageKind.Invalid, envelope.Kind);
    }

    [Fact]
    public void UnknownNotificationParsesWithoutFailing()
    {
        Assert.True(CodexProtocol.TryRead(
            "{\"method\":\"mcpServer/startupStatus/updated\",\"params\":{\"name\":\"x\"}}",
            out CodexEnvelope envelope));
        Assert.Equal(CodexMessageKind.Notification, envelope.Kind);
        Assert.Equal("", CodexProtocol.String(envelope.Parameters, "missing"));
    }

    [Fact]
    public void ScrubBoundsTextAndRedactsCredentialShapes()
    {
        string scrubbed = CodexProtocol.Scrub(
            "failed with sk-ABC123DEF456 and eyJhbGciOiJIUzI1NiJ9.payload and\ncontrol\tchars", 200);
        Assert.DoesNotContain("sk-ABC123DEF456", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", scrubbed, StringComparison.Ordinal);
        Assert.Contains("[redacted]", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', scrubbed);
        Assert.DoesNotContain('\t', scrubbed);

        string bounded = CodexProtocol.Scrub(new string('a', 500), 50);
        Assert.Equal(53, bounded.Length);
    }

    [Fact]
    public void MapsBgsToolNamesToProtocolNamesAndBack()
    {
        Assert.Equal("bgs_inspect_behavior", CodexProtocol.ToProtocolToolName("bgs.inspect_behavior"));
        Assert.True(CodexProtocol.IsProtocolToolName("bgs_inspect_behavior"));
        Assert.False(CodexProtocol.IsProtocolToolName("bgs.inspect_behavior"));

        string[] names = { "bgs.inspect_behavior", "bgs.set_clip_animation" };
        Assert.True(CodexProtocol.TryResolveBgsTool("bgs_set_clip_animation", names, out string resolved));
        Assert.Equal("bgs.set_clip_animation", resolved);
        Assert.False(CodexProtocol.TryResolveBgsTool("bgs_delete_everything", names, out _));
    }

    [Fact]
    public void BoundsToolArguments()
    {
        Assert.Equal("{}", CodexProtocol.ArgumentJson(null));
        Assert.Equal("{\"a\":1}", CodexProtocol.ArgumentJson(Parse("{\"a\":1}")));
        Assert.Equal("{}", CodexProtocol.ArgumentJson(Parse("{\"a\":\"" + new string('x', 70000) + "\"}")));
    }

    [Fact]
    public void ExtractsVersionFromUserAgent()
    {
        Assert.Equal("0.154.0",
            CodexAppServerClient.VersionFromUserAgent(
                "behaviour_graph_studio/0.154.0 (Windows 10.0) (bgs; 1.1.0)"));
        Assert.Equal("", CodexAppServerClient.VersionFromUserAgent("no-version-here"));
    }

    [Fact]
    public async Task ReadsBoundedLinesAndDropsOversizedOnes()
    {
        string payload = new string('x', 9000) + "\n" + "{\"id\":1,\"result\":{}}\n";
        var reader = new CodexLineReader(new StringReader(payload));

        string? first = await reader.ReadLineAsync(4096, CancellationToken.None);
        Assert.Equal("", first);
        Assert.Equal(1, reader.OversizedLineCount);

        string? second = await reader.ReadLineAsync(4096, CancellationToken.None);
        Assert.Equal("{\"id\":1,\"result\":{}}", second);
        Assert.Null(await reader.ReadLineAsync(4096, CancellationToken.None));
    }

    [Fact]
    public async Task ReadsFinalLineWithoutTrailingNewline()
    {
        var reader = new CodexLineReader(new StringReader("one\r\ntwo"), 2);
        Assert.Equal("one", await reader.ReadLineAsync(100, CancellationToken.None));
        Assert.Equal("two", await reader.ReadLineAsync(100, CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(100, CancellationToken.None));
    }

    private static System.Text.Json.JsonElement? Parse(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
