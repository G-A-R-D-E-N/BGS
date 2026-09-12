using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using BehaviourStudio.App;
using AppHost = BehaviourStudio.App.App;

namespace BehaviourStudio.UiSmoke;

// Production builds the window through AppHost.CreateMainWindow, which attaches the native
// authoring strip. The rest of this harness constructs MainWindow directly, so nothing
// here used to reach the strip or BatchAuthoringWindow at all. These cases go through the
// production factory so a regression in that path cannot hide behind a compile-only check.
internal static class NativeAuthoringSmoke
{
    internal static void Run()
    {
        StripAttachesOnceOnTheProductionPath();
        BatchAuthoringListsTheMachinesInTheDocument();
        QueueRemovalUsesTheRealUi();
        ApplyIsOneUndoStepAndTouchesNoFile();
        ADocumentChangeInvalidatesAPendingPreview();
        ReopeningTheSourceInvalidatesAPendingPreview();
        SaveAfterApplyStillGoesThroughTheVerifiedTransaction();
        VariablesAreAuthoredInTheSameVerifiedBatch();
    }

    private static void StripAttachesOnceOnTheProductionPath()
    {
        Console.WriteLine("\nthe native authoring strip attaches once on the production path");
        var window = AppHost.CreateMainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Smoke.Check("the production factory attaches the strip", 1,
                    Smoke.Find<NativeAuthoringStrip>(window).Count);
        Smoke.CheckTrue("and the batch authoring button is reachable from it",
                        Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "Batch authoring"));
        Smoke.Check("the production shell also carries one build-info affordance", 1,
                    Smoke.Find<BuildInfoStrip>(window).Count);
        Smoke.CheckTrue("and About is reachable from that shared shell",
                        Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "About"));

        NativeAuthoringUi.Attach(window);
        Dispatcher.UIThread.RunJobs();
        Smoke.Check("attaching a second time does not stack a second strip", 1,
                    Smoke.Find<NativeAuthoringStrip>(window).Count);

        Smoke.CloseForTest(window);
    }

    private static void BatchAuthoringListsTheMachinesInTheDocument()
    {
        Console.WriteLine("\nbatch authoring lists the state machines in the open document");
        WithFixture((window, path) =>
        {
            var blank = AppHost.CreateMainWindow();
            blank.Show();
            Dispatcher.UIThread.RunJobs();
            var empty = NativeAuthoringUi.OpenBatchAuthoringForTest(blank);
            Dispatcher.UIThread.RunJobs();
            Smoke.Check("with no document open it offers no state machine", 0, empty.MachineNamesForTest.Count);
            empty.Close();
            Smoke.CloseForTest(blank);

            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = NativeAuthoringUi.OpenBatchAuthoringForTest(window);
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("the open document contributes its one state machine", 1,
                        batch.MachineNamesForTest.Count);
            Smoke.Check("named from the object it came from", "hkbStateMachine",
                        batch.MachineNamesForTest[0]);
            batch.Close();
        });
    }

    private static void QueueRemovalUsesTheRealUi()
    {
        Console.WriteLine("\nnative authoring removes queued animations and variables through the real controls");
        WithFixture(GraphBytes(), (window, path) =>
        {
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = Queue(window,
                ("Idle", "Animations\\Idle.hkx"),
                ("Walk", "Animations\\Walk.hkx"));
            batch.QueueVariableForTest("Speed", "real", "1.5");
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("two animations start in the batch", 2, batch.QueuedForTest);
            Smoke.Check("one variable starts in the batch", 1, batch.QueuedVariablesForTest);

            var grids = Smoke.Find<HkGrid>(batch);
            var animationQueue = grids.Single(grid => grid.RowCount == 2);
            var variableQueue = grids.Single(grid => grid.RowCount == 1);

            Smoke.CheckTrue("the first queued animation can be selected", animationQueue.SelectByTag(0));
            Smoke.Click(Smoke.Find<Button>(batch)
                .Single(button => button.Content?.ToString() == "Remove selected"));
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("Remove selected removes exactly one animation", 1, batch.QueuedForTest);
            Smoke.Check("the animation grid redraws with one row", 1, animationQueue.RowCount);
            Smoke.CheckTrue("the removal is reported in the authoring status",
                            batch.SummaryForTest.Contains("Removed", StringComparison.Ordinal));

            Smoke.CheckTrue("the queued variable can be selected", variableQueue.SelectByTag(0));
            Smoke.Click(Smoke.Find<Button>(batch)
                .Single(button => button.Content?.ToString() == "Remove variable"));
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("Remove variable removes the selected variable", 0, batch.QueuedVariablesForTest);
            Smoke.Check("the variable grid redraws empty", 0, variableQueue.RowCount);
            Smoke.CheckTrue("the variable removal is reported in the authoring status",
                            batch.SummaryForTest.Contains("Removed variable", StringComparison.Ordinal));
            batch.Close();
        });
    }

    private static void ApplyIsOneUndoStepAndTouchesNoFile()
    {
        Console.WriteLine("\napply lands as one undo step and writes nothing to disk");
        WithFixture((window, path) =>
        {
            byte[] before = File.ReadAllBytes(path);
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            string opened = window.LoadedXml;
            var batch = Queue(window, ("Idle", "Animations\\Idle.hkx"), ("Walk", "Animations\\Walk.hkx"));
            Smoke.Check("both animations are queued", 2, batch.QueuedForTest);

            batch.PreviewForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("preview verifies and offers apply: " + batch.SummaryForTest,
                            batch.HasPendingPreviewForTest && batch.ApplyOfferedForTest);
            Smoke.CheckTrue("preview alone leaves the file untouched",
                            File.ReadAllBytes(path).SequenceEqual(before));
            Smoke.CheckTrue("and leaves the document clean", !window.IsDirty);

            batch.ApplyForTest();
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("apply lands as exactly one undo step", 1, window.UndoStepsForTest);
            Smoke.CheckTrue("it makes the document dirty", window.IsDirty);
            Smoke.CheckTrue("the file on disk is still untouched",
                            File.ReadAllBytes(path).SequenceEqual(before));
            Smoke.CheckTrue("and the states reached the document",
                            window.LoadedXml.Contains(">Idle<", StringComparison.Ordinal) &&
                            window.LoadedXml.Contains(">Walk<", StringComparison.Ordinal));
            Smoke.CheckTrue("apply is not offered again until the batch is previewed again",
                            !batch.ApplyOfferedForTest);

            window.UndoForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("one undo removes the whole batch", window.LoadedXml == opened);
            Smoke.Check("and leaves nothing behind to undo", 0, window.UndoStepsForTest);
            batch.Close();
        });
    }

    private static void ADocumentChangeInvalidatesAPendingPreview()
    {
        Console.WriteLine("\nchanging the document after a preview refuses the apply");
        WithFixture((window, path) =>
        {
            byte[] before = File.ReadAllBytes(path);
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = Queue(window, ("Idle", "Animations\\Idle.hkx"));
            batch.PreviewForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("the preview is pending", batch.HasPendingPreviewForTest);

            // any edit through the ordinary document path, made behind the batch window's back
            string edited = window.LoadedXml.Replace(
                "<hkparam name=\"userData\">0</hkparam>",
                "<hkparam name=\"userData\">1</hkparam>", StringComparison.Ordinal);
            Smoke.CheckTrue("the edit really changes the document", edited != window.LoadedXml);
            window.SetXmlForTest(edited);
            Dispatcher.UIThread.RunJobs();
            int steps = window.UndoStepsForTest;

            batch.ApplyForTest();
            Dispatcher.UIThread.RunJobs();

            Smoke.CheckTrue("apply is refused and says why: " + batch.SummaryForTest,
                            batch.SummaryForTest.Contains("changed after preview", StringComparison.Ordinal));
            Smoke.CheckTrue("the pending preview is dropped", !batch.HasPendingPreviewForTest);
            Smoke.Check("and no document change is made", steps, window.UndoStepsForTest);
            Smoke.CheckTrue("the file on disk is untouched", File.ReadAllBytes(path).SequenceEqual(before));
            batch.Close();
        });
    }

    private static void ReopeningTheSourceInvalidatesAPendingPreview()
    {
        Console.WriteLine("\nreopening the source after a preview refuses the apply");
        WithFixture((window, path) =>
        {
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = Queue(window, ("Idle", "Animations\\Idle.hkx"));
            batch.PreviewForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("the preview is pending", batch.HasPendingPreviewForTest);

            string other = Path.Combine(Path.GetTempPath(), $"bgs-authoring-other-{Guid.NewGuid():N}.hkx");
            File.WriteAllBytes(other, Smoke.OneMachineBytes());
            try
            {
                window.Open(other);
                Dispatcher.UIThread.RunJobs();

                batch.ApplyForTest();
                Dispatcher.UIThread.RunJobs();

                Smoke.CheckTrue("apply is refused after the document was swapped: " + batch.SummaryForTest,
                                batch.SummaryForTest.Contains("changed after preview", StringComparison.Ordinal));
                Smoke.CheckTrue("the pending preview is dropped", !batch.HasPendingPreviewForTest);
                Smoke.Check("and the newly opened document is untouched", 0, window.UndoStepsForTest);
            }
            finally { File.Delete(other); }
            batch.Close();
        });
    }

    private static void SaveAfterApplyStillGoesThroughTheVerifiedTransaction()
    {
        Console.WriteLine("\nsave after apply still runs the verified save transaction");
        WithFixture((window, path) =>
        {
            byte[] before = File.ReadAllBytes(path);
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = Queue(window, ("Idle", "Animations\\Idle.hkx"));
            batch.PreviewForTest();
            Dispatcher.UIThread.RunJobs();
            batch.ApplyForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("the batch applied", window.IsDirty);

            window.VerifyFaultForTest = () => new InvalidDataException("injected verification fault");
            window.SaveForTest();
            Dispatcher.UIThread.RunJobs();

            Smoke.CheckTrue("a verification fault blocks the save of authored bytes",
                            File.ReadAllBytes(path).SequenceEqual(before));
            Smoke.CheckTrue("and leaves the authored document dirty", window.IsDirty);

            window.VerifyFaultForTest = null;
            window.SaveForTest();
            Dispatcher.UIThread.RunJobs();

            Smoke.CheckTrue("with verification passing the authored bytes reach disk",
                            !File.ReadAllBytes(path).SequenceEqual(before));
            Smoke.CheckTrue("and the document is no longer dirty", !window.IsDirty);

            var reopened = AppHost.CreateMainWindow();
            reopened.Show();
            reopened.Open(path);
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("the saved file reopens with the authored state in it",
                            reopened.LoadedXml.Contains(">Idle<", StringComparison.Ordinal));
            Smoke.CloseForTest(reopened);
            batch.Close();
        });
    }

    private static void VariablesAreAuthoredInTheSameVerifiedBatch()
    {
        Console.WriteLine("\nvariables are authored in the same verified batch as the clips");
        WithFixture(GraphBytes(), (window, path) =>
        {
            byte[] before = File.ReadAllBytes(path);
            window.Open(path);
            Dispatcher.UIThread.RunJobs();

            var batch = Queue(window, ("Idle", "Animations\\Idle.hkx"));
            batch.QueueVariableForTest("Speed", "real", "1.5");
            batch.QueueVariableForTest("Armed", "bool", "true");
            Dispatcher.UIThread.RunJobs();
            Smoke.Check("both variables are queued", 2, batch.QueuedVariablesForTest);

            batch.QueueVariableForTest("Speed", "int32", "0");
            Smoke.Check("a name already in the batch is refused", 2, batch.QueuedVariablesForTest);
            batch.QueueVariableForTest("Broken", "int32", "not a number");
            Smoke.Check("a value the type cannot hold is refused", 2, batch.QueuedVariablesForTest);
            Smoke.CheckTrue("and it says which value: " + batch.SummaryForTest,
                            batch.SummaryForTest.Contains("not a number", StringComparison.Ordinal));

            batch.PreviewForTest();
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("preview verifies clips and variables together: " + batch.SummaryForTest,
                            batch.HasPendingPreviewForTest &&
                            batch.SummaryForTest.Contains("2 variables", StringComparison.Ordinal));
            Smoke.CheckTrue("preview writes nothing", File.ReadAllBytes(path).SequenceEqual(before));

            batch.ApplyForTest();
            Dispatcher.UIThread.RunJobs();

            Smoke.Check("clips and variables land as one undo step", 1, window.UndoStepsForTest);
            Smoke.CheckTrue("the variable names reached the document",
                            window.LoadedXml.Contains(">Speed<", StringComparison.Ordinal) &&
                            window.LoadedXml.Contains(">Armed<", StringComparison.Ordinal));
            Smoke.CheckTrue("and the state did too",
                            window.LoadedXml.Contains(">Idle<", StringComparison.Ordinal));
            Smoke.CheckTrue("the file on disk is still untouched",
                            File.ReadAllBytes(path).SequenceEqual(before));
            batch.Close();
        });
    }

    private static BatchAuthoringWindow Queue(MainWindow window, params (string State, string Animation)[] entries)
    {
        var batch = NativeAuthoringUi.OpenBatchAuthoringForTest(window);
        Dispatcher.UIThread.RunJobs();
        batch.RefreshMachinesForTest();
        foreach (var (state, animation) in entries) batch.QueueForTest(state, animation);
        Dispatcher.UIThread.RunJobs();
        return batch;
    }

    // A graph with somewhere to declare variables as well as a machine to hang states on.
    // OneMachineBytes holds only the machine, so variable authoring correctly refuses it.
    private static byte[] GraphBytes()
    {
        var image = new OpenCommonwealth.Services.Hkx.PackfileImage();
        foreach (string tag in new[] { "__classnames__", "__data__" })
        {
            var bytes = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(bytes, 0);
            image.Sections.Add(new OpenCommonwealth.Services.Hkx.PackfileSection { TagBytes = bytes });
        }
        foreach (string className in new[]
                 { "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData", "hkbVariableValueSet" })
            OpenCommonwealth.Services.Hkx.NativeAppend.Object(image, className);
        OpenCommonwealth.Services.Hkx.FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static void WithFixture(Action<MainWindow, string> test) => WithFixture(Smoke.OneMachineBytes(), test);

    private static void WithFixture(byte[] fixture, Action<MainWindow, string> test)
    {
        string path = Path.Combine(Path.GetTempPath(), $"bgs-authoring-{Guid.NewGuid():N}.hkx");
        File.WriteAllBytes(path, fixture);
        var window = AppHost.CreateMainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { test(window, path); }
        finally
        {
            Smoke.CloseForTest(window);
            File.Delete(path);
        }
    }
}
