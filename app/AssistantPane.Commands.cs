using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace BehaviourStudio.App;

internal sealed partial class AssistantPane
{
    internal Func<IReadOnlyList<AssistantCommand>>? CommandSource { get; set; }

    internal bool CommandsVisible => _commandPanel.IsVisible;
    internal IReadOnlyList<AssistantCommand> CommandChoices => _commandChoices;

    private string? TypedCommandName()
    {
        string text = _composer.Text ?? "";
        if (text.Length == 0 || text[0] != '/') return null;
        if (text.Contains(' ')) return null;
        return text[1..];
    }

    private void RefreshCommands()
    {
        IReadOnlyList<AssistantCommand>? source = CommandSource?.Invoke();
        string? typed = TypedCommandName();
        if (source is null || typed is null)
        {
            HideCommands();
            return;
        }

        _commandChoices.Clear();
        _commandRows.Clear();
        _commandPanel.Children.Clear();
        foreach (AssistantCommand command in source)
        {
            if (_commandChoices.Count >= 8) break;
            if (!command.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
            _commandChoices.Add(command);
        }
        if (_commandChoices.Count == 0)
        {
            HideCommands();
            return;
        }

        for (int index = 0; index < _commandChoices.Count; index++)
        {
            AssistantCommand command = _commandChoices[index];
            var label = new TextBlock
            {
                Text = "/" + command.Name,
                Foreground = Ux.CodeBrush,
                FontSize = Ux.FontSmall,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(Ux.Radius),
                Padding = new Thickness(6, 3, 6, 3),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        label,
                        new TextBlock
                        {
                            Text = command.Description,
                            Foreground = Ux.MutedBrush,
                            FontSize = Ux.FontSmall,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    },
                },
            };
            AssistantCommand captured = command;
            row.PointerPressed += (_, args) =>
            {
                args.Handled = true;
                AcceptCommand(captured);
            };
            _commandRows.Add(row);
            _commandPanel.Children.Add(row);
        }

        _commandChoice = 0;
        PaintCommands();
        _commandPanel.IsVisible = true;
    }

    private void AcceptCommand(AssistantCommand command)
    {
        _composer.Text = "/" + command.Name + " ";
        _composer.CaretIndex = (_composer.Text ?? "").Length;
        HideCommands();
    }

    private void PickCommand()
    {
        if (_commandChoice < 0 || _commandChoice >= _commandChoices.Count) return;
        AcceptCommand(_commandChoices[_commandChoice]);
    }

    private bool TypedCommandMatchesExactly()
    {
        string? typed = TypedCommandName();
        if (typed is null) return false;
        foreach (AssistantCommand command in _commandChoices)
            if (string.Equals(command.Name, typed, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void MoveCommand(int delta)
    {
        if (_commandChoices.Count == 0) return;
        _commandChoice = (_commandChoice + delta + _commandChoices.Count) % _commandChoices.Count;
        PaintCommands();
    }

    private void PaintCommands()
    {
        for (int index = 0; index < _commandRows.Count; index++)
            _commandRows[index].Background = index == _commandChoice
                ? Ux.CardHoverBrush
                : Brushes.Transparent;
    }

    private void HideCommands()
    {
        _commandPanel.IsVisible = false;
        _commandChoices.Clear();
        _commandRows.Clear();
        _commandPanel.Children.Clear();
        _commandChoice = -1;
    }

    public void OpenModelPicker()
    {
        _picker.IsVisible = true;
        _picker.FocusSearch();
        ModelControlOpened?.Invoke();
    }

    public void ShowQueue(IReadOnlyList<string> items)
    {
        _queuePanel.Children.Clear();
        for (int index = 0; index < items.Count; index++)
        {
            int captured = index;
            var text = new TextBlock
            {
                Text = "Queued: " + items[index],
                Foreground = Ux.MutedBrush,
                FontSize = Ux.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var remove = new Button
            {
                Content = "\u00D7",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Ux.MutedBrush,
                FontSize = 12,
                Padding = new Thickness(6, 0, 6, 0),
            };
            remove.Click += (_, _) => QueuedRemoved?.Invoke(captured);
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(text);
            _queuePanel.Children.Add(new Border
            {
                Background = Ux.CardBrush,
                BorderBrush = Ux.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Ux.Radius),
                Padding = new Thickness(8, 4, 4, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Child = row,
            });
        }
        _queuePanel.IsVisible = items.Count > 0;
    }

}
