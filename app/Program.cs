using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // Anything on the command line that is a file gets opened, which is what a file manager
            // will do when the exe is set as a handler for .hkx.
            foreach (string arg in desktop.Args ?? Array.Empty<string>())
                if (File.Exists(arg)) { window.Open(arg); break; }
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version")) { Console.WriteLine(Version()); return 0; }
        if (args.Contains("--headless")) return Headless.Run(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    private static string Version()
    {
        var name = typeof(Program).Assembly.GetName();
        return $"Behaviour Graph Studio {name.Version?.ToString(3)} " +
               $"({System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier})";
    }
}

// Opening a file and checking it without a display, so a change can be made and proved in a
// terminal or on a build runner. This and the window share one path into the format layer, so what
// this reports is what the window would show.
public static class Headless
{
    public static int Run(string[] args)
    {
        string? path = args.FirstOrDefault(File.Exists);
        if (path == null) { Console.Error.WriteLine("--headless needs a path to a .hkx file"); return 2; }

        if (!HkxBinaryReader.IsFo4Hkx(path))
        {
            Console.Error.WriteLine("not a Fallout 4 hk_2014.1.0-r1 packfile");
            return 1;
        }

        var root = HkxBehaviorParser.ParseBehavior(path);
        if (root == null) { Console.Error.WriteLine("no root object resolved"); return 1; }

        var objects = HkxBehaviorParser.LastObjects;
        Console.WriteLine($"{Path.GetFileName(path)}  root {root.ClassName}  {objects.Count} objects  " +
                          $"{objects.Select(o => o.ClassName).Distinct().Count()} classes");

        HkxTextEdit.AppDirectory = AppContext.BaseDirectory;
        string? java = HkxTextEdit.FindJava(Settings.Get("java"));
        string? jar = HkxTextEdit.FindHkxPack(Settings.Get("hkxpack"), AppContext.BaseDirectory);
        if (java == null || jar == null)
        {
            Console.WriteLine(java == null
                ? "read only: no java runtime found, so the graph and symbols are unavailable"
                : "read only: hkxpack-cli.jar was not found next to the executable");
            return 0;
        }

        string work = Path.Combine(Path.GetTempPath(), "bgs_headless", Path.GetFileNameWithoutExtension(path));
        HkxTextEdit.ResetDirectory(work);

        string xmlPath = HkxTextEdit.Unpack(java, jar, path, work);
        string xml = HkxTextEdit.ReadXml(xmlPath);
        var model = BehaviourGraphModel.Parse(xml);

        Console.WriteLine($"graph  {GraphAuthor.Layout(model, 4000).Count} nodes drawn");
        Console.WriteLine("symbols  " + SymbolEditor.Audit(model));

        var chain = ProjectChain.Resolve(path, java, jar);
        Console.WriteLine($"chain  {chain.Animations.Count} animations declared under {chain.Root}");

        var findings = GraphValidator.Check(xml, chain);
        foreach (var f in findings) Console.WriteLine("check  " + f);
        int errors = findings.Count(f => f.Level == GraphValidator.Level.Error);
        Console.WriteLine($"check  {errors} errors, {findings.Count - errors} warnings");
        return errors == 0 ? 0 : 1;
    }
}
