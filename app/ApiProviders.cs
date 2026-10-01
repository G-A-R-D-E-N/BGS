using System;

namespace BehaviourStudio.App;

public sealed record ApiProviderPreset(
    AssistantBackend Backend,
    string Name,
    string DefaultBaseUrl,
    string ModelLabel);

public static class ApiProviders
{
    public const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public const string LocalBaseUrl = "http://127.0.0.1:11434/v1";
    public const string BaseUrlSettingPrefix = "assistant.api.base_url.";

    public const string Notice =
        "This provider calls an OpenAI-compatible endpoint directly from this machine. BGS ships no API key " +
        "and never writes one to disk: a key you paste is kept only for this session. Google requires the " +
        "Gemini API for developer or business use, and users in the EEA, Switzerland, or the UK must use a " +
        "billed Google Cloud project. You are responsible for how your key is used and for following the " +
        "provider's terms.";

    public static readonly ApiProviderPreset Gemini = new(
        AssistantBackend.Gemini, "Google Gemini", GeminiBaseUrl, "Gemini model id");

    public static readonly ApiProviderPreset Local = new(
        AssistantBackend.Local, "Local models", LocalBaseUrl, "model id");

    public static bool IsApi(AssistantBackend backend) =>
        backend is AssistantBackend.Gemini or AssistantBackend.Local;

    public static ApiProviderPreset? For(AssistantBackend backend) => backend switch
    {
        AssistantBackend.Gemini => Gemini,
        AssistantBackend.Local => Local,
        _ => null,
    };

    public static string BaseUrlSetting(AssistantBackend backend) =>
        BaseUrlSettingPrefix + AssistantCliLocator.CommandName(backend);

    public static string BaseUrl(AssistantBackend backend)
    {
        string configured = Settings.Get(BaseUrlSetting(backend));
        if (configured.Length > 0) return configured.Trim();
        return For(backend)?.DefaultBaseUrl ?? "";
    }

    public static bool IsLoopback(Uri endpoint) =>
        endpoint.IsLoopback ||
        string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    public static bool AllowsCredentials(Uri endpoint) =>
        endpoint.Scheme == Uri.UriSchemeHttps || IsLoopback(endpoint);

    public static bool TryBaseUrl(AssistantBackend backend, out Uri? uri, out string error)
    {
        uri = null;
        error = "";
        string value = BaseUrl(backend);
        if (value.Length == 0)
        {
            error = "Set the API base URL first.";
            return false;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            error = "The API base URL must be an absolute http or https address.";
            return false;
        }
        if (backend == AssistantBackend.Gemini && parsed.Scheme != Uri.UriSchemeHttps)
        {
            error = "The Gemini API requires an https base URL.";
            return false;
        }
        uri = parsed;
        return true;
    }
}
