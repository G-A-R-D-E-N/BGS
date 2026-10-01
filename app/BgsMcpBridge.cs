using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace BehaviourStudio.App;

public sealed class BgsMcpBridge : IDisposable
{
    public const string EndpointPath = "/mcp";

    private const int MaximumHeaderBytes = 16 * 1024;
    private const int MaximumBodyBytes = 1 * 1024 * 1024;
    internal const int MaximumClients = 16;
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private readonly TcpListener _listener;
    private readonly BgsMcpProtocol _protocol;
    private readonly CancellationTokenSource _shutdown = new();

    private BgsMcpBridge(TcpListener listener, BgsMcpProtocol protocol, string token)
    {
        _listener = listener;
        _protocol = protocol;
        Token = token;
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + EndpointPath;
        _ = AcceptAsync();
    }

    public string Url { get; }
    public string Token { get; }
    public int ToolCount => _protocol.ToolCount;
    public IReadOnlyList<string> ToolNames => _protocol.ToolNames;

    public Action<string>? ToolInvoked
    {
        get => _protocol.OnToolInvoked;
        set => _protocol.OnToolInvoked = value;
    }

    public static BgsMcpBridge Start(IReadOnlyList<AIFunction> tools)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                continue;
            }
            return new BgsMcpBridge(listener, new BgsMcpProtocol(tools), NewToken());
        }
        throw new InvalidOperationException("The BGS tool bridge could not listen on a loopback port.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        try { _listener.Stop(); }
        catch (SocketException) { }
        _shutdown.Dispose();
    }

    private int _disposed;
    private int _activeClients;

    private async Task AcceptAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                or SocketException or InvalidOperationException)
            {
                return;
            }

            if (Interlocked.Increment(ref _activeClients) > MaximumClients)
            {
                Interlocked.Decrement(ref _activeClients);
                client.Dispose();
                continue;
            }
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token))
            {
                timeout.CancelAfter(HeaderTimeout);
                await RespondAsync(client.GetStream(), timeout).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException
            or ObjectDisposedException or SocketException) { }
        finally { Interlocked.Decrement(ref _activeClients); }
    }

    private async Task RespondAsync(NetworkStream stream, CancellationTokenSource timeout)
    {
        CancellationToken cancellationToken = timeout.Token;
        byte[]? header = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            await WriteAsync(stream, 400, "Bad Request", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }

        Request request = Parse(Encoding.ASCII.GetString(header));
        if (request.Path != EndpointPath)
        {
            await WriteAsync(stream, 404, "Not Found", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (request.Method != "POST")
        {
            await WriteAsync(stream, 405, "Method Not Allowed", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (request.ContentLength < 0)
        {
            await WriteAsync(stream, 400, "Bad Request", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (request.ContentLength > MaximumBodyBytes)
        {
            await WriteAsync(stream, 413, "Payload Too Large", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (!IsAuthorized(request.Authorization, request.QueryToken))
        {
            await WriteAsync(stream, 401, "Unauthorized", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (request.ContentLength == 0)
        {
            await WriteAsync(stream, 413, "Payload Too Large", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }

        timeout.CancelAfter(RequestTimeout);
        if (request.ExpectContinue)
            await WriteRawAsync(stream, "HTTP/1.1 100 Continue\r\n\r\n", cancellationToken).ConfigureAwait(false);
        string body = await ReadBodyAsync(stream, request.ContentLength, cancellationToken).ConfigureAwait(false);
        if (body.Length == 0)
        {
            await WriteAsync(stream, 400, "Bad Request", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }

        string response = await _protocol.HandleAsync(body, cancellationToken).ConfigureAwait(false);
        if (response.Length == 0)
        {
            await WriteAsync(stream, 202, "Accepted", "", false, cancellationToken).ConfigureAwait(false);
            return;
        }
        await WriteAsync(stream, 200, "OK", response, true, cancellationToken).ConfigureAwait(false);
    }

    private bool IsAuthorized(string authorization, string queryToken)
    {
        const string prefix = "Bearer ";
        if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string presented = authorization[prefix.Length..].Trim();
            if (FixedEquals(presented, Token)) return true;
        }
        return queryToken.Length > 0 && FixedEquals(queryToken, Token);
    }

    internal string TokenQueryUrl() => Url + "?token=" + Uri.EscapeDataString(Token);

    private static bool FixedEquals(string left, string right)
    {
        if (left.Length != right.Length) return false;
        int difference = 0;
        for (int index = 0; index < left.Length; index++)
            difference |= left[index] ^ right[index];
        return difference == 0;
    }

    private static async Task<byte[]?> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(1024);
        var single = new byte[1];
        while (buffer.Count < MaximumHeaderBytes)
        {
            int read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0) return buffer.Count == 0 ? null : buffer.ToArray();
            buffer.Add(single[0]);
            if (buffer.Count >= 4 &&
                buffer[^4] == (byte)'\r' && buffer[^3] == (byte)'\n' &&
                buffer[^2] == (byte)'\r' && buffer[^1] == (byte)'\n')
                return buffer.ToArray();
        }
        return null;
    }

    private static async Task<string> ReadBodyAsync(
        NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        if (length > MaximumBodyBytes) return "";
        var body = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(body.AsMemory(offset, length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) return "";
            offset += read;
        }
        return Encoding.UTF8.GetString(body);
    }

    private readonly record struct Request(
        string Method, string Path, string Authorization, string QueryToken, int ContentLength,
        bool ExpectContinue);

    private static Request Parse(string header)
    {
        string[] lines = header.Split("\r\n");
        string[] parts = lines.Length > 0 ? lines[0].Split(' ') : Array.Empty<string>();
        string method = parts.Length > 0 ? parts[0] : "";
        string target = parts.Length > 1 ? parts[1] : "";

        string path = target;
        string queryToken = "";
        int query = target.IndexOf('?');
        if (query >= 0)
        {
            path = target[..query];
            queryToken = QueryValue(target[(query + 1)..], "token");
        }

        string authorization = "";
        int? contentLength = null;
        bool expectContinue = false;
        for (int index = 1; index < lines.Length; index++)
        {
            int separator = lines[index].IndexOf(':');
            if (separator <= 0) continue;
            string name = lines[index][..separator].Trim();
            string value = lines[index][(separator + 1)..].Trim();
            if (string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase))
                authorization = value;
            else if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                contentLength = !contentLength.HasValue &&
                    int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
                    ? parsed : -1;
            else if (string.Equals(name, "Expect", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("100-continue", StringComparison.OrdinalIgnoreCase))
                expectContinue = true;
        }

        return new Request(method, path, authorization, queryToken, contentLength ?? 0, expectContinue);
    }

    private static string QueryValue(string query, string name)
    {
        foreach (string pair in query.Split('&'))
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0) continue;
            if (!string.Equals(pair[..separator], name, StringComparison.Ordinal)) continue;
            try { return Uri.UnescapeDataString(pair[(separator + 1)..]); }
            catch (UriFormatException) { return ""; }
        }
        return "";
    }

    private static Task WriteAsync(
        NetworkStream stream, int status, string reason, string body, bool json,
        CancellationToken cancellationToken)
    {
        string contentType = json ? "application/json; charset=utf-8" : "text/plain; charset=utf-8";
        string head = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                      "Content-Type: " + contentType + "\r\n" +
                      "Content-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n" +
                      "Connection: close\r\n\r\n";
        return WriteRawAsync(stream, head + body, cancellationToken);
    }

    private static async Task WriteRawAsync(
        NetworkStream stream, string text, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NewToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
