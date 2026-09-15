using System;
using System.Net;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace BehaviourStudio.App;

public sealed record AssistantProviderOptions(
    string BaseUrl,
    string Model,
    string ApiKeyEnvironment)
{
    public static AssistantProviderOptions FromSettings() => new(
        Settings.Get("assistant.base_url") is { Length: > 0 } baseUrl
            ? baseUrl : "https://api.openai.com/v1",
        Settings.Get("assistant.model") is { Length: > 0 } model ? model : "gpt-5",
        Settings.Get("assistant.api_key_env") is { Length: > 0 } environment
            ? environment : "BGS_AI_API_KEY");
}

public static class AssistantProvider
{
    public static bool TryCreate(
        AssistantProviderOptions configuration,
        out IChatClient? client,
        out string error)
    {
        client = null;
        error = "";
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.Model))
        {
            error = "an assistant model is required";
            return false;
        }
        if (string.IsNullOrWhiteSpace(configuration.ApiKeyEnvironment))
        {
            error = "an assistant credential environment variable is required";
            return false;
        }
        if (!Uri.TryCreate(configuration.BaseUrl, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme is not ("http" or "https") || endpoint.Host.Length == 0)
        {
            error = "assistant base URL must be an http or https endpoint";
            return false;
        }
        if (endpoint.Scheme == "http" && !IsLoopback(endpoint.Host))
        {
            error = "assistant credentials may only use HTTPS or a loopback HTTP endpoint";
            return false;
        }

        string? key = Environment.GetEnvironmentVariable(configuration.ApiKeyEnvironment);
        if (string.IsNullOrWhiteSpace(key))
        {
            error = $"set {configuration.ApiKeyEnvironment} to connect to the assistant provider";
            return false;
        }

        try
        {
            var openAi = new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions
            {
                Endpoint = endpoint,
            });
            client = openAi.GetChatClient(configuration.Model).AsIChatClient();
            return true;
        }
        catch (Exception)
        {
            error = "the assistant provider could not be configured";
            return false;
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
}
