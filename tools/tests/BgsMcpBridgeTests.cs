using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class BgsMcpProtocolTests
{
    private static object InspectObject(string objectId) => new { status = "ok", objectId };

    private static object PreviewEdit(string objectId) =>
        throw new InvalidOperationException("boom");

    internal static IReadOnlyList<AIFunction> Tools() => new AIFunction[]
    {
        AIFunctionFactory.Create(InspectObject, "bgs.inspect_object", "Inspects an object."),
        AIFunctionFactory.Create(PreviewEdit, "bgs.set_clip_animation", "Previews an edit."),
    };

    private static async Task<JsonElement> ExchangeAsync(string body)
    {
        var protocol = new BgsMcpProtocol(Tools());
        string response = await protocol.HandleAsync(body, CancellationToken.None);
        Assert.NotEmpty(response);
        using JsonDocument document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task InitializeAgreesAProtocolVersionAndNamesTheServer()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"," +
            "\"params\":{\"protocolVersion\":\"2025-03-26\"}}");

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        JsonElement result = root.GetProperty("result");
        Assert.Equal("2025-03-26", result.GetProperty("protocolVersion").GetString());
        Assert.Equal(BgsMcpProtocol.ServerName, result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task AnUnsupportedProtocolVersionFallsBackToTheLatestWeSpeak()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"," +
            "\"params\":{\"protocolVersion\":\"1999-01-01\"}}");

        Assert.Equal(BgsMcpProtocol.DefaultProtocolVersion,
            root.GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task AStringIdentifierIsEchoedWithoutChangingItsType()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"method\":\"tools/list\"}");

        Assert.Equal(JsonValueKind.String, root.GetProperty("id").ValueKind);
        Assert.Equal("abc", root.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToolsListPublishesBgsToolsUnderTheirProtocolNames()
    {
        JsonElement root = await ExchangeAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        JsonElement[] tools = root.GetProperty("result").GetProperty("tools")
            .EnumerateArray().ToArray();

        Assert.Contains("bgs_inspect_object", tools.Select(tool => tool.GetProperty("name").GetString()));
        Assert.Contains("bgs_set_clip_animation", tools.Select(tool => tool.GetProperty("name").GetString()));
        Assert.All(tools, tool =>
        {
            Assert.NotEmpty(tool.GetProperty("description").GetString() ?? "");
            Assert.Equal(JsonValueKind.Object, tool.GetProperty("inputSchema").ValueKind);
        });
    }

    [Fact]
    public async Task ToolsCallInvokesTheToolAndReturnsTextContent()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"," +
            "\"params\":{\"name\":\"bgs_inspect_object\",\"arguments\":{\"objectId\":\"42\"}}}");

        JsonElement result = root.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        JsonElement content = result.GetProperty("content").EnumerateArray().Single();
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Contains("42", content.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownToolIsRefusedAsAProtocolError()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"bgs.nope\"}}");

        Assert.Equal(-32602, root.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task AToolThatFailsBecomesAToolErrorRatherThanATransportError()
    {
        JsonElement root = await ExchangeAsync(
            "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\"," +
            "\"params\":{\"name\":\"bgs_set_clip_animation\",\"arguments\":{\"objectId\":\"7\"}}}");

        Assert.True(root.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(root.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task AnUnknownMethodAndMalformedJsonAreSeparateFailures()
    {
        JsonElement unknown = await ExchangeAsync("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/other\"}");
        Assert.Equal(-32601, unknown.GetProperty("error").GetProperty("code").GetInt32());

        JsonElement malformed = await ExchangeAsync("{not json");
        Assert.Equal(-32700, malformed.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ANotificationProducesNoResponseBody()
    {
        var protocol = new BgsMcpProtocol(Tools());
        string response = await protocol.HandleAsync(
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None);

        Assert.Empty(response);
    }
}

public sealed class BgsMcpBridgeTests
{
    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task TheBridgeServesAnAuthorizedCallAndRefusesEverythingUninvited()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        using var client = new HttpClient();

        Assert.EndsWith(BgsMcpBridge.EndpointPath, bridge.Url, StringComparison.Ordinal);
        Assert.NotEmpty(bridge.Token);
        Assert.Equal(2, bridge.ToolCount);

        using var authorized = new HttpRequestMessage(HttpMethod.Post, bridge.Url)
        {
            Content = Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"),
        };
        authorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Token);
        using HttpResponseMessage ok = await client.SendAsync(authorized);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (JsonDocument document = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
            Assert.True(document.RootElement.GetProperty("result").TryGetProperty("tools", out _));

        using var unauthorized = new HttpRequestMessage(HttpMethod.Post, bridge.Url)
        {
            Content = Json("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"),
        };
        unauthorized.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Token + "x");
        using HttpResponseMessage rejected = await client.SendAsync(unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using HttpResponseMessage wrongMethod = await client.GetAsync(bridge.Url);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);

        string otherPath = bridge.Url[..^BgsMcpBridge.EndpointPath.Length] + "/other";
        using HttpResponseMessage wrongPath = await client.PostAsync(otherPath, Json("{}"));
        Assert.Equal(HttpStatusCode.NotFound, wrongPath.StatusCode);
    }

    [Fact]
    public async Task ANotificationIsAcknowledgedWithoutAResponseBody()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, bridge.Url)
        {
            Content = Json("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridge.Token);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheBridgeAlsoAcceptsItsTokenFromTheUrlSoAnyClientCanAuthenticate()
    {
        using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        using var client = new HttpClient();

        using HttpResponseMessage accepted = await client.PostAsync(
            bridge.TokenQueryUrl(), Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using HttpResponseMessage refused = await client.PostAsync(
            bridge.Url + "?token=not-the-token",
            Json("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public void EveryBridgeListensOnLoopbackAndUsesAFreshToken()
    {
        using BgsMcpBridge first = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
        using BgsMcpBridge second = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());

        Assert.StartsWith("http://127.0.0.1:", first.Url, StringComparison.Ordinal);
        Assert.NotEqual(first.Url, second.Url);
        Assert.NotEqual(first.Token, second.Token);
    }
}
