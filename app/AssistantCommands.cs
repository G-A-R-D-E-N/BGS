using System;
using System.Collections.Generic;

namespace BehaviourStudio.App;

public sealed record AssistantCommand(string Name, string Description);

public static class AssistantCommands
{
    public static readonly IReadOnlyList<AssistantCommand> All = new[]
    {
        new AssistantCommand("new", "Start a new chat"),
        new AssistantCommand("clear", "Clear this chat"),
        new AssistantCommand("model", "Choose a model"),
        new AssistantCommand("cancel", "Stop the running turn"),
        new AssistantCommand("help", "List the BGS commands"),
    };

    public static bool IsCommand(string text) =>
        text.Length > 0 && text[0] == '/';

    public static bool TryParse(string text, out string name, out string arguments)
    {
        name = "";
        arguments = "";
        if (!IsCommand(text)) return false;
        string body = text[1..].Trim();
        if (body.Length == 0) return false;
        int space = body.IndexOf(' ');
        if (space < 0)
        {
            name = body.ToLowerInvariant();
            return true;
        }
        name = body[..space].ToLowerInvariant();
        arguments = body[(space + 1)..].Trim();
        return name.Length > 0;
    }

    public static bool IsBgsCommand(string name)
    {
        foreach (AssistantCommand command in All)
            if (string.Equals(command.Name, name, StringComparison.Ordinal)) return true;
        return false;
    }
}
