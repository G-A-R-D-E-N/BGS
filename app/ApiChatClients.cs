using System;
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace BehaviourStudio.App;

public static class ApiChatClients
{
    internal static Func<Uri, string, string, IChatClient> CreateForTest = CreateDefault;

    public static IChatClient Create(Uri endpoint, string apiKey, string model) =>
        CreateForTest(endpoint, apiKey ?? "", model ?? "");

    private static IChatClient CreateDefault(Uri endpoint, string apiKey, string model)
    {
        var options = new OpenAIClientOptions { Endpoint = endpoint };
        var credential = new ApiKeyCredential(apiKey.Length > 0 ? apiKey : "not-required");
        var client = new OpenAIClient(credential, options);
        return client.GetChatClient(model).AsIChatClient();
    }
}
