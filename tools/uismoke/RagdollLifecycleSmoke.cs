using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

public static class LifecycleSmoke
{
    public static int Main(string[] args)
    {
        if (args.Contains("--layout-smoke"))
        {
            AppBuilder.Configure<HeadlessApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            Settings.SettingsPathForTest =
                Path.Combine(Path.GetTempPath(), $"bgs-layout-{Guid.NewGuid():N}.cfg");
            Settings.TrySet("tour_done", "1", out _);
            try { return WorkspaceSmoke.LayoutRun(); }
            finally
            {
                File.Delete(Settings.SettingsPathForTest);
                Settings.SettingsPathForTest = null;
            }
        }

        if (args.Length > 0 && args[0] == "--assistant")
        {
            AppBuilder.Configure<HeadlessApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            AssistantLifecycleSmoke.Run();
            return 0;
        }
        int existing = Smoke.Main(args);
        if (args.Length >= 2 && args[0] == "--png") return existing;
        int workspace = WorkspaceSmoke.Run();
        int lifecycle = RagdollLifecycle();
        return existing == 0 && workspace == 0 && lifecycle == 0 ? 0 : 1;
    }

    private static int RagdollLifecycle()
    {
        int failed = 0;
        string root = Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors", "Character");
        string animation = Path.Combine(root, "Animations", "Paired",
            "PairedKill2HMBashKneeAndHead_AttackerLead.hkx");
        string skeleton = Path.Combine(root, "CharacterAssets", "skeleton.hkx");

        Check("ragdoll lifecycle animation fixture exists", File.Exists(animation), ref failed);
        Check("ragdoll lifecycle skeleton fixture exists", File.Exists(skeleton), ref failed);
        if (failed != 0) return failed;

        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(skeleton));
        Check("vanilla sibling skeleton has rigid bodies", model is { Bodies.Count: > 0 }, ref failed);
        Check("vanilla sibling skeleton has constraints", model is { Constraints.Count: > 0 }, ref failed);
        if (failed != 0) return failed;

        var window = new MainWindow();
        window.Show();
        try
        {
            window.Open(animation);
            Dispatcher.UIThread.RunJobs();

            var play = Field<Button>(window, "_playButton");
            var drop = Field<Button>(window, "_dropButton");
            var engineer = Field<CheckBox>(window, "_engineerToggle");

            Check("sibling ragdoll source is shown",
                window.PlaybackSummary.Contains("loaded from the sibling skeleton", StringComparison.Ordinal),
                ref failed);
            Check("Drop is enabled for the sibling ragdoll", drop.IsEnabled, ref failed);

            window.Viewport.ToggleBodyPinForTest(model!.Bodies[0].Id);
            window.Viewport.ToggleBodyPinForTest(model.Bodies[1].Id);
            Dispatcher.UIThread.RunJobs();
            Check("production body-frame distance is surfaced",
                window.FrameDistanceStatusForTest.Contains("file units", StringComparison.Ordinal), ref failed);
            Check("body-frame distance stays visible until cleared",
                window.FrameDistanceVisibleForTest, ref failed);
            Smoke.SelectTab(window, "Playback");
            Smoke.Click(Smoke.Find<Button>(window)
                .Single(button => button.Content?.ToString() == "Clear measure"));
            Check("clearing body-frame distance removes the production status",
                window.FrameDistanceStatusForTest.Length == 0 && !window.FrameDistanceVisibleForTest, ref failed);

            Smoke.Click(play);
            Check("animation playback starts before Drop", window.IsPlaying, ref failed);
            Smoke.Click(drop);
            Check("Drop captures the displayed mapped pose", window.Viewport.IsDropped, ref failed);
            Check("Drop freezes normal animation playback", !window.IsPlaying, ref failed);

            if (window.Viewport.IsDropped) Smoke.Click(drop);
            window.ScrubTo(window.PoseFrame);
            Dispatcher.UIThread.RunJobs();

            string sourceSummary = window.PlaybackSummary;
            int constraintId = model!.Constraints[0].Id;
            window.Viewport.HoverConstraintForTest(constraintId);
            Dispatcher.UIThread.RunJobs();
            window.Viewport.HoverConstraintForTest(-1);
            Dispatcher.UIThread.RunJobs();
            Check("constraint summary restore preserves sibling source",
                window.PlaybackSummary == sourceSummary, ref failed);

            engineer.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Check("Frames only enables engineering rendering", window.Viewport.EngineeringFrames, ref failed);

            string refused = Path.Combine(Path.GetTempPath(), $"bgs-ragdoll-refused-{Guid.NewGuid():N}.hkx");
            byte[] refusedBytes = File.ReadAllBytes(animation);
            refusedBytes[0x10] = 4;
            File.WriteAllBytes(refused, refusedBytes);
            window.Open(refused);
            Dispatcher.UIThread.RunJobs();

            Check("refused load keeps Frames only state consistent",
                engineer.IsChecked == window.Viewport.EngineeringFrames, ref failed);
            Check("refused load disables Drop", !drop.IsEnabled, ref failed);
            Check("refused load resets Drop label", drop.Content?.ToString() == "Drop", ref failed);
            Check("refused load hides stale ragdoll scale", !window.ScaleStatusVisibleForTest, ref failed);

            window.Open(animation);
            Dispatcher.UIThread.RunJobs();
            string orphan = Path.Combine(Path.GetTempPath(), $"bgs-ragdoll-orphan-{Guid.NewGuid():N}.hkx");
            File.Copy(animation, orphan, true);
            window.Open(orphan);
            Dispatcher.UIThread.RunJobs();
            Check("no-ragdoll load clears sibling source state",
                !window.PlaybackSummary.Contains("loaded from the sibling skeleton", StringComparison.Ordinal),
                ref failed);

            TryDelete(refused);
            TryDelete(orphan);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        return failed;
    }

    private static T Field<T>(MainWindow window, string name) where T : class
    {
        return (T)(typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
            ?? throw new InvalidOperationException($"Missing MainWindow field {name}"));
    }

    private static void Check(string what, bool value, ref int failed)
    {
        if (!value) failed++;
        Console.WriteLine($"  {(value ? "ok  " : "FAIL")}  {what}");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
