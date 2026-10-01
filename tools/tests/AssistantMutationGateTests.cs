using System;
using System.IO;
using System.Linq;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("AssistantChatStore")]
public sealed class AssistantMutationGateTests : IDisposable
{
    private readonly string _folder;
    private readonly string _previousFolder;

    public AssistantMutationGateTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "bgs-assistant-gate-" + Guid.NewGuid().ToString("N"));
        _previousFolder = AssistantChatStore.Folder;
        AssistantChatStore.Folder = _folder;
    }

    public void Dispose()
    {
        AssistantChatStore.DeleteForTest = null;
        AssistantChatStore.Folder = _previousFolder;
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void ChatBCannotApproveAPreviewOwnedByChatA()
    {
        Fixture fixture = BuildTools();

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        fixture.Gate.MarkOwner("chat-a");

        ClipAnimationChangeResult refused = fixture.Gate.Approve("chat-b");

        Assert.False(refused.Applied);
        Assert.Equal("no_pending_approval", refused.Code);
        Assert.Equal(0, fixture.Document.Revision);
        Assert.False(fixture.Tools.HasPendingApproval,
            "a foreign approval attempt must fail closed and drop the dangling preview");
    }

    [Fact]
    public void OwnerCanApproveItsOwnPreview()
    {
        Fixture fixture = BuildTools();

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        fixture.Gate.MarkOwner("chat-a");

        ClipAnimationChangeResult applied = fixture.Gate.Approve("chat-a");

        Assert.True(applied.Applied);
        Assert.Equal("applied", applied.Status);
        Assert.Equal(1, fixture.Document.Revision);
        Assert.Contains("Approved.hkx", fixture.Document.Xml);
    }

    [Fact]
    public void StaleRevisionIsPreservedInsteadOfReportingApplied()
    {
        Fixture fixture = BuildTools();

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        fixture.Gate.MarkOwner("chat-a");
        fixture.Document.CommitOutsideEditor();

        ClipAnimationChangeResult stale = fixture.Gate.Approve("chat-a");

        Assert.False(stale.Applied);
        Assert.Equal("stale_approval", stale.Code);
        Assert.Equal(1, fixture.Document.Revision);
        Assert.DoesNotContain("Approved.hkx", fixture.Document.Xml);
    }

    [Fact]
    public void TransitionRejectDropsTheRealPreview()
    {
        Fixture fixture = BuildTools();

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        fixture.Gate.MarkOwner("chat-a");
        Assert.True(fixture.Tools.HasPendingApproval);

        fixture.Gate.Reject();

        Assert.False(fixture.Tools.HasPendingApproval);
        Assert.Null(fixture.Gate.OwnerChatId);
        Assert.Equal(0, fixture.Document.Revision);
    }

    [Fact]
    public void NewChatClearsTheRealPendingMutation()
    {
        Fixture fixture = BuildTools();
        using var controller = new AssistantConversationController(_ => null);
        controller.Mutations = fixture.Gate;
        AssistantConversationEntry a = controller.Active!;

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        a.MarkPendingApproval();
        fixture.Gate.MarkOwner(a.Chat.Id);
        Assert.True(a.ApprovalActive);

        AssistantConversationEntry? b = controller.NewChat();

        Assert.NotNull(b);
        Assert.False(fixture.Tools.HasPendingApproval);
        Assert.False(a.ApprovalActive);
        Assert.Equal(0, fixture.Document.Revision);
    }

    [Fact]
    public void SwitchingChatsRejectsTheOutgoingPreviewSoTheNextChatCannotApplyIt()
    {
        Fixture fixture = BuildTools();
        using var controller = new AssistantConversationController(_ => null);
        controller.Mutations = fixture.Gate;
        AssistantConversationEntry a = controller.Active!;
        AssistantConversationEntry b = controller.NewChat()!;
        Assert.True(controller.TrySelectById(a.Chat.Id));

        Assert.True(fixture.Tools.PreviewSetClipAnimation(fixture.ObjectId, Change).Accepted);
        a.MarkPendingApproval();
        fixture.Gate.MarkOwner(a.Chat.Id);

        Assert.True(controller.TrySelectById(b.Chat.Id));

        Assert.False(fixture.Tools.HasPendingApproval);
        Assert.False(a.ApprovalActive);
        ClipAnimationChangeResult refused = controller.ApproveActive(b);
        Assert.False(refused.Applied);
        Assert.Equal(0, fixture.Document.Revision);
    }

    private const string Change = "Animations\\Assistant\\Approved.hkx";

    private static Fixture BuildTools()
    {
        string path = Path.Combine(new[]
        {
            AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character",
            "Behaviors", "SingleAnimFurniture.hkx",
        });
        string xml = HkxTextEdit.TextOf(path);
        string id = BehaviourGraphModel.Parse(xml).Objects
            .First(objectInfo => objectInfo.Class == "hkbClipGenerator").Id;
        var document = new FakeDocument(xml, path);
        var tools = new AssistantTools(new AssistantInspection(), new AssistantClipMutation(document),
            Allow(path));
        var gate = new AssistantMutationGate(
            () => tools.HasPendingApproval,
            tools.RejectPendingClipAnimation,
            tools.ApprovePendingClipAnimation);
        return new Fixture(tools, gate, document, id);
    }

    private static AssistantPathAuthorization Allow(string expected) =>
        (string requested, out string canonical, out string code, out string message) =>
        {
            canonical = requested;
            code = "ok";
            message = "";
            return string.Equals(requested, expected, StringComparison.Ordinal);
        };

    private sealed record Fixture(
        AssistantTools Tools,
        AssistantMutationGate Gate,
        FakeDocument Document,
        string ObjectId);

    private sealed class FakeDocument : IAssistantEditorDocument
    {
        public FakeDocument(string xml, string id)
        {
            Xml = xml;
            DocumentId = id;
        }

        public string DocumentId { get; }
        public string Xml { get; private set; }
        public long Revision { get; private set; }
        public bool IsDirty { get; private set; }
        public int UndoDepth { get; private set; }

        public AssistantEditorSnapshot Snapshot() =>
            new(DocumentId, Revision, Xml, false, IsDirty, UndoDepth);

        public bool TryCommit(string xml, out string failure)
        {
            Xml = xml;
            Revision++;
            UndoDepth++;
            IsDirty = true;
            failure = "";
            return true;
        }

        public void CommitOutsideEditor() => Revision++;
    }
}
