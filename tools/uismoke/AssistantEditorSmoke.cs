using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

internal static class AssistantEditorSmoke
{
    internal static void Run()
    {
        string? previous = Settings.SettingsPathForTest;
        string previousChats = AssistantChatStore.Folder;
        string folder = Path.Combine(Path.GetTempPath(), "bgs-editor-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Settings.SettingsPathForTest = Path.Combine(folder, "settings.cfg");
        AssistantChatStore.Folder = Path.Combine(folder, "chats");
        Settings.TrySet("tour_done", "1", out _);
        try { RunCore(); ExportPathsStayWithinProject(folder); }
        finally
        {
            Settings.SettingsPathForTest = previous; AssistantChatStore.Folder = previousChats;
            Directory.Delete(folder, true);
        }
    }

    private static void ExportPathsStayWithinProject(string folder)
    {
        string project = Path.Combine(folder, "project");
        string outside = Path.Combine(folder, "outside");
        string destination = Path.Combine(project, "destination");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        string behavior = Path.Combine(project, "behavior.hkx");
        File.Copy(Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors", "Character",
            "Behaviors", "SingleAnimFurniture.hkx"), behavior);
        string report = Path.Combine(destination, "report.json");
        string outsideReport = Path.Combine(outside, "report.json");
        File.WriteAllText(outsideReport, "unchanged");
        var owner = new MainWindow();
        owner.Show();
        try
        {
            owner.Open(behavior); Pump();
            owner.CompareLoadedWith(behavior); Pump();
            var editor = new AssistantEditor(owner);
            var state = editor.State(query: "MainWindow").GetAwaiter().GetResult();
            Require("ordinary nested export is accepted", editor.File(state.Snapshot,
                state.Controls.Single().Id, "export_diff_json", report).GetAwaiter().GetResult().Accepted);
            Directory.Delete(destination);
            LinkDirectory(destination, outside);
            editor.Approve(); Pump();
            Require("approval-time linked export leaves outside file untouched", File.ReadAllText(outsideReport) == "unchanged");
            Require("linked parent export is refused", !owner.AssistantExportPathAllowed(report));
            Directory.Delete(destination);
            Directory.CreateDirectory(destination);
            Require("ordinary export remains allowed", owner.AssistantExportPathAllowed(report));
            Directory.CreateDirectory(project + "-sibling");
            Require("sibling-prefix export is refused", !owner.AssistantExportPathAllowed(Path.Combine(project + "-sibling", "report.json")));
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(report, outsideReport);
                Require("linked destination file is refused", !owner.AssistantExportPathAllowed(report));
                File.Delete(report);
            }
            Directory.Move(project, project + "-original");
            LinkDirectory(project, outside);
            Require("linked project root is refused", !owner.AssistantExportPathAllowed(Path.Combine(project, "root-report.json")));
            Directory.Delete(project);
            Directory.Move(project + "-original", project);
        }
        finally { owner.Close(); Pump(); }
    }

    private static void LinkDirectory(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, target); return; }
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create the export test junction.");
    }

    private static void RunCore()
    {
        var owner = new MainWindow();
        owner.Show(); Pump();
        var editor = new AssistantEditor(owner);
        var tools = new AssistantTools(new AssistantInspection(), new AssistantClipMutation(owner),
            (string path, out string canonical, out string code, out string message) =>
            { canonical = path; code = "ok"; message = ""; return true; }, editor);
        try
        {
            Require("provider tool registration", owner.CreateAssistantTools().Functions.Select(tool => tool.Name)
                .Intersect(new[] { "bgs.editor_state", "bgs.editor_action", "bgs.editor_file" }).Count() == 3);
            var state = editor.State(0, 200).GetAwaiter().GetResult();
            var launch = state.Controls.Single(control => control.Kind == "Button" && control.Label == "Open skeleton editor");
            Require("launch waits for approval", tools.PreviewEditorAction(state.Snapshot, launch.Id, "click").GetAwaiter().GetResult().RequiresApproval &&
                !owner.OwnedWindows.OfType<RigAuthoringWindow>().Any());
            var gate = new AssistantMutationGate(() => tools.HasPendingApproval, tools.RejectPendingClipAnimation, tools.ApprovePendingClipAnimation);
            gate.MarkOwner("a");
            Require("foreign chat refused", !gate.Approve("b").Applied && !editor.HasPending);
            tools.PreviewEditorAction(state.Snapshot, launch.Id, "click").GetAwaiter().GetResult(); gate.MarkOwner("a");
            Require("approved dispatch is not a save claim", gate.Approve("a") is { Applied: true, Saved: false, Code: "dispatched" });
            Pump();
            var rig = owner.OwnedWindows.OfType<RigAuthoringWindow>().Single();
            try
            {
                state = editor.State(0, 200).GetAwaiter().GetResult();
                string skeleton = Path.Combine(Path.GetDirectoryName(Settings.SettingsPathForTest)!, "skeleton.hkx");
                File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "TurretMountedSkeleton.hkx"), skeleton);
                Require("named file proposal", tools.PreviewEditorFile(state.Snapshot,
                    state.Controls.Single(control => control.Kind == "RigAuthoringWindow").Id, "skeleton", skeleton).GetAwaiter().GetResult().RequiresApproval);
                Require("file unopened until approval", rig.GetVisualDescendants().OfType<SkeletonView>().First().DrawnBones == 0);
                tools.ApprovePendingClipAnimation(); Pump();
                Require("native skeleton loader used", rig.GetVisualDescendants().OfType<SkeletonView>().First().DrawnBones > 0);
                state = editor.State(0, 200).GetAwaiter().GetResult();
                var name = state.Controls.Single(control => control.Label == "BoneName");
                tools.PreviewEditorAction(state.Snapshot, name.Id, "text", "proposed").GetAwaiter().GetResult();
                var box = rig.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "BoneName");
                string original = box.Text!;
                tools.RejectPendingClipAnimation(); Require("reject preserves field", box.Text == original);
                tools.PreviewEditorAction(state.Snapshot, name.Id, "text", "proposed").GetAwaiter().GetResult();
                box.Text = "user changed this";
                Require("stale approval refused", tools.ApprovePendingClipAnimation().Code == "stale_approval"); box.Text = original;
                state = editor.State(query: "BoneName").GetAwaiter().GetResult();
                var field = state.Controls.Single(control => control.Kind == "TextBox");
                tools.PreviewEditorAction(state.Snapshot, field.Id, "text", "first proposal").GetAwaiter().GetResult();
                string oldApproval = tools.PendingApprovalId;
                tools.PreviewEditorAction(state.Snapshot, field.Id, "text", "second proposal").GetAwaiter().GetResult();
                Require("approval is bound to the displayed proposal", tools.ApprovePendingAction(oldApproval).Code == "stale_approval" && box.Text == original);
                void Act(string label, string action = "click", string value = "")
                {
                    var current = editor.State(query: label).GetAwaiter().GetResult();
                    var target = current.Controls.Single(control => control.Label == label && control.Actions.Contains(action));
                    Require($"propose {label}", tools.PreviewEditorAction(current.Snapshot, target.Id, action, value).GetAwaiter().GetResult().Accepted);
                    Require($"dispatch {label}", tools.ApprovePendingAction(tools.PendingApprovalId).Code == "dispatched"); Pump();
                }
                byte[] before = File.ReadAllBytes(skeleton);
                Act("BoneName", "text", "Assistant Edited Root"); Act("Apply bone");
                Require("edit stays in memory", before.SequenceEqual(File.ReadAllBytes(skeleton)));
                Act("Undo skeleton"); Require("native undo restores bone", box.Text == original);
                Act("Redo skeleton"); Act("Save skeleton");
                Require("native verified save survives reopen", new SkeletonDocument(skeleton).Skeletons[0].BoneNames[0] == "Assistant Edited Root");
                Require("native backup preserved", File.ReadAllBytes(skeleton + ".bak").SequenceEqual(before));
                state = editor.State(0, 200).GetAwaiter().GetResult();
                var view = state.Controls.Single(control => control.Kind == "SkeletonView");
                Require("out-of-bounds pointer refused", !tools.PreviewEditorAction(state.Snapshot, view.Id, "drag", x: -1).GetAwaiter().GetResult().Accepted);
                Require("bounded native wheel proposal", tools.PreviewEditorAction(state.Snapshot, view.Id, "wheel", "1", x: view.Width / 2, y: view.Height / 2).GetAwaiter().GetResult().Accepted);
                Require("native wheel dispatched", tools.ApprovePendingClipAnimation().Code == "dispatched"); Pump();
                state = editor.State(query: "SkeletonView").GetAwaiter().GetResult();
                view = state.Controls.Single(control => control.Kind == "SkeletonView");
                tools.PreviewEditorAction(state.Snapshot, view.Id, "wheel", "1", x: view.Width / 2, y: view.Height / 2).GetAwaiter().GetResult();
                rig.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Fit preview")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Require("camera changes invalidate gestures", tools.ApprovePendingClipAnimation().Code == "stale_approval");
                Require("scene exposes bone screen coordinates", view.Items.Any(item => item.Contains("Position")));
                var tabs = rig.GetVisualDescendants().OfType<TabControl>().Single();
                for (int index = 0; index < 3; index++)
                {
                    tabs.SelectedIndex = index; Pump();
                    state = editor.State(0, 200).GetAwaiter().GetResult();
                    Require($"rig tab {index} controls", state.Controls.Any(control => control.Window == rig.Title && control.Kind == "SkeletonView"));
                }
            }
            finally { rig.Close(); Pump(); }
            var mainTabs = owner.GetVisualDescendants().OfType<TabControl>().First();
            foreach (var tab in mainTabs.Items.OfType<TabItem>())
            {
                mainTabs.SelectedItem = tab; Pump();
                Require($"{tab.Header} controls", editor.State(0, 200).GetAwaiter().GetResult().Controls.Any(control => control.Kind == "Button"));
            }
            state = editor.State(query: "MainWindow").GetAwaiter().GetResult();
            Require("export refuses account destinations", tools.PreviewEditorFile(state.Snapshot,
                state.Controls.Single(control => control.Kind == "MainWindow").Id, "export_diff_json",
                Path.Combine(Path.GetTempPath(), "account.json")).GetAwaiter().GetResult().Code == "path_not_allowed");
            var password = new Window { Content = new TextBox { PasswordChar = '*', Text = "private-test-token" } };
            password.Show(owner); Pump();
            try { Require("password excluded", !System.Text.Json.JsonSerializer.Serialize(editor.State(0, 200).GetAwaiter().GetResult()).Contains("private-test-token")); }
            finally { password.Close(); }
            Require("approval controls excluded", editor.State(0, 200).GetAwaiter().GetResult().Controls.All(control => control.Label != "Approve action"));
            mainTabs.SelectedIndex = 0; Pump();
            var session = new EditorSession(tools, editor);
            owner.AssistantUiForTest.SetSessionForTest(session, tools);
            EditorShell.Find(owner.Content as Control)!.ChatButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump();
            owner.AssistantUiForTest.Pane.Composer.Text = "Open physics simulation";
            owner.AssistantUiForTest.Pane.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Send")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump();
            Require("real conversation displays action approval", owner.AssistantUiForTest.ApprovalVisible && session.Calls == 1);
            owner.AssistantUiForTest.Pane.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Approve action")
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Pump();
            Require("approved interaction continues through result inspection", session.Calls == 2);
            Require("continuation is saved as an editor event", AssistantChatStore.Load(owner.AssistantUiForTest.ActiveChatIdForTest!)!
                .Messages.Any(message => message.Role == AssistantChatRole.Tool && message.ToolName == "bgs.editor_action"));
            foreach (var child in owner.OwnedWindows.OfType<RigAuthoringWindow>().ToArray()) child.Close();
            owner.AssistantUiForTest.Pane.Composer.Text = "private-assistant-draft";
            Require("composer and chat transcript excluded", !System.Text.Json.JsonSerializer.Serialize(editor.State(0, 200).GetAwaiter().GetResult())
                .Contains("private-assistant-draft"));
            var queued = new EditorSession(tools, editor);
            owner.AssistantUiForTest.SetSessionForTest(queued, tools);
            void Send(string prompt)
            {
                owner.AssistantUiForTest.Pane.Composer.Text = prompt;
                owner.AssistantUiForTest.Pane.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Send")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            }
            void ApprovePane()
            {
                owner.AssistantUiForTest.Pane.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Approve action")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            }
            Send("Open physics simulation"); Send("queued follow-up");
            Require("queue pauses at approval", queued.Calls == 1 && tools.HasPendingApproval);
            ApprovePane();
            Require("queue resumes after approval", queued.Calls == 2 && queued.LastPrompt.Contains("queued follow-up"));
            foreach (var child in owner.OwnedWindows.OfType<RigAuthoringWindow>().ToArray()) child.Close();
            var dismiss = new Button { Content = "Dismiss fixture" };
            var modal = new Window { Title = "BGS fixture dialog", Content = dismiss };
            dismiss.Click += (_, _) => modal.Close();
            var launchButton = owner.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Open physics simulation");
            EventHandler<Avalonia.Interactivity.RoutedEventArgs> openModal = (_, _) => { _ = modal.ShowDialog(owner); };
            launchButton.Click += openModal;
            try
            {
                var modalSession = new EditorSession(tools, editor, true);
                owner.AssistantUiForTest.SetSessionForTest(modalSession, tools);
                Send("Open the fixture dialog and dismiss it"); ApprovePane();
                var approval = modal.OwnedWindows.OfType<AssistantApprovalWindow>().Single();
                Require("modal has an accessible approval window", approval.IsVisible && modal.IsDialog);
                Require("background window actions are disabled", !editor.State(query: "MainWindow").GetAwaiter().GetResult().Controls.Single().Enabled);
                Require("modal approval buttons remain excluded", editor.State(0, 200).GetAwaiter().GetResult().Controls.All(control => control.Label != "Approve action"));
                approval.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Approve action")
                    .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
                Require("approved modal interaction continues", !modal.IsVisible && modalSession.Calls == 3);
            }
            finally
            {
                launchButton.Click -= openModal; modal.Close();
                foreach (var child in owner.OwnedWindows.OfType<RigAuthoringWindow>().ToArray()) child.Close();
            }
            state = editor.State(query: "MainWindow").GetAwaiter().GetResult();
            string behavior = Path.GetFullPath(Path.Combine("tools", "tests", "fixtures", "vanilla", "Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx"));
            Require("behavior file proposal", tools.PreviewEditorFile(state.Snapshot, state.Controls.Single().Id, "open", behavior).GetAwaiter().GetResult().Accepted);
            tools.ApprovePendingAction(tools.PendingApprovalId); Pump();
            state = editor.State(query: "MainWindow").GetAwaiter().GetResult();
            Require("loaded project export refuses outside destinations", !tools.PreviewEditorFile(state.Snapshot,
                state.Controls.Single().Id, "export_diff_json", Path.Combine(Path.GetTempPath(), "outside-bgs-project.json")).GetAwaiter().GetResult().Accepted);
            Require("loaded project export accepts inside destinations", tools.PreviewEditorFile(state.Snapshot,
                state.Controls.Single().Id, "export_diff_json", Path.Combine(Path.GetDirectoryName(behavior)!, "assistant-export.json")).GetAwaiter().GetResult().Accepted);
            tools.RejectPendingClipAnimation();
            mainTabs.SelectedItem = mainTabs.Items.OfType<TabItem>().Single(tab => tab.Header?.ToString() == "Graph"); Pump();
            owner.Canvas.FrameAll(); Pump();
            state = editor.State(query: "GraphView").GetAwaiter().GetResult();
            var graph = state.Controls.Single(control => control.Kind == "GraphView");
            using var node = System.Text.Json.JsonDocument.Parse(graph.Items.First());
            var position = node.RootElement.GetProperty("Position");
            double px = position[0].GetDouble() + node.RootElement.GetProperty("Width").GetDouble() / 2;
            double py = position[1].GetDouble() + 5;
            Require("graph gesture proposal", tools.PreviewEditorAction(state.Snapshot, graph.Id, "pointer", "double", x: px, y: py).GetAwaiter().GetResult().Accepted);
            Require("gesture approval describes its coordinates", tools.PendingDescription.Contains(" at (") && tools.PendingDescription.Contains("Left"));
            tools.ApprovePendingAction(tools.PendingApprovalId); Pump();
            Require("native graph pointer selects the observed node", owner.AssistantContextSnapshot.SelectedObjectId == node.RootElement.GetProperty("Id").GetString());
            state = editor.State(query: "GraphView").GetAwaiter().GetResult();
            graph = state.Controls.Single(control => control.Kind == "GraphView");
            tools.PreviewEditorAction(state.Snapshot, graph.Id, "pointer", x: px, y: py, button: "Right").GetAwaiter().GetResult();
            tools.ApprovePendingAction(tools.PendingApprovalId); Pump();
            state = editor.State(query: "MenuItem", limit: 200).GetAwaiter().GetResult();
            Require("native graph menu is discoverable", state.Controls.Any(control => control.Kind == "MenuItem" && control.Label.StartsWith("Highlight the paths")));
            var highlight = state.Controls.First(control => control.Kind == "MenuItem" && control.Label.StartsWith("Highlight the paths"));
            tools.PreviewEditorAction(state.Snapshot, highlight.Id, "click").GetAwaiter().GetResult();
            tools.ApprovePendingAction(tools.PendingApprovalId); Pump();
            Require("native menu handler runs", owner.Canvas.HighlightId == node.RootElement.GetProperty("Id").GetString());
            Console.WriteLine("Assistant editor smoke PASS");
        }
        finally { tools.RejectPendingClipAnimation(); owner.Close(); Pump(); }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Require(string label, bool condition)
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {label}");
        if (!condition) throw new InvalidOperationException(label);
    }

    private sealed class EditorSession(AssistantTools tools, AssistantEditor editor, bool modal = false) : IAssistantSession
    {
        public int Calls;
        public string LastPrompt = "";
        public async Task<AssistantReply> SendAsync(string prompt, AssistantContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPrompt = prompt;
            if (Calls == 1 || modal && Calls == 2)
            {
                var state = await editor.State(query: Calls == 1 ? "Open physics simulation" : "Dismiss fixture");
                await tools.PreviewEditorAction(state.Snapshot, state.Controls.Single(control => control.Kind == "Button").Id, "click");
                return new("ok", "Review the launch action.", 1, new[] { new AssistantToolActivity("bgs.editor_action", true) }, true);
            }
            await editor.State();
            return new("ok", "The tool is open.", 1, Array.Empty<AssistantToolActivity>(), false);
        }
        public void ClearHistory() { }
        public void Dispose() { }
    }
}
