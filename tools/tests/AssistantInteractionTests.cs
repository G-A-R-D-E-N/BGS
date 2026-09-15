using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BehaviourStudio.App;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantInteractionTests
{
    [Fact]
    public void ClipPreviewRequiresApprovalAndApplyRechecksRevision()
    {
        string xml = HkxTextEdit.TextOf(Fixture("Meshes", "Actors", "Character", "Behaviors",
            "SingleAnimFurniture.hkx"));
        var model = BehaviourGraphModel.Parse(xml);
        string id = model.Objects.First(o => o.Class == "hkbClipGenerator").Id;
        var document = new FakeDocument(xml);
        var mutation = new AssistantClipMutation(document);

        var preview = mutation.Preview(id, "Animations\\Assistant\\Approved.hkx");

        Assert.True(preview.Accepted);
        Assert.True(preview.RequiresApproval);
        Assert.Equal(xml, document.Xml);
        Assert.Equal(0, document.Revision);

        var applied = mutation.Apply(preview);

        Assert.True(applied.Applied);
        Assert.False(applied.Saved);
        Assert.True(applied.IsDirty);
        Assert.Equal(1, applied.UndoStepsAdded);
        Assert.Contains("Approved.hkx", document.Xml);

        var stale = mutation.Preview(id, "Animations\\Assistant\\Second.hkx");
        document.CommitOutsideEditor();
        var refused = mutation.Apply(stale);

        Assert.False(refused.Applied);
        Assert.Equal("stale_approval", refused.Code);
    }

    [Fact]
    public void AssistantToolsExposeSixReadsAndOnePreviewOnlyWrite()
    {
        string path = Fixture("Meshes", "Actors", "Character", "Behaviors", "SingleAnimFurniture.hkx");
        string xml = HkxTextEdit.TextOf(path);
        var document = new FakeDocument(xml, path);
        var model = BehaviourGraphModel.Parse(xml);
        string id = model.Objects.First(o => o.Class == "hkbClipGenerator").Id;
        var tools = new AssistantTools(new AssistantInspection(), new AssistantClipMutation(document),
            Allow(path));

        Assert.Equal(7, tools.Functions.Count);
        Assert.Contains(tools.Functions, tool => tool.Name == "bgs.set_clip_animation");

        var preview = tools.PreviewSetClipAnimation(id, "Animations\\Assistant\\Approved.hkx");
        Assert.True(preview.RequiresApproval);
        Assert.True(tools.HasPendingApproval);
        Assert.Equal(0, document.Revision);

        var applied = tools.ApprovePendingClipAnimation();

        Assert.True(applied.Applied);
        Assert.False(tools.HasPendingApproval);
    }

    [Fact]
    public async Task SessionRunsOneToolAtATimeAndReturnsFinalAnswer()
    {
        var function = AIFunctionFactory.Create(() => new { status = "ok" }, "bgs.test", "test");
        var client = new FakeChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call-1", "bgs.test", new Dictionary<string, object?>()),
            })),
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "The inspected file is valid.")));
        using var session = new AssistantSession(client, new[] { function });

        var reply = await session.SendAsync("Check this file.", Context());

        Assert.Equal("ok", reply.Status);
        Assert.Equal("The inspected file is valid.", reply.Text);
        Assert.Single(reply.Tools);
        Assert.Equal(2, client.CallCount);
        Assert.False(client.Options!.AllowMultipleToolCalls);
        Assert.Contains(client.Messages.Last().Contents, content => content is FunctionResultContent);
    }

    [Fact]
    public async Task SessionStopsAtSixToolIterations()
    {
        var function = AIFunctionFactory.Create(() => "still working", "bgs.test", "test");
        var call = new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call", "bgs.test", new Dictionary<string, object?>()),
        }));
        using var session = new AssistantSession(new FakeChatClient(call, call, call, call, call, call, call),
            new[] { function });

        var reply = await session.SendAsync("Keep checking.", Context());

        Assert.Equal("iteration_limit", reply.Status);
        Assert.Equal(6, reply.Iterations);
    }

    private static AssistantContext Context() => new(
        "fixture", "0", "fixture.hkx", "Graph", "1", "hkbClipGenerator", false, false, 0, "", "");

    private static AssistantPathAuthorization Allow(string expected) =>
        (string requested, out string canonical, out string code, out string message) =>
        {
            canonical = requested;
            code = "ok";
            message = "";
            return string.Equals(requested, expected, StringComparison.Ordinal);
        };

    private static string Fixture(params string[] parts) =>
        System.IO.Path.Combine(new[] { AppContext.BaseDirectory, "fixtures", "vanilla" }.Concat(parts).ToArray());

    private sealed class FakeDocument : IAssistantEditorDocument
    {
        public FakeDocument(string xml, string id = "fixture.hkx")
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

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses;

        public FakeChatClient(params ChatResponse[] responses) => _responses = new(responses);

        public int CallCount { get; private set; }
        public IList<ChatMessage> Messages { get; private set; } = new List<ChatMessage>();
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Messages = messages.ToList();
            Options = options;
            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
