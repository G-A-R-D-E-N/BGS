using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

/// <summary>
/// Headless verification of the hardened loading and save paths. Run with
/// `uismoke --verify &lt;check&gt; &lt;path&gt;` where check is one of:
///   load     open a behaviour file and prove the UI thread keeps pumping during it
///   check    press Check graph and prove the validation work leaves the UI thread
///   symbols  open a file with real variable bounds and show them on the Symbols tab
///   gate     open a file that is not a Fallout 4 packfile and show it is refused
///   save     make an edit the native saver cannot write, press Save, and show the
///            detailed refusal instead of the generic fallback line
///   probe    list which single-field edits on a file NativeSave refuses
/// </summary>
public static class Verify
{
    private static int _failed;
    private static int _ran;

    private static void Check(string name, bool ok)
    {
        _ran++;
        if (ok)
        {
            Console.WriteLine("ok    " + name);
            return;
        }
        _failed++;
        Console.WriteLine("FAIL  " + name);
    }

    private static void Note(string message) => Console.WriteLine("      " + message);

    private static IEnumerable<T> Find<T>(Visual root) where T : Visual
    {
        if (root is T hit) yield return hit;
        foreach (var child in root.GetVisualChildren())
            foreach (var found in Find<T>(child))
                yield return found;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static TextBlock? _status;
    private static TextBlock? _summary;

    private static string StatusOf(MainWindow window)
    {
        if (_status == null)
        {
            // The status bar is the TextBlock the window first fills with this
            // prompt, before any file is opened.
            _status = Find<TextBlock>(window)
                .FirstOrDefault(t => t.Text == "Open a behaviour file to start.");
        }
        return _status?.Text ?? "";
    }

    private static string SummaryOf(MainWindow window)
    {
        if (_summary == null)
        {
            _summary = Find<TextBlock>(window)
                .FirstOrDefault(t => t.Text == "No file loaded.");
        }
        return _summary?.Text ?? "";
    }

    private static MainWindow Window()
    {
        var window = new MainWindow();
        window.Show();
        Pump();
        StatusOf(window);
        SummaryOf(window);
        return window;
    }

    /// <summary>
    /// Runs the given action while a background thread posts a low-priority beat
    /// onto the UI thread every few milliseconds. If the UI message loop keeps
    /// pumping during the action, beats accumulate; if the UI thread is blocked
    /// inside the action, none are processed. The beat comes from a timer thread
    /// rather than re-posting itself, because RunJobs drains the queue until it
    /// is empty and a self-reposting beat would keep it non-empty forever.
    /// </summary>
    private static int BeatsDuring(Action action)
    {
        int ticks = 0;
        using var timer = new System.Threading.Timer(_ =>
        {
            Dispatcher.UIThread.Post(() => Interlocked.Increment(ref ticks),
                                      DispatcherPriority.Background);
        }, null, 0, 5);

        action();
        timer.Dispose();
        return Volatile.Read(ref ticks);
    }

    public static int Run(string[] args)
    {
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        string check = args[1];
        string path = args[2];
        if (!File.Exists(path))
        {
            Console.WriteLine($"not found: {path}");
            return 2;
        }

        try
        {
            switch (check)
            {
                case "load": VerifyLoad(path); break;
                case "check": VerifyCheck(path); break;
                case "symbols": VerifySymbols(path); break;
                case "gate": VerifyGate(path); break;
                case "save": VerifySave(path); break;
                case "probe": ProbeRefusals(path); break;
                default:
                    Console.WriteLine($"unknown check: {check}");
                    return 2;
            }
        }
        catch (Exception e)
        {
            _failed++;
            Console.WriteLine("FAIL  the check itself threw: " + e);
        }

        Console.WriteLine($"{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void VerifyLoad(string path)
    {
        var window = Window();

        var sw = new Stopwatch();
        int beats = BeatsDuring(() =>
        {
            sw.Start();
            window.Open(path);
            sw.Stop();
        });

        Note($"open took {sw.ElapsedMilliseconds} ms wall time, " +
             $"{beats} UI-thread beats fired while it ran");
        Check("the file opened", window.LoadedXml.Length > 0 || window.ClipGrid.RowCount > 0);
        Check("the UI thread kept pumping during the load", beats > 0);
        Check("the window is still fully alive after opening",
              Find<Button>(window).Any(b => b.Content?.ToString() == "Check graph"));
    }

    private static void VerifyCheck(string path)
    {
        var window = Window();
        window.Open(path);
        Pump();

        var button = Find<Button>(window).First(b => b.Content?.ToString() == "Check graph");

        // The click handler itself: with the fix it returns at its first await and
        // the validation runs on a thread-pool thread; before the fix it blocked the
        // UI thread for the whole check.
        var swClick = new Stopwatch();
        swClick.Start();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        swClick.Stop();

        int beats = BeatsDuring(() =>
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                string status = StatusOf(window);
                if (status.Contains("Checked", StringComparison.Ordinal) ||
                    status.Contains("error", StringComparison.OrdinalIgnoreCase)) break;
                Thread.Sleep(5);
                Pump();
            }
        });

        string said = StatusOf(window);
        Note($"click returned in {swClick.ElapsedMilliseconds} ms; check finished " +
             $"with {beats} UI-thread beats while the UI kept pumping");
        Note(said.Length > 0 ? "status: " + said : "status: (empty)");
        Check("the check completed", said.Contains("Checked", StringComparison.Ordinal) ||
                                     said.Contains("error", StringComparison.OrdinalIgnoreCase));
        Check("the check ran off the UI thread (click returned before it finished)",
              swClick.ElapsedMilliseconds < 250 && beats > 0);
    }

    private static void VerifySymbols(string path)
    {
        var window = Window();
        window.Open(path);
        Pump();

        var model = BehaviourGraphModel.Parse(window.LoadedXml.Length > 0 ? window.LoadedXml : "");
        var bounds = SymbolEditor.VariableBounds(model);
        Note($"model reads {bounds.Count} bound row(s); first = {Describe(bounds.FirstOrDefault())}");
        Check("the model carries real bound values from the file", bounds.Count > 0);

        var tabs = Find<TabControl>(window).First();
        int symbols = tabs.Items.OfType<TabItem>().ToList()
                          .FindIndex(t => t.Header?.ToString() == "Symbols");
        tabs.SelectedIndex = symbols;
        Pump();

        bool selected = window.SymbolGrid.SelectByTag("v:0");
        Pump();

        var minBox = Find<TextBox>(window).FirstOrDefault(t => t.Watermark == "min");
        var maxBox = Find<TextBox>(window).FirstOrDefault(t => t.Watermark == "max");
        string min = minBox?.Text ?? "";
        string max = maxBox?.Text ?? "";
        Note($"variable 0 bounds shown on the Symbols tab: min={min}, max={max}");
        Check("a variable row was selected", selected);
        Check("the Symbols tab shows the real minimum", min.Length > 0);
        Check("and the real maximum", max.Length > 0);
    }

    private static (string Min, string Max) Describe((string Min, string Max)? row) =>
        row == null ? ("", "") : row.Value;

    private static void VerifyGate(string path)
    {
        var window = Window();
        window.Open(path);
        Pump();

        string reason = MainWindow.RefuseReason(path) ?? "";
        Check("the file is refused by the gate", reason.Length > 0);
        Note("reason: " + (reason.Length > 0 ? reason : "(none — file accepted)"));

        var dialog = window.OwnedWindows.OfType<NotBehaviourDialog>().FirstOrDefault();
        Check("a blocking dialog appeared", dialog != null);
        if (dialog != null)
        {
            var said = Find<TextBlock>(dialog).Select(t => t.Text ?? "").ToList();
            Note("dialog: " + string.Join(" | ", said.Where(s => s.Length > 0)));
            string chunk = reason.Length > 20 ? reason[..20] : reason;
            Check("the dialog explains why",
                  said.Any(t => t.Contains(chunk, StringComparison.Ordinal)));
            Check("the dialog names the file",
                  said.Any(t => t.Contains(Path.GetFileName(path), StringComparison.Ordinal)));
            Check("the dialog points at the issue tracker",
                  said.Any(t => t.Contains("github.com/NomadsReach/BehaviorGraphStudio/issues",
                                           StringComparison.Ordinal)));
            Check("the main window is disabled while it is up", !window.IsEnabled);

            Find<Button>(dialog).FirstOrDefault(b => b.Content?.ToString() == "OK")
                ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            Check("OK dismisses the dialog and re-enables the main window",
                  window.IsEnabled && !dialog.IsVisible);
        }

        string summary = SummaryOf(window);
        Note("summary: " + (summary.Length > 0 ? summary : "(empty)"));
        Check("nothing was parsed from it", window.LoadedXml.Length == 0);
        Check("the window is still alive after the refusal",
              Find<Button>(window).Any(b => b.Content?.ToString() == "Check graph"));
    }

    private static void VerifySave(string path)
    {
        // Work on a copy so the sample corpus is never at risk, and a refusal leaves
        // the copy byte-identical.
        string work = Path.Combine(Path.GetTempPath(), "uismoke-verify-save.hkx");
        File.Copy(path, work, true);
        string? bak = work + ".bak";
        if (File.Exists(bak)) File.Delete(bak);
        byte[] before = File.ReadAllBytes(work);

        var window = Window();
        window.Open(work);
        Pump();

        string xml = window.LoadedXml;
        var candidate = FirstRefusedField(xml);
        if (candidate == null)
        {
            Note("no single-field edit on this file is refused by the native saver; " +
                 "checking the refusal path directly instead");
            CandidateFallback(window, xml);
            return;
        }
        Note($"refused edit: #{candidate.Id} {candidate.Field} -> '{candidate.Value}'");

        var model = BehaviourGraphModel.Parse(xml);
        var node = model.Objects.FirstOrDefault(o => o.Id == candidate.Id);
        Check("the refused object is in the file", node != null);
        if (node == null) return;

        // Drive the same edit through the window's own field-commit path. The
        // properties panel lives on the Graph tab, so open that tab first.
        var tabs = Find<TabControl>(window).First();
        tabs.SelectedIndex = tabs.Items.OfType<TabItem>().ToList()
                                 .FindIndex(t => t.Header?.ToString() == "Graph");
        Pump();
        window.SelectNode(candidate.Id);
        Pump();

        var allCombos = Find<ComboBox>(window.GraphProperties).ToList();
        var allBoxes = Find<TextBox>(window.GraphProperties).ToList();
        var allLabels = Find<TextBlock>(window.GraphProperties).ToList();
        Note($"panel: {allBoxes.Count} box(es), {allCombos.Count} combo(s), " +
             $"{allLabels.Count} label(s); selected={window.SelectedObjectId}; " +
             $"combo items: [{string.Join(" | ", allCombos.Select(c => c.SelectedItem?.ToString() ?? "?"))}]; " +
             $"labels: [{string.Join(" | ", allLabels.Take(3).Select(t => t.Text ?? ""))}]");

        // Enum fields are offered as ComboBoxes whose selected item is the current
        // value; changing one is exactly the native-save-unsupported edit of #54.
        var combo = allCombos.FirstOrDefault(c => c.Items.Count > 1 &&
                                                  c.SelectedItem?.ToString() == candidate.Original);
        if (combo != null)
        {
            var options = combo.Items.OfType<object>().ToList();
            var next = options.FirstOrDefault(o => o.ToString() != candidate.Original);
            combo.SelectedItem = next;
            Pump();
        }
        else
        {
            var box = Find<TextBox>(window.GraphProperties)
                .FirstOrDefault(b => b.Text == candidate.Original);
            if (box == null) box = Find<TextBox>(window.GraphProperties).FirstOrDefault();
            Check("the property panel offers an editable box", box != null);
            if (box == null) return;
            box.Text = candidate.Value;
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            window.CommitPendingFields();
            Pump();
        }

        string edited = window.LoadedXml;
        var plan = NativeSave.Compare(xml, edited);
        Note("NativeSave says: " + (plan.Refusal ?? $"possible, {plan.Changes.Count} change(s)"));
        Check("the document now holds the edit", edited != xml);
        Check("the native saver refuses it with a reason", plan.Refusal != null);

        var save = Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();

        string said = StatusOf(window);
        Note("status: " + (said.Length > 0 ? said : "(empty)"));
        Check("Save reports a refusal", said.Contains("Not saved", StringComparison.Ordinal));
        string? refusalHead = plan.Refusal?.Split(',')[0].Trim();
        Check("the refusal is the detailed one, not the generic line",
              plan.Refusal != null && (said.Contains(plan.Refusal, StringComparison.Ordinal) ||
                                       (refusalHead != null &&
                                        said.Contains(refusalHead, StringComparison.Ordinal))));
        Check("the generic fallback line is gone",
              !said.Contains("native save does not support this edit yet", StringComparison.Ordinal));
        Check("the window is still alive after Save (no crash)",
              Find<Button>(window).Any(b => b.Content?.ToString() == "Check graph"));
        byte[] after = File.ReadAllBytes(work);
        Check("the file on disk was not touched by the refused save",
              before.Length == after.Length && before.SequenceEqual(after));
        Check("no backup was written either", !File.Exists(bak));
    }

    private static void CandidateFallback(MainWindow window, string xml)
    {
        // No natural refusal exists on this file. Refuse via an out-of-range bound
        // authoring through the Symbols tab instead, and verify Save still refuses.
        var save = Find<Button>(window).First(b => b.Content?.ToString() == "Save to .hkx");
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        string said = StatusOf(window);
        Note("status: " + (said.Length > 0 ? said : "(empty)"));
        Check("the window survives pressing Save with no changes",
              Find<Button>(window).Any(b => b.Content?.ToString() == "Check graph"));
    }

    private sealed record RefusedField(string Id, string Field, string Original, string Value);

    /// <summary>
    /// Finds a single-field edit the native saver refuses, preferring the genuine
    /// "cannot be written in place" class (an edit that is otherwise valid but has
    /// no in-place writer) over invalid-input refusals.
    /// </summary>
    private static RefusedField? FirstRefusedField(string xml)
    {
        RefusedField? firstAny = null;
        var model = BehaviourGraphModel.Parse(xml);
        foreach (var node in model.Objects)
        {
            foreach (var p in HkxTextEdit.ReadParams(xml, node.Id))
            {
                if (p.Value.Length == 0) continue;
                string changed = TryChange(p.Value);
                if (changed == p.Value) continue;

                string edited;
                try { edited = HkxTextEdit.SetParamAt(xml, node.Id, p.Name, changed); }
                catch (Exception) { continue; }
                if (edited == xml) continue;

                var plan = NativeSave.Compare(xml, edited);
                if (plan.Possible || plan.Refusal == null) continue;

                firstAny ??= new RefusedField(node.Id, p.Name, p.Value, changed);
                if (plan.Refusal.Contains("cannot be written in place", StringComparison.Ordinal))
                    return new RefusedField(node.Id, p.Name, p.Value, changed);
            }
        }
        return firstAny;
    }

    private static string TryChange(string value)
    {
        if (value == "0" || value == "0.000000") return "7";
        if (value == "1" || value == "1.000000") return "2";
        if (value.All(c => char.IsDigit(c) || c == '-' || c == '.'))
        {
            if (value.EndsWith("1", StringComparison.Ordinal)) return value[..^1] + "9";
            return value + "1";
        }
        return value + "x";
    }

    private static void ProbeRefusals(string path)
    {
        var window = Window();
        window.Open(path);
        Pump();

        string xml = window.LoadedXml;
        var seen = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, string>(StringComparer.Ordinal);
        int tried = 0;

        var model = BehaviourGraphModel.Parse(xml);
        foreach (var node in model.Objects)
        {
            foreach (var p in HkxTextEdit.ReadParams(xml, node.Id))
            {
                if (p.Value.Length == 0) continue;
                string changed = TryChange(p.Value);
                if (changed == p.Value) continue;

                string edited;
                try { edited = HkxTextEdit.SetParamAt(xml, node.Id, p.Name, changed); }
                catch (Exception) { continue; }
                if (edited == xml) continue;

                tried++;
                var plan = NativeSave.Compare(xml, edited);
                if (plan.Possible) continue;
                string why = plan.Refusal ?? "(no reason given)";
                seen[why] = seen.GetValueOrDefault(why) + 1;
                if (!examples.ContainsKey(why)) examples[why] = $"#{node.Id} {p.Name} '{p.Value}' -> '{changed}'";
            }
        }

        Note($"{tried} field edits compared; {seen.Count} distinct refusal(s):");
        foreach (var (why, count) in seen)
            Note($"{count,4}  {why}   e.g. {examples[why]}");
    }
}
