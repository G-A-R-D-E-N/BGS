using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;

namespace BehaviourStudio.UiSmoke;

internal static class RigAuthoringSmoke
{
    internal static void Run(string? renderDirectory = null)
    {
        string skeletonSource = Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors", "Character", "CharacterAssets", "skeleton.hkx");
        string skinSource = Path.Combine("tools", "symrm", "samples", "Meshes", "Actors", "Turret", "CharacterAssets", "TurretMounted.nif");
        string skeletonPath = Path.Combine(Path.GetTempPath(), $"bgs-rig-{Guid.NewGuid():N}.hkx");
        string skinPath = Path.Combine(Path.GetTempPath(), $"bgs-skin-{Guid.NewGuid():N}.nif");
        File.Copy(skeletonSource, skeletonPath);
        File.Copy(skinSource, skinPath);
        var owner = BehaviourStudio.App.App.CreateMainWindow();
        owner.Show();
        RigAuthoringWindow? editor = null;
        try
        {
            Dispatcher.UIThread.RunJobs();
            Click(owner, "Skeleton / skin authoring");
            editor = owner.OwnedWindows.OfType<RigAuthoringWindow>().Single();
            editor.OpenSkeleton(skeletonPath);
            Dispatcher.UIThread.RunJobs();
            ViewportFits(editor);
            Render(editor, renderDirectory, "skeleton");
            var boneName = Smoke.Find<TextBox>(editor).Single(box => box.Name == "BoneName");
            string beforeName = boneName.Text!;
            boneName.Text = "UI Edited Root";
            byte[] before = File.ReadAllBytes(skeletonPath);
            Click(editor, "Apply bone");
            Require(before.SequenceEqual(File.ReadAllBytes(skeletonPath)), "skeleton edits must stay in memory");
            Click(editor, "Undo skeleton");
            Require(boneName.Text == beforeName, "skeleton UI undo must restore the field");
            Click(editor, "Redo skeleton");
            Click(editor, "Save skeleton");
            Require(new SkeletonDocument(skeletonPath).Skeletons[0].BoneNames[0] == "UI Edited Root", "skeleton UI edit must survive reopen");
            Require(File.ReadAllBytes(skeletonPath + ".bak").SequenceEqual(before), "skeleton backup must preserve the original");

            var tabs = Smoke.Find<TabControl>(editor).Single();
            tabs.SelectedIndex = 1;
            editor.OpenSkin(skinPath);
            Dispatcher.UIThread.RunJobs();
            ViewportFits(editor);
            Require(Smoke.Find<SkeletonView>(editor).Any(view => view.DrawnEdges > 0), "skin preview must contain mesh edges without an animation pose");
            Render(editor, renderDirectory, "skin");
            var weight = Smoke.Find<TextBox>(editor).Single(box => box.Name == "SkinWeight0");
            string originalWeight = weight.Text!;
            weight.Text = "0.75";
            before = File.ReadAllBytes(skinPath);
            Click(editor, "Apply influences");
            Require(before.SequenceEqual(File.ReadAllBytes(skinPath)), "skin edits must stay in memory");
            Click(editor, "Undo skin");
            Require(weight.Text == originalWeight, "skin UI undo must restore influences");
            Click(editor, "Redo skin");
            Click(editor, "Save skin");
            Require(new SkinDocument(skinPath).Shapes.First(SkinEdit.Editable).BoneWeights[0] == 0.75f, "skin UI edit must survive reopen");
            Require(File.ReadAllBytes(skinPath + ".bak").SequenceEqual(before), "skin backup must preserve the original");

            tabs.SelectedIndex = 2;
            editor.OpenPhysics(skeletonSource);
            Dispatcher.UIThread.RunJobs();
            ViewportFits(editor);
            Click(editor, "Step physics");
            Require(Simulation(editor) == null, "physics must refuse missing user-authored masses/settings");
            Smoke.Find<TextBox>(editor).Single(box => box.Name == "SimulationMass").Text = "5";
            Click(editor, "Set all masses");
            Click(editor, "Set all preview joints");
            Click(editor, "Step physics");
            RagdollSimulation simulation = Simulation(editor) ?? throw new InvalidOperationException("configured physics must start through the editor controls");
            Require(simulation.Frames.Count > 0, "physics must expose simulated frames");
            Require(simulation.HullEdges.Length > 0 && Smoke.Find<SkeletonView>(editor).Any(view => view.DrawnEdges == simulation.HullEdges.Length), "physics must render the solver's actual collision hulls");
            Console.WriteLine($"Physics preview: {simulation.Frames.Count} bodies, {simulation.HullEdges.Length} solver hull edges.");
            Render(editor, renderDirectory, "physics");
            var gravity = Smoke.Find<TextBox>(editor).Single(box => box.Name == "SimulationGravity");
            var ground = Smoke.Find<TextBox>(editor).Single(box => box.Name == "SimulationGround");
            Require(gravity.IsReadOnly && ground.IsReadOnly, "active physics environment values must require reset before editing");
            Click(editor, "Reset simulation");
            Require(!gravity.IsReadOnly && !ground.IsReadOnly, "reset must allow environment edits");
            Require(Simulation(editor) == null, "reset must release the simulation");
            bool disposed = false;
            try { simulation.Advance(1f / 120); } catch (ObjectDisposedException) { disposed = true; }
            Require(disposed, "reset must dispose buffers");
            Click(editor, "Start / resume simulation");
            simulation = Simulation(editor) ?? throw new InvalidOperationException("physics must restart after reset");
            editor.Close();
            disposed = false;
            try { simulation.Advance(1f / 120); } catch (ObjectDisposedException) { disposed = true; }
            Require(disposed, "window close must dispose physics");
            DirtyClose(owner, skeletonPath);
            Console.WriteLine("PASS: production rig controls, skeleton/skin undo/save/reopen, configured physics, reset and close disposal.");
        }
        finally
        {
            editor?.Close();
            owner.Close();
            foreach (string path in new[] { skeletonPath, skinPath }) { File.Delete(path); File.Delete(path + ".bak"); }
        }
    }

    private static void Click(Window window, string label)
    {
        Smoke.Click(Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == label));
        Dispatcher.UIThread.RunJobs();
    }

    private static void ViewportFits(RigAuthoringWindow window)
    {
        var view = Smoke.Find<SkeletonView>(window).Single();
        Require(view.ClipToBounds, "rig geometry must be clipped to its viewport");
        var fit = Smoke.Find<Button>(window).Single(button => button.Content?.ToString() == "Fit preview");
        double before = view.Bounds.Height;
        window.Height += 180;
        Dispatcher.UIThread.RunJobs();
        Require(view.Bounds.Height > before + 100, "rig viewport must grow with the window");
        Require(!view.GetVisualAncestors().OfType<ScrollViewer>().Any(), "settings scrolling must not move the viewport");
        typeof(SkeletonView).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, 8d);
        typeof(SkeletonView).GetField("_pan", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, new Avalonia.Point(500, -500));
        Smoke.Click(fit);
        Require((double)typeof(SkeletonView).GetField("_zoom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)! == 1d,
            "Fit preview must reset zoom");
        Require((Avalonia.Point)typeof(SkeletonView).GetField("_pan", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)! == default,
            "Fit preview must reset pan");
        window.Height -= 180;
        Dispatcher.UIThread.RunJobs();
        double width = window.Width, height = window.Height;
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Dispatcher.UIThread.RunJobs();
        Console.WriteLine($"Minimum rig viewport: {view.Bounds.Width:0} x {view.Bounds.Height:0}.");
        Require(view.Bounds.Width > 600 && view.Bounds.Height > 100,
            "minimum-size rig window must keep a usable viewport");
        var content = (Control)window.Content!;
        Require(view.TranslatePoint(new Avalonia.Point(0, view.Bounds.Height), content)!.Value.Y <= content.Bounds.Height,
            "minimum-size rig viewport must stay inside the window");
        window.Width = width;
        window.Height = height;
        Dispatcher.UIThread.RunJobs();
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static RagdollSimulation? Simulation(RigAuthoringWindow window) =>
        (RagdollSimulation?)typeof(RigAuthoringWindow).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static void Render(Window window, string? directory, string name)
    {
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        var content = (Control)window.Content!;
        content.Measure(new Avalonia.Size(1050, 760));
        content.Arrange(new Avalonia.Rect(0, 0, 1050, 760));
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        Dispatcher.UIThread.RunJobs();
        using var bitmap = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)
            ?? throw new InvalidOperationException("Rig editor did not render a frame.");
        bitmap.Save(Path.Combine(directory, name + ".png"));
    }

    private static void DirtyClose(MainWindow owner, string path)
    {
        foreach (string decision in new[] { "Cancel", "Save and continue", "Discard changes" })
        {
            var editor = new RigAuthoringWindow(owner);
            editor.Show(owner); editor.OpenSkeleton(path);
            Dispatcher.UIThread.RunJobs();
            byte[] before = File.ReadAllBytes(path);
            string changed = "Close " + decision;
            Smoke.Find<TextBox>(editor).Single(box => box.Name == "BoneName").Text = changed;
            Click(editor, "Apply bone");
            editor.Close(); Dispatcher.UIThread.RunJobs();
            Require(editor.IsVisible, "unsaved close must wait for a decision");
            Click(editor.OwnedWindows.OfType<DiscardDialog>().Single(), decision);
            if (decision == "Save and continue")
                Require(!editor.IsVisible && new SkeletonDocument(path).Skeletons[0].BoneNames[0] == changed, "save-and-close must preserve the edited name");
            else
            {
                Require(before.SequenceEqual(File.ReadAllBytes(path)), "cancel/discard must not write the file");
                Require(editor.IsVisible == (decision == "Cancel"), "close must respect cancel/discard");
                if (editor.IsVisible)
                {
                    editor.Close(); Dispatcher.UIThread.RunJobs();
                    Click(editor.OwnedWindows.OfType<DiscardDialog>().Single(), "Discard changes");
                }
            }
        }
    }
}
