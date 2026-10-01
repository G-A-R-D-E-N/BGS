using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BehaviourStudio.App;

public sealed class AssistantConversationEntry : IDisposable
{
    internal AssistantConversationEntry(AssistantChat chat)
    {
        Chat = chat;
    }

    public AssistantChat Chat { get; internal set; }
    public IAssistantSession? Session { get; internal set; }
    public bool PendingApproval { get; set; }
    public Func<bool>? HasPendingApproval { get; internal set; }
    internal bool SessionPrimed { get; set; }
    internal bool Deleted { get; set; }
    internal object SaveGate { get; } = new();
    internal long SessionGeneration => Interlocked.Read(ref _sessionGeneration);
    internal void InvalidateSession() => Interlocked.Increment(ref _sessionGeneration);
    private long _sessionGeneration;

    public void ClearPendingApproval() => PendingApproval = false;
    public void MarkPendingApproval() => PendingApproval = true;
    public bool ApprovalActive =>
        !Deleted &&
        (PendingApproval ||
        (HasPendingApproval is { } probe && probe()));

    public void Dispose()
    {
        var session = Session;
        Session = null;
        session?.Dispose();
    }
}

public sealed class AssistantConversationList : IDisposable
{
    private readonly List<AssistantConversationEntry> _entries = new();
    private int _activeIndex = -1;

    public AssistantConversationList() { }

    public event Action<int>? SelectionChanged;
    public event Action? StructureChanged;

    public IReadOnlyList<AssistantConversationEntry> Entries => _entries;
    public AssistantConversationEntry? Active =>
        _activeIndex >= 0 && _activeIndex < _entries.Count ? _entries[_activeIndex] : null;
    public int ActiveIndex => _activeIndex;
    public bool IsBusy { get; set; }
    public string? ActiveChatId => Active?.Chat.Id;

    public void Add(AssistantChat chat, int? selectIndex = null)
    {
        if (!AssistantChatStore.IsValidId(chat.Id))
            throw new ArgumentException("Conversation id is not a valid storage key.", nameof(chat));
        if (Count >= AssistantChatStore.MaxConversations)
            throw new InvalidOperationException(
                $"BGS only retains {AssistantChatStore.MaxConversations} conversations.");
        var entry = new AssistantConversationEntry(chat);
        _entries.Add(entry);
        StructureChanged?.Invoke();
        if (selectIndex is int i)
        {
            _activeIndex = i;
            SelectionChanged?.Invoke(_activeIndex);
        }
        else if (_activeIndex < 0)
        {
            _activeIndex = 0;
            SelectionChanged?.Invoke(_activeIndex);
        }
    }

    public int Count => _entries.Count;

    public int IndexOf(string chatId)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (string.Equals(_entries[i].Chat.Id, chatId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    public AssistantConversationEntry? Select(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;
        if (IsBusy) return null;
        AssistantConversationEntry? previous = Active;
        _activeIndex = index;
        SelectionChanged?.Invoke(_activeIndex);
        return _entries[index];
    }

    public bool TrySetActiveById(string? chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return false;
        int idx = IndexOf(chatId);
        if (idx < 0) return false;
        return Select(idx) != null;
    }

    public AssistantConversationEntry? NewChat()
    {
        if (IsBusy) return null;
        if (Count >= AssistantChatStore.MaxConversations) return null;
        AssistantChat chat = AssistantChatStore.CreateEmpty();
        try
        {
            chat = AssistantChatStore.Save(chat);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
        _entries.Add(new AssistantConversationEntry(chat));
        StructureChanged?.Invoke();
        _activeIndex = _entries.Count - 1;
        SelectionChanged?.Invoke(_activeIndex);
        return Active;
    }

    public bool Rename(int index, string newTitle)
    {
        if (index < 0 || index >= _entries.Count) return false;
        var entry = _entries[index];
        AssistantChat renamed = entry.Chat.WithTitle(newTitle);
        try
        {
            renamed = AssistantChatStore.Save(renamed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
        entry.Chat = renamed;
        StructureChanged?.Invoke();
        return true;
    }

    public void ClearSessions()
    {
        foreach (AssistantConversationEntry entry in _entries)
        {
            entry.InvalidateSession();
            entry.ClearPendingApproval();
            entry.Session?.Dispose();
            entry.Session = null;
            entry.SessionPrimed = false;
        }
    }

    public AssistantConversationEntry? Delete(int index)
    {
        if (index < 0 || index >= _entries.Count) return null;
        var entry = _entries[index];

        lock (entry.SaveGate)
        {
            entry.Deleted = true;
            entry.ClearPendingApproval();

            bool removed;
            try
            {
                removed = AssistantChatStore.Delete(entry.Chat.Id);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                removed = false;
            }

            if (!removed)
            {
                entry.Deleted = false;
                return null;
            }
        }

        entry.Dispose();
        _entries.RemoveAt(index);

        bool autoSelect = index == _activeIndex;
        bool shift = index < _activeIndex;
        bool repaired = autoSelect || shift;

        if (_entries.Count == 0)
        {
            AssistantChat fresh = AssistantChatStore.CreateEmpty();
            try
            {
                fresh = AssistantChatStore.Save(fresh);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
            }
            _entries.Add(new AssistantConversationEntry(fresh));
            _activeIndex = 0;
            repaired = true;
        }
        else if (autoSelect)
        {
            _activeIndex = Math.Min(index, _entries.Count - 1);
        }
        else if (shift)
        {
            _activeIndex--;
        }

        StructureChanged?.Invoke();
        if (repaired) SelectionChanged?.Invoke(_activeIndex);
        return Active;
    }

    public IReadOnlyList<AssistantChatSummary> Summaries() =>
        _entries
            .Select(e => new AssistantChatSummary(e.Chat.Id, e.Chat.Title, e.Chat.UpdatedUtc, e.Chat.Messages.Count))
            .OrderByDescending(summary => summary.UpdatedUtc)
            .ToList();

    public void Dispose()
    {
        foreach (var entry in _entries) entry.Dispose();
        _entries.Clear();
        _activeIndex = -1;
    }
}
