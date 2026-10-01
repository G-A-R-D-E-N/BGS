using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BehaviourStudio.App;

public sealed record ApiModelList(IReadOnlyList<string> ModelIds, string Error)
{
    public static readonly ApiModelList Empty = new(Array.Empty<string>(), "");
    public bool Failed => Error.Length > 0;
}

public static class ApiModelCatalog
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    internal static Func<string, string, CancellationToken, Task<ApiModelList>> FetchForTest = FetchAsync;

    public static Task<ApiModelList> ReadAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken = default) =>
        FetchForTest(baseUrl, apiKey ?? "", cancellationToken);

    public static async Task<ApiModelList> FetchAsync(
        string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        string url = ModelsUrl(baseUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new ApiModelList(Array.Empty<string>(), "The API base URL is not a valid address.");

        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (apiKey.Length > 0)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using HttpResponseMessage response = await http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ApiModelList(Array.Empty<string>(),
                    "The endpoint answered " + (int)response.StatusCode + ".");
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ApiModelList(Parse(body), "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              or InvalidOperationException or UriFormatException)
        {
            return new ApiModelList(Array.Empty<string>(), "The endpoint could not be reached.");
        }
    }

    public static string ModelsUrl(string baseUrl)
    {
        string trimmed = (baseUrl ?? "").Trim().TrimEnd('/');
        return trimmed.Length == 0 ? "" : trimmed + "/models";
    }

    public static IReadOnlyList<string> Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in data.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                string id = CodexProtocol.Scrub(CodexProtocol.String(entry, "id"), 120);
                if (id.Length > 0 && seen.Add(id)) ids.Add(id);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids.AsReadOnly();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
