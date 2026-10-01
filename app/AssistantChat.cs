using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BehaviourStudio.App;

public enum AssistantChatRole
{
    User,
    Assistant,
    Tool,
}

public sealed record AssistantChatMessage(
    AssistantChatRole Role,
    string Text,
    DateTime TimestampUtc,
    string? ToolName = null,
    string? ToolStatus = null,
    bool? ToolRequiresApproval = null)
{
    public AssistantChatMessage Trimmed() =>
        new(Role, ChatText.Cap(Text), TimestampUtc, ToolName, ToolStatus, ToolRequiresApproval);
}

public sealed record AssistantChat(
    string Id,
    string Title,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<AssistantChatMessage> Messages)
{
    public AssistantChat WithTitle(string newTitle) =>
        new(Id, ChatText.NormalizeTitle(newTitle, Title), CreatedUtc, UpdatedUtc, Messages);

    public AssistantChat WithMessages(IReadOnlyList<AssistantChatMessage> messages, DateTime updatedUtc) =>
        new(Id, Title, CreatedUtc, updatedUtc, messages);
}

public sealed record AssistantChatSummary(
    string Id,
    string Title,
    DateTime UpdatedUtc,
    int MessageCount);

internal static class ChatText
{
    public const int MaxTitleLength = 80;
    public const int MaxMessageCharacters = 8_000;

    public static string Cap(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= MaxMessageCharacters ? text : text[..MaxMessageCharacters];
    }

    public static string NormalizeTitle(string requested, string fallback)
    {
        string trimmed = (requested ?? "").Trim();
        if (trimmed.Length == 0) return NormalizeTitle(fallback, "");
        string collapsed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
        if (collapsed.Length <= MaxTitleLength) return collapsed;
        return collapsed[..MaxTitleLength].TrimEnd() + "\u2026";
    }

    public static string DeriveTitleFromFirstMessage(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return "New chat";
        string trimmed = prompt.Trim();
        string[] paragraphs = System.Text.RegularExpressions.Regex
            .Split(trimmed, @"(?:\r?\n){2,}");
        string joined = string.Join(' ', paragraphs);
        string collapsed = System.Text.RegularExpressions.Regex
            .Replace(joined, @"\s+", " ").Trim();
        if (collapsed.Length == 0) return "New chat";
        return NormalizeTitle(collapsed, "New chat");
    }
}

public sealed class AssistantChatSerializer
{
    public const int CurrentSchemaVersion = 1;

    public string Serialize(AssistantChat chat)
    {
        if (chat is null) throw new ArgumentNullException(nameof(chat));
        var dto = new PersistedChat
        {
            schemaVersion = CurrentSchemaVersion,
            id = chat.Id,
            title = chat.Title,
            createdUtc = chat.CreatedUtc,
            updatedUtc = chat.UpdatedUtc,
            messages = chat.Messages.Select(m => new PersistedMessage
            {
                role = m.Role.ToString().ToLowerInvariant(),
                text = m.Text,
                timestampUtc = m.TimestampUtc,
                toolName = m.ToolName,
                toolStatus = m.ToolStatus,
                toolRequiresApproval = m.ToolRequiresApproval,
            }).ToList(),
        };
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = false };
        return System.Text.Json.JsonSerializer.Serialize(dto, options);
    }

    public AssistantChat? Deserialize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };
            PersistedChat? dto = System.Text.Json.JsonSerializer.Deserialize<PersistedChat>(text, options);
            if (dto is null) return null;
            if (dto.schemaVersion > CurrentSchemaVersion) return null;
            if (string.IsNullOrEmpty(dto.id) || string.IsNullOrWhiteSpace(dto.title)) return null;
            var messages = new Queue<AssistantChatMessage>(AssistantChatStore.MaxMessagesPerChat);
            if (dto.messages is not null)
            {
                foreach (PersistedMessage? raw in dto.messages)
                {
                    if (raw is null) continue;
                    if (!Enum.TryParse<AssistantChatRole>(raw.role, true, out AssistantChatRole role))
                        role = AssistantChatRole.Assistant;
                    messages.Enqueue(new AssistantChatMessage(
                        role, ChatText.Cap(raw.text ?? ""), raw.timestampUtc, raw.toolName, raw.toolStatus, raw.toolRequiresApproval));
                    if (messages.Count > AssistantChatStore.MaxMessagesPerChat) messages.Dequeue();
                }
            }
            return new AssistantChat(dto.id, ChatText.NormalizeTitle(dto.title, "New chat"),
                dto.createdUtc, dto.updatedUtc, messages.ToArray());
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed class PersistedChat
    {
        [JsonPropertyName("schemaVersion")] public int schemaVersion { get; set; }
        [JsonPropertyName("id")] public string id { get; set; } = "";
        [JsonPropertyName("title")] public string title { get; set; } = "";
        [JsonPropertyName("createdUtc")] public DateTime createdUtc { get; set; }
        [JsonPropertyName("updatedUtc")] public DateTime updatedUtc { get; set; }
        [JsonPropertyName("messages")] public List<PersistedMessage>? messages { get; set; }
    }

    private sealed class PersistedMessage
    {
        [JsonPropertyName("role")] public string role { get; set; } = "";
        [JsonPropertyName("text")] public string text { get; set; } = "";
        [JsonPropertyName("timestampUtc")] public DateTime timestampUtc { get; set; }
        [JsonPropertyName("toolName")] public string? toolName { get; set; }
        [JsonPropertyName("toolStatus")] public string? toolStatus { get; set; }
        [JsonPropertyName("toolRequiresApproval")] public bool? toolRequiresApproval { get; set; }
    }
}
