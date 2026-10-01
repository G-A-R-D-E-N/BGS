using System;
using System.Collections.Generic;
using System.IO;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("AssistantChatStore")]
public sealed class AssistantConversationListTests : IDisposable
{
    private readonly string _folder;
    private readonly string _previousFolder;

    public AssistantConversationListTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "bgs-assistant-list-" + Guid.NewGuid().ToString("N"));
        _previousFolder = AssistantChatStore.Folder;
        AssistantChatStore.Folder = _folder;
    }

    public void Dispose() => AssistantChatStore.Folder = _previousFolder;

    [Fact]
    public void DeletingTheOnlyChatKeepsActiveReadableDuringStructureChanged()
    {
        var list = new AssistantConversationList();
        list.Add(SavedChat());
        var seen = new List<string?>();
        list.StructureChanged += () => seen.Add(list.ActiveChatId);

        list.Delete(0);

        Assert.NotEmpty(seen);
        Assert.All(seen, id => Assert.Equal(list.ActiveChatId, id));
        Assert.NotNull(list.ActiveChatId);
    }

    [Fact]
    public void DeletingTheActiveLastChatKeepsActiveReadableDuringStructureChanged()
    {
        var list = new AssistantConversationList();
        AssistantChat first = SavedChat();
        list.Add(first);
        list.Add(SavedChat());
        list.Select(1);
        var seen = new List<string?>();
        list.StructureChanged += () => seen.Add(list.ActiveChatId);

        list.Delete(1);

        Assert.NotEmpty(seen);
        Assert.All(seen, id => Assert.Equal(list.ActiveChatId, id));
        Assert.Equal(first.Id, list.ActiveChatId);
    }

    [Fact]
    public void DeletingAnEarlierChatNeverExposesTheWrongActiveChatDuringStructureChanged()
    {
        var list = new AssistantConversationList();
        list.Add(SavedChat());
        AssistantChat middle = SavedChat();
        list.Add(middle);
        list.Add(SavedChat());
        list.Select(1);
        var seen = new List<string?>();
        list.StructureChanged += () => seen.Add(list.ActiveChatId);

        list.Delete(0);

        Assert.NotEmpty(seen);
        Assert.All(seen, id => Assert.Equal(middle.Id, id));
        Assert.Equal(middle.Id, list.ActiveChatId);
    }

    private static AssistantChat SavedChat() =>
        AssistantChatStore.Save(AssistantChatStore.CreateEmpty());
}
