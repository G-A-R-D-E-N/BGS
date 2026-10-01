using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

public sealed class AssistantConversationSidebar : Border
{
    public event Action? NewChatRequested;
    public event Action<string>? ConversationSelected;
    public event Action<string>? DeleteRequested;
    public event Action<string, string>? RenameCommitted;

    private readonly StackPanel _items = new() { Spacing = 4 };
    private readonly TextBlock _empty = new()
    {
        Text = "No saved chats yet.\nStart one above.",
        Foreground = Ux.MutedBrush,
        FontSize = Ux.FontSmall,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 6, 0, 0),
    };
    private readonly Dictionary<string, TextBox> _editingEditors = new(StringComparer.Ordinal);

    public AssistantConversationSidebar()
    {
        Background = Ux.RailBrush;
        BorderBrush = Ux.BorderBrush;
        BorderThickness = new Thickness(0, 0, 1, 0);
        Padding = new Thickness(10, 12, 10, 12);

        var header = new TextBlock
        {
            Text = "Conversations",
            Foreground = Ux.TitleBrush,
            FontSize = Ux.FontSmall,
            FontWeight = FontWeight.SemiBold,
        };

        var newChat = Ux.Secondary("+ New chat");
        newChat.HorizontalAlignment = HorizontalAlignment.Stretch;
        newChat.Margin = new Thickness(0, 8, 0, 0);
        newChat.Click += (_, _) => NewChatRequested?.Invoke();

        var hint = new TextBlock
        {
            Text = "Each chat stays isolated.",
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontSmall,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(newChat);
        stack.Children.Add(hint);
        stack.Children.Add(_empty);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _items,
            Margin = new Thickness(0, 10, 0, 0),
        };
        stack.Children.Add(scroll);
        Child = stack;
    }

    public void Bind(IReadOnlyList<AssistantChatSummary> chats, string? activeChatId)
    {
        _items.Children.Clear();
        _editingEditors.Clear();
        _empty.IsVisible = chats.Count == 0;
        foreach (AssistantChatSummary summary in chats)
            _items.Children.Add(BuildRow(summary, activeChatId));
    }

    private Border BuildRow(AssistantChatSummary summary, string? activeChatId)
    {
        bool active = activeChatId is not null && string.Equals(activeChatId, summary.Id, StringComparison.Ordinal);

        var title = new TextBlock
        {
            Text = summary.Title,
            Foreground = active ? Ux.TitleBrush : Ux.MetaBrush,
            FontSize = Ux.FontBody,
            FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
        };
        var meta = new TextBlock
        {
            Text = Describe(summary),
            Foreground = Ux.MutedBrush,
            FontSize = Ux.FontSmall,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var open = new Button
        {
            Content = new StackPanel { Spacing = 1, Children = { title, meta } },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 2, 2, 2),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        open.Click += (_, _) => ConversationSelected?.Invoke(summary.Id);

        var editor = new TextBox
        {
            Text = summary.Title,
            IsVisible = false,
            FontSize = Ux.FontBody,
            MaxLength = ChatText.MaxTitleLength,
            Margin = new Thickness(0),
        };
        _editingEditors[summary.Id] = editor;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsVisible = active,
            Margin = new Thickness(0, 2, 0, 0),
        };
        actions.Children.Add(IconAction("\u270E", "Rename this chat", () =>
        {
            open.IsVisible = false;
            editor.IsVisible = true;
            editor.Text = summary.Title;
            editor.Focus();
            editor.SelectAll();
        }));
        actions.Children.Add(IconAction("\u2715", "Delete this chat", () => DeleteRequested?.Invoke(summary.Id)));

        editor.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) CommitEditor(editor, summary.Id, open);
            else if (args.Key == Key.Escape) CancelEditor(editor, open, summary.Title);
        };
        editor.LostFocus += (_, _) => CommitEditor(editor, summary.Id, open);

        var content = new StackPanel { Spacing = 2, Children = { open, editor, actions } };

        return new Border
        {
            Background = active ? Ux.CardBrush : Brushes.Transparent,
            BorderBrush = active ? Ux.AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(Ux.Radius),
            Padding = new Thickness(8, 6, 8, 6),
            Child = content,
        };
    }

    private static Button IconAction(string glyph, string tip, Action invoke)
    {
        var button = Ux.Secondary(glyph);
        button.FontSize = 12;
        button.Padding = new Thickness(8, 2, 8, 2);
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => invoke();
        return button;
    }

    internal static string Describe(AssistantChatSummary summary)
    {
        string messages = summary.MessageCount == 1 ? "1 message" : summary.MessageCount + " messages";
        return messages + " \u00B7 " + Relative(summary.UpdatedUtc);
    }

    internal static string Relative(DateTime updatedUtc)
    {
        TimeSpan age = DateTime.UtcNow - updatedUtc;
        if (age < TimeSpan.Zero) return "just now";
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return (int)age.TotalMinutes + "m ago";
        if (age < TimeSpan.FromDays(1)) return (int)age.TotalHours + "h ago";
        if (age < TimeSpan.FromDays(7)) return (int)age.TotalDays + "d ago";
        return updatedUtc.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture);
    }

    private void CommitEditor(TextBox editor, string chatId, Button open)
    {
        if (!editor.IsVisible) return;
        string text = (editor.Text ?? "").Trim();
        editor.IsVisible = false;
        open.IsVisible = true;
        if (text.Length == 0) return;
        RenameCommitted?.Invoke(chatId, text);
    }

    private static void CancelEditor(TextBox editor, Button open, string previous)
    {
        editor.Text = previous;
        editor.IsVisible = false;
        open.IsVisible = true;
    }
}
