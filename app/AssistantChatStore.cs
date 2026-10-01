using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BehaviourStudio.App;

public static class AssistantChatStore
{
    public const int MaxConversations = 10;
    public const int MaxMessagesPerChat = 200;
    public const int MaxPromptCharacters = 4_000;
    public const int MaxTotalFileSizeBytes = 1_048_576;
    public const int MaxReplayTurns = 8;
    public const int MaxReplayMessages = MaxReplayTurns * 2;
    public const long MaxChatBytes = 1_048_576L;

    private static readonly object WriteGate = new();
    private static readonly TimeSpan WriteLockTimeout = TimeSpan.FromSeconds(5);
    private const int MinimumCredentialMaterialLength = 16;
    private const int MinimumJwtHeaderBodyLength = 8;
    private const int MinimumJwtSignatureLength = 4;
    private static readonly Regex[] ForbiddenCredentialPatterns =
    {
        new($"authorization\\s*[:=]\\s*bearer\\s+[A-Za-z0-9._-]{{{MinimumCredentialMaterialLength},}}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new($"\\bsk-[A-Za-z0-9_-]{{{MinimumCredentialMaterialLength},}}", RegexOptions.Compiled),
        new($"\\beyJ[A-Za-z0-9_-]{{{MinimumJwtHeaderBodyLength},}}\\.[A-Za-z0-9_-]{{{MinimumJwtHeaderBodyLength},}}\\.[A-Za-z0-9_-]{{{MinimumJwtSignatureLength},}}",
            RegexOptions.Compiled),
        new($"\\b[\"']?(?:access|refresh|id)[_-]?token[\"']?\\s*[:=]\\s*[\"']?[A-Za-z0-9._-]{{{MinimumCredentialMaterialLength},}}[\"']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BehaviourGraphStudio", "assistant", "chats");

    public static string Folder { get; set; } = DefaultFolder;

    internal static Func<string, bool>? DeleteForTest { get; set; }

    internal static Action? SaveFailureForTest { get; set; }

    public const string RedactedPlaceholder = "[redacted]";

    public static string CreateId()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static AssistantChat CreateEmpty()
    {
        DateTime now = DateTime.UtcNow;
        return new AssistantChat(CreateId(), "New chat", now, now, Array.Empty<AssistantChatMessage>());
    }

    public static AssistantChat CreateForFirstMessage(string firstUserMessage)
    {
        DateTime now = DateTime.UtcNow;
        string title = ChatText.DeriveTitleFromFirstMessage(firstUserMessage);
        AssistantChat empty = CreateEmpty();
        return new AssistantChat(empty.Id, title, empty.CreatedUtc, empty.UpdatedUtc,
            new List<AssistantChatMessage>
            {
                new(AssistantChatRole.User, ChatText.Cap(firstUserMessage.Trim()), now),
            });
    }

    public static string Serialize(AssistantChat chat)
    {
        var serializer = new AssistantChatSerializer();
        return serializer.Serialize(chat);
    }

    public static AssistantChat? Deserialize(string text) =>
        new AssistantChatSerializer().Deserialize(text);

    public static AssistantChat Save(AssistantChat chat)
    {
        if (chat is null) throw new ArgumentNullException(nameof(chat));
        if (!IsValidId(chat.Id))
            throw new ArgumentException("The chat id is not a valid storage key.", nameof(chat));
        if (Encoding.UTF8.GetByteCount(chat.Title) > ChatText.MaxTitleLength * 4)
            throw new ArgumentException("The chat title is too long.", nameof(chat));
        AssistantChat trimmed = Redact(Trim(chat));
        string text = Serialize(trimmed);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > MaxChatBytes)
            throw new InvalidOperationException("The chat is too large to persist.");

        ScanForForbiddenCredentials(text);

        EnsureFolder();
        string finalPath = FilePathFor(chat.Id);

        lock (WriteGate)
        {
            using var mutex = new Mutex(false, WriteMutexName(finalPath));
            bool acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(WriteLockTimeout); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    throw new IOException("Another Behaviour Graph Studio process is saving the chat history. Try again.");

                string tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                bool staged = false;
                try
                {
                    using (var stream = AssistantPrivateFiles.CreateFile(tempPath))
                    {
                        staged = true;
                        SaveFailureForTest?.Invoke();
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(tempPath, finalPath, overwrite: true);
                }
                catch
                {
                    if (staged) TryDelete(tempPath);
                    throw;
                }
            }
            finally
            {
                if (acquired) mutex.ReleaseMutex();
            }
        }

        return trimmed;
    }

    public static AssistantChat? Load(string id)
    {
        if (!IsValidId(id)) return null;
        string path = FilePathFor(id);
        if (!File.Exists(path)) return null;
        try
        {
            FileInfo info = new(path);
            if (info.Length > MaxChatBytes) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            return Deserialize(text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<AssistantChatSummary> All()
    {
        if (!Directory.Exists(Folder)) return Array.Empty<AssistantChatSummary>();
        var found = new List<AssistantChatSummary>();
        foreach (string path in Directory.GetFiles(Folder, "*.json"))
        {
            try
            {
                FileInfo info = new(path);
                if (info.Length > MaxChatBytes) continue;
                string text = File.ReadAllText(path, Encoding.UTF8);
                AssistantChat? chat = Deserialize(text);
                if (chat is null) continue;
                found.Add(new AssistantChatSummary(chat.Id, chat.Title, chat.UpdatedUtc, chat.Messages.Count));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
        return found.OrderByDescending(chat => chat.UpdatedUtc).ToList();
    }

    public static bool Delete(string id)
    {
        if (!IsValidId(id)) return false;
        if (DeleteForTest is { } hook) return hook(id);
        string path = FilePathFor(id);
        return TryDelete(path);
    }

    public static bool Rename(string id, string newTitle)
    {
        AssistantChat? chat = Load(id);
        if (chat is null) return false;
        AssistantChat renamed = chat.WithTitle(newTitle);
        Save(renamed);
        return true;
    }

    public static bool Exists(string id) =>
        IsValidId(id) && File.Exists(FilePathFor(id));

    public static AssistantChat AppendMessage(AssistantChat chat, AssistantChatMessage message)
    {
        var messages = chat.Messages.ToList();
        messages.Add(message.Trimmed());
        return chat.WithMessages(messages, message.TimestampUtc);
    }

    public static AssistantChat TrimToMaxMessages(AssistantChat chat)
    {
        if (chat.Messages.Count <= MaxMessagesPerChat) return chat;
        int skip = chat.Messages.Count - MaxMessagesPerChat;
        var trimmed = new List<AssistantChatMessage>(MaxMessagesPerChat);
        for (int i = skip; i < chat.Messages.Count; i++) trimmed.Add(chat.Messages[i]);
        return chat.WithMessages(trimmed, DateTime.UtcNow);
    }

    public static IReadOnlyList<AssistantChatMessage> ReplayHistory(AssistantChat chat)
    {
        var turns = new List<AssistantChatMessage>();
        int turnsAdded = 0;
        for (int i = chat.Messages.Count - 1; i >= 0 && turnsAdded < MaxReplayMessages; i--)
        {
            AssistantChatMessage message = chat.Messages[i];
            if (message.Role == AssistantChatRole.Tool) continue;
            turns.Insert(0, message);
            if (message.Role == AssistantChatRole.User || message.Role == AssistantChatRole.Assistant)
                turnsAdded++;
        }
        return turns;
    }

    public static bool ContainsForbiddenCredential(string content) =>
        ForbiddenCredentialPatterns.Any(pattern => pattern.IsMatch(content));

    public static AssistantChat Redact(AssistantChat chat)
    {
        bool changed = false;

        string title = chat.Title.Length == 0 ? chat.Title : RedactText(chat.Title);
        if (!string.Equals(title, chat.Title, StringComparison.Ordinal)) changed = true;

        var messages = new List<AssistantChatMessage>(chat.Messages.Count);
        foreach (AssistantChatMessage message in chat.Messages)
        {
            string redacted = message.Text.Length == 0 ? message.Text : RedactText(message.Text);
            if (!string.Equals(redacted, message.Text, StringComparison.Ordinal))
            {
                changed = true;
                messages.Add(message with { Text = redacted });
            }
            else
            {
                messages.Add(message);
            }
        }

        return changed
            ? new AssistantChat(chat.Id, title, chat.CreatedUtc, chat.UpdatedUtc, messages)
            : chat;
    }

    public static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string redacted = text;
        foreach (Regex pattern in ForbiddenCredentialPatterns)
            redacted = pattern.Replace(redacted, RedactedPlaceholder);
        return ChatText.Cap(redacted);
    }

    internal static void ScanForForbiddenCredentials(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        if (ContainsForbiddenCredential(content))
            throw new InvalidOperationException(
                "Refusing to save chat: the serialized content contained a forbidden credential pattern.");
    }

    internal static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool digit = c >= '0' && c <= '9';
            if (!letter && !digit) return false;
        }
        return true;
    }

    internal static string FilePathFor(string id) => Path.Combine(Folder, id + ".json");

    internal static void EnsureFolder()
    {
        AssistantPrivateFiles.EnsureDirectory(Folder);
        VerifyPathContained(FilePathFor(CreateId()));
    }

    internal static void VerifyPathContained(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string fullFolder = Path.GetFullPath(Folder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool ok = string.Equals(fullPath, fullFolder, comparison) ||
                  fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, comparison) ||
                  fullPath.StartsWith(fullFolder + Path.AltDirectorySeparatorChar, comparison);
        if (!ok)
            throw new InvalidOperationException("The assistant chat path must stay inside the assistant folder.");
    }

    private static AssistantChat Trim(AssistantChat chat)
    {
        IReadOnlyList<AssistantChatMessage> trimmed = chat.Messages
            .TakeLast(MaxMessagesPerChat)
            .Select(m => m.Trimmed())
            .ToList();
        return chat.WithMessages(trimmed, chat.UpdatedUtc);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string WriteMutexName(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return "BehaviourGraphStudio.AssistantChat." + hash;
    }
}
