using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("AssistantChatStore")]
public sealed class AssistantChatStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly string _previousFolder;

    public AssistantChatStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "bgs-assistant-store-" + Guid.NewGuid().ToString("N"));
        _previousFolder = AssistantChatStore.Folder;
        AssistantChatStore.Folder = _folder;
    }

    public void Dispose() => AssistantChatStore.Folder = _previousFolder;

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        AssistantChat original = AssistantChatStore.CreateEmpty();
        original = AssistantChatStore.AppendMessage(original, NewUser("Hello."));
        original = AssistantChatStore.AppendMessage(original, NewAssistant("World."));

        AssistantChatStore.Save(original);
        AssistantChat? loaded = AssistantChatStore.Load(original.Id);

        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded!.Id);
        Assert.Equal(original.Title, loaded.Title);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("Hello.", loaded.Messages[0].Text);
        Assert.Equal("World.", loaded.Messages[1].Text);
    }

    [Fact]
    public void MultipleChatsSurviveAcrossLoads()
    {
        AssistantChat a = AssistantChatStore.CreateEmpty();
        AssistantChat b = AssistantChatStore.CreateEmpty();
        Assert.NotEqual(a.Id, b.Id);

        AssistantChatStore.Save(AssistantChatStore.AppendMessage(a, NewUser("a-msg")));
        AssistantChatStore.Save(AssistantChatStore.AppendMessage(b, NewUser("b-msg")));

        AssistantChat? loadedA = AssistantChatStore.Load(a.Id);
        AssistantChat? loadedB = AssistantChatStore.Load(b.Id);

        Assert.NotNull(loadedA);
        Assert.NotNull(loadedB);
        Assert.Single(loadedA!.Messages);
        Assert.Single(loadedB!.Messages);
        Assert.Equal("a-msg", loadedA.Messages[0].Text);
        Assert.Equal("b-msg", loadedB.Messages[0].Text);
    }

    [Fact]
    public void EnumerationReturnsSavedChatsSortedByUpdatedUtcDescending()
    {
        AssistantChat older = AssistantChatStore.CreateEmpty();
        AssistantChat newer = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(older);
        AssistantChatStore.Save(newer.WithMessages(newer.Messages, DateTime.UtcNow.AddMinutes(5)));
        File.SetLastWriteTimeUtc(Path.Combine(_folder, older.Id + ".json"), DateTime.UtcNow.AddHours(1));

        IReadOnlyList<AssistantChatSummary> found = AssistantChatStore.All();
        Assert.Equal(2, found.Count);
        Assert.Equal(newer.Id, found[0].Id);
    }

    [Fact]
    public void RenamePersistsImmediately()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);

        bool ok = AssistantChatStore.Rename(chat.Id, "Renamed title");
        Assert.True(ok);

        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);
        Assert.Equal("Renamed title", loaded!.Title);
    }

    [Fact]
    public void RenameRejectsBlankTitleAndPreservesPrevious()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);

        AssistantChatStore.Rename(chat.Id, "   ");
        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);
        Assert.NotEqual("   ", loaded!.Title);
        Assert.NotEqual("", loaded.Title);
    }

    [Fact]
    public void DeleteRemovesFromListAndFromDisk()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);
        Assert.True(AssistantChatStore.Exists(chat.Id));

        bool ok = AssistantChatStore.Delete(chat.Id);
        Assert.True(ok);
        Assert.False(AssistantChatStore.Exists(chat.Id));
        Assert.Empty(AssistantChatStore.All());
    }

    [Fact]
    public void SaveUsesAtomicReplaceWithTempFile()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);
        string finalPath = Path.Combine(_folder, chat.Id + ".json");
        Assert.True(File.Exists(finalPath));

        foreach (string leftover in Directory.GetFiles(_folder, "*.tmp"))
            Assert.Fail($"Staging file left behind: {leftover}");
    }

    [Fact]
    public void SaveAppliesMaxMessagesPerChatTrim()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        for (int i = 0; i < AssistantChatStore.MaxMessagesPerChat + 50; i++)
        {
            chat = AssistantChatStore.AppendMessage(chat, NewUser("m" + i));
        }
        AssistantChatStore.Save(chat);

        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);
        Assert.Equal(AssistantChatStore.MaxMessagesPerChat, loaded!.Messages.Count);
        Assert.Equal("m50", loaded.Messages[0].Text);
    }

    [Fact]
    public void CapEnforcesMaxMessageCharacters()
    {
        string huge = new string('x', ChatText.MaxMessageCharacters + 200);
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = AssistantChatStore.AppendMessage(chat, NewAssistant(huge));

        string capped = chat.Messages[0].Text;
        Assert.Equal(ChatText.MaxMessageCharacters, capped.Length);
    }

    [Fact]
    public void ReplayReturnsChronologicalUserAssistantPairsBoundedByMaxReplayTurns()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        DateTime stamp = DateTime.UtcNow;
        int pairs = AssistantChatStore.MaxReplayTurns + 4;
        for (int i = 0; i < pairs; i++)
        {
            chat = AssistantChatStore.AppendMessage(chat,
                new AssistantChatMessage(AssistantChatRole.User, "u" + i, stamp.AddMinutes(i)));
            chat = AssistantChatStore.AppendMessage(chat,
                new AssistantChatMessage(AssistantChatRole.Assistant, "a" + i, stamp.AddMinutes(i).AddSeconds(1)));
        }
        AssistantChatStore.Save(chat);

        IReadOnlyList<AssistantChatMessage> replay = AssistantChatStore.ReplayHistory(chat);

        Assert.Equal(AssistantChatStore.MaxReplayMessages, replay.Count);
        Assert.Equal(AssistantChatStore.MaxReplayTurns * 2, replay.Count);
        Assert.True(replay.First().TimestampUtc < replay.Last().TimestampUtc);
        Assert.DoesNotContain(replay, m => m.Role == AssistantChatRole.Tool);

        int firstRetained = pairs - AssistantChatStore.MaxReplayTurns;
        Assert.Equal("u" + firstRetained, replay[0].Text);
        Assert.Equal("a" + firstRetained, replay[1].Text);
        Assert.Equal("u" + (pairs - 1), replay[^2].Text);
        Assert.Equal("a" + (pairs - 1), replay[^1].Text);
        Assert.DoesNotContain(replay, m => m.Text == "u" + (firstRetained - 1));
    }

    [Fact]
    public void ReplaySkipsToolEntriesFromHistory()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = AssistantChatStore.AppendMessage(chat, NewUser("first"));
        chat = AssistantChatStore.AppendMessage(chat,
            new AssistantChatMessage(AssistantChatRole.Tool, "ignored", DateTime.UtcNow,
                ToolName: "bgs.set_clip_animation", ToolStatus: "previewed", ToolRequiresApproval: true));
        chat = AssistantChatStore.AppendMessage(chat, NewAssistant("reply"));

        IReadOnlyList<AssistantChatMessage> replay = AssistantChatStore.ReplayHistory(chat);
        Assert.Equal(2, replay.Count);
        Assert.DoesNotContain(replay, m => m.Role == AssistantChatRole.Tool);
    }

    [Fact]
    public void LoadReturnsNullForCorruptJson()
    {
        Directory.CreateDirectory(_folder);
        string bad = Path.Combine(_folder, "abc123.json");
        File.WriteAllText(bad, "this is { not valid json");
        AssistantChat? loaded = AssistantChatStore.Load("abc123");
        Assert.Null(loaded);
    }

    [Fact]
    public void CorruptChatDoesNotBreakEnumeration()
    {
        AssistantChat good = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(good);

        string bad = Path.Combine(_folder, "deadbeef.json");
        File.WriteAllText(bad, "{ \"unrelated\": true }");

        IReadOnlyList<AssistantChatSummary> all = AssistantChatStore.All();
        Assert.Single(all);
        Assert.Equal(good.Id, all[0].Id);
    }

    [Fact]
    public void PersistedOversizedMessageTextIsBoundedOnLoad()
    {
        DateTime now = DateTime.UtcNow;
        AssistantChat chat = new("boundedtext01", "New chat", now, now, new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, new string('x', 20_000), now),
        });
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, chat.Id + ".json"), AssistantChatStore.Serialize(chat));

        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Messages);
        Assert.Equal(ChatText.MaxMessageCharacters, loaded.Messages[0].Text.Length);
    }

    [Fact]
    public void PersistedOversizedMessageCountIsBoundedOnLoad()
    {
        DateTime now = DateTime.UtcNow;
        var messages = Enumerable.Range(0, AssistantChatStore.MaxMessagesPerChat + 60)
            .Select(i => new AssistantChatMessage(AssistantChatRole.User, "m" + i, now))
            .ToArray();
        AssistantChat chat = new("boundedcount01", "New chat", now, now, messages);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, chat.Id + ".json"), AssistantChatStore.Serialize(chat));

        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.NotNull(loaded);
        Assert.Equal(AssistantChatStore.MaxMessagesPerChat, loaded!.Messages.Count);
        Assert.Equal("m60", loaded.Messages[0].Text);
        Assert.Equal("m259", loaded.Messages[loaded.Messages.Count - 1].Text);
    }

    [Fact]
    public void PersistedOversizedTitleIsBoundedOnLoad()
    {
        DateTime now = DateTime.UtcNow;
        AssistantChat chat = new("boundedtitle01", new string('t', 200), now, now,
            Array.Empty<AssistantChatMessage>());
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, chat.Id + ".json"), AssistantChatStore.Serialize(chat));

        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.NotNull(loaded);
        Assert.True(loaded!.Title.Length <= ChatText.MaxTitleLength + 1);
    }

    [Fact]
    public void SaveRedactsBearerAuthorizationBeforePersisting()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, "authorization: bearer abcdefghijklmnop", DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChat saved = AssistantChatStore.Save(chat);
        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.Contains(AssistantChatStore.RedactedPlaceholder, saved.Messages[0].Text);
        Assert.NotNull(loaded);
        Assert.DoesNotContain("abcdefghijklmnop", loaded!.Messages[0].Text);
        Assert.Contains(AssistantChatStore.RedactedPlaceholder, loaded.Messages[0].Text);
    }

    [Fact]
    public void SaveRedactsRefreshTokenBeforePersisting()
    {
        string secret = new string('a', 32);
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, "refresh_token=" + secret, DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChatStore.Save(chat);
        string text = File.ReadAllText(Path.Combine(_folder, chat.Id + ".json"));
        Assert.DoesNotContain(secret, text);
        Assert.Contains(AssistantChatStore.RedactedPlaceholder, text);
    }

    [Fact]
    public void SaveRedactsApiKeyShapeBeforePersisting()
    {
        string secret = "sk-projects-" + new string('a', 16);
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, secret, DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChatStore.Save(chat);
        string text = File.ReadAllText(Path.Combine(_folder, chat.Id + ".json"));
        Assert.DoesNotContain(secret, text);
        Assert.Contains(AssistantChatStore.RedactedPlaceholder, text);
    }

    [Fact]
    public void RedactKeepsUnrelatedMessagesIntact()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, "my token is sk-abcdefghijklmnop and nothing else", DateTime.UtcNow),
            new AssistantChatMessage(AssistantChatRole.Assistant, "noted, keep it private", DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChat redacted = AssistantChatStore.Redact(chat);

        Assert.Equal(2, redacted.Messages.Count);
        Assert.StartsWith("my token is", redacted.Messages[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("and nothing else", redacted.Messages[0].Text, StringComparison.Ordinal);
        Assert.Equal("noted, keep it private", redacted.Messages[1].Text);
    }

    [Fact]
    public void NormalAuthJsonMentionIsSaved()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, "see auth.json for token", DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChatStore.Save(chat);
        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Messages);
        Assert.Equal("see auth.json for token", loaded.Messages[0].Text);
    }

    [Fact]
    public void NormalAccessTokenMentionIsSaved()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = chat.WithMessages(new[]
        {
            new AssistantChatMessage(AssistantChatRole.User, "How do I refresh an access_token without logging in?", DateTime.UtcNow),
        }, DateTime.UtcNow);

        AssistantChatStore.Save(chat);
        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Messages);
        Assert.Equal("How do I refresh an access_token without logging in?", loaded.Messages[0].Text);
    }

    [Fact]
    public void PersistenceScanDoesNotFlagNormalMessageContent()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        chat = AssistantChatStore.AppendMessage(chat, NewUser("How do I rename a clip?"));
        chat = AssistantChatStore.AppendMessage(chat, NewAssistant("Use the tools tab."));
        AssistantChatStore.Save(chat);
        AssistantChat? loaded = AssistantChatStore.Load(chat.Id);
        Assert.NotNull(loaded);
    }

    [Fact]
    public void PathTraversalInTitleCannotEscapeFolder()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);

        AssistantChatStore.Rename(chat.Id, "../../../etc/passwd");
        string[] files = Directory.GetFiles(_folder);
        Assert.All(files, file => Assert.StartsWith(_folder, file));
    }

    [Fact]
    public void FilePathDerivedFromIdNotTitle()
    {
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        AssistantChatStore.Save(chat);
        string expected = Path.Combine(_folder, chat.Id + ".json");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public void IdMustBeSanitized()
    {
        Assert.False(AssistantChatStore.IsValidId(""));
        Assert.False(AssistantChatStore.IsValidId("../etc/passwd"));
        Assert.False(AssistantChatStore.IsValidId("with space"));
        Assert.False(AssistantChatStore.IsValidId("with/slash"));
        Assert.True(AssistantChatStore.IsValidId("deadbeefcafebabe"));
    }

    [Fact]
    public void TitleDerivationNormalizesWhitespace()
    {
        string title = ChatText.DeriveTitleFromFirstMessage("Hello\n\n world   friend");
        Assert.Equal("Hello world friend", title);
    }

    [Fact]
    public void TitleDerivationCapsLengthAndAddsEllipsis()
    {
        string raw = new string('a', ChatText.MaxTitleLength + 10);
        string title = ChatText.DeriveTitleFromFirstMessage(raw);
        Assert.True(title.Length <= ChatText.MaxTitleLength + 1);
        Assert.EndsWith("\u2026", title);
    }

    [Fact]
    public void CreateIdIsUnique()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++) ids.Add(AssistantChatStore.CreateId());
        Assert.Equal(200, ids.Count);
    }

    [Fact]
    public async Task ConcurrentSavesForDifferentChatsDoNotCorruptEachOther()
    {
        var chats = Enumerable.Range(0, 8)
            .Select(i => AssistantChatStore.AppendMessage(AssistantChatStore.CreateEmpty(), NewUser("u" + i)))
            .ToList();

        var tasks = chats.Select(chat => Task.Run(() => AssistantChatStore.Save(chat))).ToArray();
        await Task.WhenAll(tasks);

        foreach (AssistantChat chat in chats)
        {
            AssistantChat? loaded = AssistantChatStore.Load(chat.Id);
            Assert.NotNull(loaded);
            Assert.Single(loaded!.Messages);
        }
    }

    private static AssistantChatMessage NewUser(string text) =>
        new(AssistantChatRole.User, text, DateTime.UtcNow);

    private static AssistantChatMessage NewAssistant(string text) =>
        new(AssistantChatRole.Assistant, text, DateTime.UtcNow);
}
