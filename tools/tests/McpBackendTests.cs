using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BehaviourStudio.Mcp;
using ModelContextProtocol.Server;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class McpBackendTests
{
    [Fact]
    public void PathPolicyAllowsFilesUnderRootAndRejectsPrefixSiblings()
    {
        string parent = Directory.CreateTempSubdirectory("bgs-mcp-policy-parent").FullName;
        string root = Path.Combine(parent, "allowed");
        string sibling = Path.Combine(parent, "allowed-copy");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);
        string file = Path.Combine(root, "sample.hkx");
        string siblingFile = Path.Combine(sibling, "sample.hkx");
        File.WriteAllBytes(file, new byte[] { 0 });
        File.WriteAllBytes(siblingFile, new byte[] { 0 });

        try
        {
            Assert.True(McpPathPolicy.TryCreate(new[] { root }, out var policy, out _));
            Assert.True(policy!.TryAuthorize(file, out var canonical, out var allowedCode, out _));
            Assert.Equal(Path.GetFullPath(file), canonical);
            Assert.Equal("ok", allowedCode);
            Assert.False(policy.TryAuthorize(siblingFile, out _, out var deniedCode, out _));
            Assert.Equal("path_not_allowed", deniedCode);
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void PathPolicyRejectsTraversalAndMissingTargets()
    {
        string parent = Directory.CreateTempSubdirectory("bgs-mcp-policy-parent").FullName;
        string root = Path.Combine(parent, "allowed");
        Directory.CreateDirectory(root);
        string outside = Path.Combine(parent, "outside.hkx");
        File.WriteAllBytes(outside, new byte[] { 0 });
        try
        {
            Assert.True(McpPathPolicy.TryCreate(new[] { root }, out var policy, out _));
            string traversal = Path.Combine(root, "..", "outside.hkx");
            Assert.False(policy!.TryAuthorize(traversal, out _, out var traversalCode, out _));
            Assert.Equal("path_not_allowed", traversalCode);
            Assert.False(policy.TryAuthorize(Path.Combine(root, "missing.hkx"), out _, out var missingCode, out _));
            Assert.Equal("path_not_found", missingCode);
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void AdapterPassesAuthorizedFileToInspection()
    {
        string root = Directory.CreateTempSubdirectory("bgs-mcp-adapter").FullName;
        string target = Path.Combine(root, "sample.hkx");
        File.Copy(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), target);
        try
        {
            Assert.True(McpPathPolicy.TryCreate(new[] { root }, out var policy, out _));
            var tools = new McpTools(new AssistantInspection(), policy!, new AssistantDiskMutation());
            var result = tools.InspectBehavior(target, 1);

            Assert.Equal("ok", result.Status);
            Assert.Equal(target, result.Path);
            Assert.True(result.Readable);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void DiskMutationRequiresExactApprovalAndVerifiedSave()
    {
        string root = Directory.CreateTempSubdirectory("bgs-mcp-write").FullName;
        string target = Path.Combine(root, "SingleAnimFurniture.hkx");
        File.Copy(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), target);
        try
        {
            string xml = HkxTextEdit.TextOf(target);
            string id = BehaviourGraphModel.Parse(xml).Objects
                .First(o => o.Class == "hkbClipGenerator").Id;
            byte[] before = File.ReadAllBytes(target);
            var mutation = new AssistantDiskMutation();
            var preview = mutation.Preview(target, id, "Animations\\Assistant\\Written.hkx");

            Assert.True(preview.Accepted);
            Assert.True(preview.RequiresApproval);
            Assert.Equal(before, File.ReadAllBytes(target));

            var refused = mutation.Apply(new DiskClipAnimationChangeRequest(
                target, id, preview.OldAnimationName, preview.NewAnimationName,
                preview.SourceSha256, Approved: false));
            Assert.False(refused.Committed);
            Assert.Equal("approval_required", refused.Code);
            Assert.Equal(before, File.ReadAllBytes(target));

            var saved = mutation.Apply(new DiskClipAnimationChangeRequest(
                target, id, preview.OldAnimationName, preview.NewAnimationName,
                preview.SourceSha256, Approved: true));

            Assert.True(saved.Committed);
            Assert.True(saved.Saved);
            Assert.Contains("Written.hkx", HkxTextEdit.TextOf(target));
            Assert.True(File.Exists(target + ".bak"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void StdioDiscoveryIsReadOnlyAndPreviewCannotMutate()
    {
        string root = Directory.CreateTempSubdirectory("bgs-mcp-read-only").FullName;
        string target = Path.Combine(root, "SingleAnimFurniture.hkx");
        File.Copy(Fixture("Meshes/Actors/Character/Behaviors/SingleAnimFurniture.hkx"), target);
        byte[] before = File.ReadAllBytes(target);

        try
        {
            var names = typeof(McpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute is not null)
                .Select(attribute => attribute!.Name)
                .ToArray();
            Assert.Equal(7, names.Length);
            Assert.DoesNotContain("bgs.set_clip_animation", names);
            Assert.Contains("bgs.preview_set_clip_animation", names);

            Assert.DoesNotContain(typeof(McpTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                method => method.Name == "SetClipAnimation");
            string objectId = BehaviourGraphModel.Parse(HkxTextEdit.TextOf(target)).Objects
                .First(objectInfo => objectInfo.Class == "hkbClipGenerator").Id;
            var tools = new McpTools(new AssistantInspection(), CreatePolicy(root), new AssistantDiskMutation());
            var preview = tools.PreviewSetClipAnimation(target, objectId, "Animations\\Assistant\\Rejected.hkx");
            Assert.True(preview.Accepted);
            Assert.True(preview.RequiresApproval);
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.False(File.Exists(target + ".bak"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static McpPathPolicy CreatePolicy(string root) =>
        McpPathPolicy.TryCreate(new[] { root }, out var policy, out string error)
            ? policy!
            : throw new InvalidOperationException(error);

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla", relative.Replace('/', Path.DirectorySeparatorChar));
}
