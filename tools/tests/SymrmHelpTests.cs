using System;
using System.IO;
using System.Linq;
using BehaviourStudio.Tools;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SymrmHelpTests
{
    [Fact]
    public void CatalogCoversEveryDispatchedCommand()
    {
        string[] expected =
        {
            "corpus", "check", "states", "events", "frames", "scale", "skeleton", "rig",
            "extract", "ba2", "motion", "pose", "channels", "packfile", "nullsave", "layout",
            "relayout", "ground", "offsets", "convert", "compare", "delete", "paste", "template",
            "conditions", "savedelete", "classcheck", "types", "chain", "crash", "hash", "sweep",
            "diff", "notes", "saveevent", "savewide", "savenumbers", "walk", "signatures", "paths",
            "elements", "nesting", "objects", "capacity", "qstransform", "splinestats", "spline",
            "savespline", "editframe", "trim", "retime", "run", "weights", "cliptime", "cliptrim",
            "mesh", "meshpng", "lifecycle", "test", "defaults",
        };

        Assert.Equal(expected, SymrmHelp.Commands.Select(command => command.Name));
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryCommandHasStandaloneHelpWithFlagsAndExample()
    {
        foreach (var command in SymrmHelp.Commands)
        {
            Assert.True(SymrmHelp.TryCommand(command.Name, out string help));
            Assert.Contains("Usage:", help, StringComparison.Ordinal);
            Assert.Contains("symrm " + command.Name, help, StringComparison.Ordinal);
            Assert.Contains("Flags:", help, StringComparison.Ordinal);
            Assert.Contains("Example:", help, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(command.Summary));
            Assert.False(string.IsNullOrWhiteSpace(command.Flags));
            Assert.StartsWith("symrm " + command.Name, command.Example, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OverviewListsEveryCommandAndPointsToCommandHelp()
    {
        string overview = SymrmHelp.Overview();
        Assert.Contains("symrm <command> --help", overview, StringComparison.Ordinal);
        foreach (var command in SymrmHelp.Commands)
            Assert.Contains(command.Name, overview, StringComparison.Ordinal);
    }

    [Fact]
    public void CliEntryRoutesOverviewAndCommandHelp()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, CliEntry.Run(new[] { "--help" }, output, error));
        Assert.Contains("Commands:", output.ToString(), StringComparison.Ordinal);
        Assert.Equal("", error.ToString());

        output.GetStringBuilder().Clear();
        Assert.Equal(0, CliEntry.Run(new[] { "hash", "--help" }, output, error));
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("symrm hash", output.ToString(), StringComparison.Ordinal);

        output.GetStringBuilder().Clear();
        Assert.Equal(0, CliEntry.Run(new[] { "help", "extract" }, output, error));
        Assert.Contains("--tree", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandHelpFailsClearly()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(1, CliEntry.Run(new[] { "no-such-command", "--help" }, output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("unknown symrm command", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("symrm --help", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HelpTokensAreExplicit()
    {
        Assert.True(SymrmHelp.IsHelpToken("--help"));
        Assert.True(SymrmHelp.IsHelpToken("-h"));
        Assert.False(SymrmHelp.IsHelpToken("help"));
        Assert.False(SymrmHelp.TryCommand("no-such-command", out _));
    }
}
