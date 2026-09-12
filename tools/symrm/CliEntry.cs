using System;
using System.IO;

namespace BehaviourStudio.Tools;

public static class CliEntry
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0)
        {
            output.WriteLine(SymrmHelp.Overview());
            return 1;
        }

        if (SymrmHelp.IsHelpToken(args[0]))
        {
            output.WriteLine(SymrmHelp.Overview());
            return 0;
        }

        if (string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length == 1)
            {
                output.WriteLine(SymrmHelp.Overview());
                return 0;
            }
            return PrintCommandHelp(args[1], output, error);
        }

        if (args.Length >= 2 && SymrmHelp.IsHelpToken(args[1]))
            return PrintCommandHelp(args[0], output, error);

        return Program.Main(args);
    }

    private static int PrintCommandHelp(string command, TextWriter output, TextWriter error)
    {
        if (SymrmHelp.TryCommand(command, out string help))
        {
            output.WriteLine(help);
            return 0;
        }

        error.WriteLine($"unknown symrm command '{command}'");
        error.WriteLine("Run 'symrm --help' for the complete command list.");
        return 1;
    }
}
