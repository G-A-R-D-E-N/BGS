using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using BehaviourStudio.App;
using AppHost = BehaviourStudio.App.App;
using OpenCommonwealth.Services.Hkx;

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
        StructureAuthoringIsReachableFromTheProductionPath();
        StructureAuthoringConditionRoundTripsThroughSave();
        StructureAuthoringNotifyRoundTripsThroughSave();
        StructureAuthoringStateMachineRoundTripsThroughSave();
        BatchAuthoringListsTheMachinesInTheDocument();
        QueueRemovalUsesTheRealUi();
        ApplyIsOneUndoStepAndTouchesNoFile();
        ADocumentChangeInvalidatesAPendingPreview();
        ReopeningTheSourceInvalidatesAPendingPreview();
        SaveAfterApplyStillGoesThroughTheVerifiedTransaction();
        VariablesAreAuthoredInTheSameVerifiedBatch();
    }

    private static void StructureAuthoringIsReachableFromTheProductionPath()
    {
        Console.WriteLine("\nstructure authoring is reachable from the production path");
        var window = AppHost.CreateMainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Smoke.CheckTrue("the structure authoring button is reachable",
                        Smoke.Find<Button>(window).Any(b => b.Content?.ToString() == "Structure authoring"));
        var editor = AdvancedAuthoringUi.OpenForTest(window);
        Dispatcher.UIThread.RunJobs();
        var labels = Smoke.Find<Button>(editor).Select(button => button.Content?.ToString()).ToHashSet();
        Smoke.CheckTrue("the structure editor exposes condition authoring",
                        labels.Contains("Create / assign condition") && labels.Contains("Clear condition"));
        Smoke.CheckTrue("the structure editor exposes notify authoring",
                        labels.Contains("Add enter") && labels.Contains("Add exit") &&
                        labels.Contains("Remove enter") && labels.Contains("Remove exit"));
        Smoke.CheckTrue("the structure editor exposes state-machine authoring",
                        labels.Contains("Create and attach"));
        editor.Close();
        Smoke.CloseForTest(window);
    }

    private static void StructureAuthoringConditionRoundTripsThroughSave()
    {
        Console.WriteLine("\nstructure authoring conditions survive Save and reopen");
        WithFixture(StructureGraphBytes(), (window, path) =>
        {
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();

            byte[] before = File.ReadAllBytes(path);
            editor.CreateExpressionConditionForTest("1 > 0");
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("condition create updates the document", window.IsDirty &&
                window.LoadedXml.Contains("hkbExpressionCondition", StringComparison.Ordinal));
            Smoke.CheckTrue("condition create leaves source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "condition create");

            editor.Close();
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var created = BehaviourGraphModel.Parse(window.LoadedXml);
            var createdRoute = StateRoutes.Of(created).Routes.Single();
            Smoke.CheckTrue("reopen keeps the created condition",
                created.Get(createdRoute.ConditionId)?.Str("expression") == "1 > 0");

            editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();
            before = File.ReadAllBytes(path);
            editor.CreateExpressionConditionForTest("bGateOpen == 1");
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("condition replace leaves source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "condition replace");

            editor.Close();
            if (window.IsDirty) return;
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var replaced = BehaviourGraphModel.Parse(window.LoadedXml);
            var replacedRoute = StateRoutes.Of(replaced).Routes.Single();
            Smoke.CheckTrue("reopen keeps the replaced condition",
                replaced.Get(replacedRoute.ConditionId)?.Str("expression") == "bGateOpen == 1");

            editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();
            before = File.ReadAllBytes(path);
            Smoke.Click(Smoke.Find<Button>(editor).Single(button => button.Content?.ToString() == "Clear condition"));
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("condition clear leaves source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "condition clear");

            editor.Close();
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var cleared = BehaviourGraphModel.Parse(window.LoadedXml);
            Smoke.CheckTrue("reopen keeps the cleared condition",
                StateRoutes.Of(cleared).Routes.Single().ConditionId.Length == 0);
        });
    }

    private static void StructureAuthoringNotifyRoundTripsThroughSave()
    {
        Console.WriteLine("\nstructure authoring notify rows survive Save and reopen");
        byte[] fixture = StructureNotifyBytes();
        var seeded = BehaviourGraphModel.Parse(NativeXml.From(fixture));
        var seededState = seeded.Objects.Single(o => o.Class == "hkbStateMachineStateInfo");
        string payload = seeded.Get(seededState.Ref("enterNotifyEvents")!)!.StructLists["events"][0]["payload"];

        WithFixture(fixture, (window, path) =>
        {
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();

            byte[] before = File.ReadAllBytes(path);
            editor.AddEnterNotifyForTest(3);
            editor.AddExitNotifyForTest(1);
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("notify adds leave source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "notify add");

            editor.Close();
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            CheckNotifyRows(window.LoadedXml, new[] { 0, 1, 3 }, new[] { 2, 1 }, payload);

            editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();
            before = File.ReadAllBytes(path);
            editor.RemoveEnterNotifyForTest(1);
            editor.RemoveExitNotifyForTest(0);
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("notify removes leave source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "notify remove");

            editor.Close();
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            CheckNotifyRows(window.LoadedXml, new[] { 0, 3 }, new[] { 1 }, payload);
        });
    }

    private static void StructureAuthoringStateMachineRoundTripsThroughSave()
    {
        Console.WriteLine("\nstructure authoring state machines survive Save and reopen");
        WithFixture(MachineGraphBytes(), (window, path) =>
        {
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var editor = AdvancedAuthoringUi.OpenForTest(window);
            Dispatcher.UIThread.RunJobs();

            byte[] before = File.ReadAllBytes(path);
            editor.CreateStateMachineForTest("NestedMachine", "NestedFirst");
            Dispatcher.UIThread.RunJobs();
            Smoke.CheckTrue("state-machine create updates the document", window.IsDirty &&
                window.LoadedXml.Contains("NestedMachine", StringComparison.Ordinal));
            Smoke.CheckTrue("state-machine create leaves source bytes unchanged", File.ReadAllBytes(path).SequenceEqual(before));
            SaveAndCheck(window, path, before, "state-machine create");

            editor.Close();
            window.Open(path);
            Dispatcher.UIThread.RunJobs();
            var model = BehaviourGraphModel.Parse(window.LoadedXml);
            var machine = model.Objects.Single(o => o.Class == "hkbStateMachine" &&
                o.Str("name") == "NestedMachine");
            var first = AssertState(model, machine.Id, "NestedFirst");
            var graph = model.Objects.Single(o => o.Class == "hkbBehaviorGraph");
            Smoke.CheckTrue("reopen keeps the non-empty state machine", first != null);
            Smoke.CheckTrue("reopen keeps the first state generator", first!.GeneratorRef.Length > 0);
            Smoke.Check("reopen keeps the parent reference", "#" + machine.Id, graph.Str("rootGenerator"));
        });
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

    private static void SaveAndCheck(MainWindow window, string path, byte[] before, string label)
    {
        window.SaveForTest();
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue($"{label} writes the authored structure: {window.StatusForTest}",
            !File.ReadAllBytes(path).SequenceEqual(before) && !window.IsDirty);
    }

    private static void CheckNotifyRows(string xml, int[] enter, int[] exit, string payload)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var state = model.Objects.Single(o => o.Class == "hkbStateMachineStateInfo");
        var enterRows = model.Get(state.Ref("enterNotifyEvents")!)!.StructLists["events"];
        var exitRows = model.Get(state.Ref("exitNotifyEvents")!)!.StructLists["events"];
        Smoke.Check("enter notify row order", string.Join(",", enter),
            string.Join(",", enterRows.Select(row => row["id"])));
        Smoke.Check("exit notify row order", string.Join(",", exit),
            string.Join(",", exitRows.Select(row => row["id"])));
        Smoke.Check("untouched payload reference", payload, enterRows[0]["payload"]);
    }

    private static StateEditor.StateRow? AssertState(BehaviourGraphModel model, string machineId, string name) =>
        StateEditor.States(model, machineId).SingleOrDefault(state => state.Name == name);

    // A graph with somewhere to declare variables as well as a machine to hang states on.
    // OneMachineBytes holds only the machine, so variable authoring correctly refuses it.
    private static byte[] GraphBytes(params string[] additionalClasses)
    {
        var image = new OpenCommonwealth.Services.Hkx.PackfileImage();
        foreach (string tag in new[] { "__classnames__", "__data__" })
        {
            var bytes = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(bytes, 0);
            image.Sections.Add(new OpenCommonwealth.Services.Hkx.PackfileSection { TagBytes = bytes });
        }
        foreach (string className in new[]
                 { "hkbStateMachine", "hkbBehaviorGraphStringData", "hkbBehaviorGraphData", "hkbVariableValueSet" }
                 .Concat(additionalClasses))
            OpenCommonwealth.Services.Hkx.NativeAppend.Object(image, className);
        OpenCommonwealth.Services.Hkx.FixupOrder.Reorder(image);
        return image.Rebuild();
    }

    private static byte[] StructureNotifyBytes()
    {
        var session = new BehaviourAuthoringSession(GraphBytes());
        int machine = NativeGraphModel.FirstId;
        int first = session.AddEvent("First");
        int second = session.AddEvent("Second");
        int third = session.AddEvent("Third");
        int fourth = session.AddEvent("Fourth");
        var clip = session.AddClip("Idle", "Animations\\Idle.hkx");
        var state = session.AddState(machine, "Idle", clip.Id);
        session.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, first);
        session.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Enter, second);
        session.AddNotifyEvent(state.ObjectId, BehaviourAuthoringSession.NotifyPhase.Exit, third);
        var withRows = session.Build().Bytes;
        var model = BehaviourGraphModel.Parse(NativeXml.From(withRows));
        var info = model.Get(state.ObjectId.ToString())!;
        int array = int.Parse(info.Ref("enterNotifyEvents")!);
        var plan = new NativeAuthoringPlan(withRows);
        var payload = plan.AddObject("hkbIntEventPayload");
        plan.SetInt(payload.Id, "data", 42);
        plan.SetStructMember(array, "events", 0, "payload", payload.Reference);
        return plan.Apply().Bytes;
    }

    private static byte[] MachineGraphBytes()
    {
        var source = GraphBytes("hkbBehaviorGraph");
        var sourceModel = BehaviourGraphModel.Parse(NativeXml.From(source));
        int graph = int.Parse(sourceModel.Objects.Single(o => o.Class == "hkbBehaviorGraph").Id);
        int machine = int.Parse(sourceModel.Objects.Single(o => o.Class == "hkbStateMachine").Id);
        var session = new BehaviourAuthoringSession(source);
        var clip = session.AddClip("Seed", "Animations\\Seed.hkx");
        session.AddState(machine, "SeedState", clip.Id);
        session.AttachGenerator(graph, "rootGenerator", machine);
        return session.Build().Bytes;
    }

    private static byte[] StructureGraphBytes()
    {
        var source = GraphBytes();
        var session = new OpenCommonwealth.Services.Hkx.BehaviourAuthoringSession(source);
        int machine = OpenCommonwealth.Services.Hkx.NativeGraphModel.FirstId;
        int eventId = session.AddEvent("Go");
        var idle = session.AddClip("Idle", "Animations\\Idle.hkx");
        var walk = session.AddClip("Walk", "Animations\\Walk.hkx");
        var from = session.AddState(machine, "Idle", idle.Id);
        var to = session.AddState(machine, "Walk", walk.Id);
        session.AddTransition(machine, from.ObjectId, to.ObjectId, eventId);
        return session.Build().Bytes;
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
