using System;
using System.Linq;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantCommandsTests
{
    [Fact]
    public void ACommandIsRecognisedOnlyWhenItLeadsWithASlash()
    {
        Assert.True(AssistantCommands.IsCommand("/help"));
        Assert.False(AssistantCommands.IsCommand("help"));
        Assert.False(AssistantCommands.IsCommand(""));
        Assert.False(AssistantCommands.IsCommand("say /help"));
    }

    [Fact]
    public void ParsingSplitsTheNameFromItsArgumentsAndLowercasesTheName()
    {
        Assert.True(AssistantCommands.TryParse("/New", out string name, out string arguments));
        Assert.Equal("new", name);
        Assert.Equal("", arguments);

        Assert.True(AssistantCommands.TryParse("/model gemini-9-pro", out string model, out string rest));
        Assert.Equal("model", model);
        Assert.Equal("gemini-9-pro", rest);
    }

    [Fact]
    public void ABareSlashIsNotACommand()
    {
        Assert.False(AssistantCommands.TryParse("/", out _, out _));
        Assert.False(AssistantCommands.TryParse("plain text", out _, out _));
    }

    [Fact]
    public void TheQuickStartExplainsTheCommandsAndTheApprovalRule()
    {
        string guide = string.Join(" ", AssistantPane.QuickStartLines);

        Assert.Contains("/new, /clear, /model", guide, StringComparison.Ordinal);
        Assert.Contains("Shift+Enter", guide, StringComparison.Ordinal);
        Assert.Contains("approval", guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheBgsCommandSetIsTheApprovedFive()
    {
        Assert.Equal(new[] { "new", "clear", "model", "cancel", "help" },
            AssistantCommands.All.Select(command => command.Name));
        Assert.All(AssistantCommands.All, command => Assert.NotEmpty(command.Description));
        Assert.True(AssistantCommands.IsBgsCommand("help"));
        Assert.False(AssistantCommands.IsBgsCommand("pdf"));
    }
}
