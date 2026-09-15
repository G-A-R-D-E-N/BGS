using System.ComponentModel;
using OpenCommonwealth.Services.Hkx;
using ModelContextProtocol.Server;

namespace BehaviourStudio.Mcp;

[McpServerToolType]
public sealed class McpTools
{
    private readonly AssistantInspection _inspection;
    private readonly McpPathPolicy _paths;
    private readonly AssistantDiskMutation _disk;

    public McpTools(AssistantInspection inspection, McpPathPolicy paths, AssistantDiskMutation disk)
    {
        _inspection = inspection;
        _paths = paths;
        _disk = disk;
    }

    [McpServerTool(Name = "bgs.inspect_behavior"), Description("Inspect a behavior HKX with bounded graph and round-trip diagnostics.")]
    public BehaviorInspection InspectBehavior(string path, int findingLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectBehavior(canonical, findingLimit), static (code, message) =>
            new BehaviorInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.resolve_project_chain"), Description("Resolve the bounded project, character, skeleton, and animation chain for an HKX.")]
    public ProjectChainInspection ResolveProjectChain(string path, int animationLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.ResolveProjectChain(canonical, animationLimit), static (code, message) =>
            new ProjectChainInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.check_project"), Description("Check the behavior files belonging to an HKX project with bounded findings.")]
    public ProjectInspection CheckProject(string path, int fileLimit = AssistantInspectionLimits.DefaultListLimit,
                                          int findingLimitPerFile = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.CheckProject(canonical, fileLimit, findingLimitPerFile), static (code, message) =>
            new ProjectInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.search_project"), Description("Search a project for bounded behavior, symbol, field, and asset matches.")]
    public SearchInspection SearchProject(string path, string query,
                                          int limit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.SearchProject(canonical, query, limit), static (code, message) =>
            new SearchInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.inspect_animation"), Description("Inspect bounded animation metadata and annotations without returning frame transforms.")]
    public AnimationInspection InspectAnimation(string path,
                                                int annotationLimit = AssistantInspectionLimits.DefaultListLimit,
                                                int boneLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectAnimation(canonical, annotationLimit, boneLimit), static (code, message) =>
            new AnimationInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.inspect_object"), Description("Inspect one bounded HKX object with fields and reference sites.")]
    public ObjectInspection InspectObject(string path, string objectId,
                                          int fieldLimit = AssistantInspectionLimits.DefaultListLimit,
                                          int referenceLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectObject(canonical, objectId, fieldLimit, referenceLimit), static (code, message) =>
            new ObjectInspection { Status = "error", Code = code, Message = message });

    [McpServerTool(Name = "bgs.preview_set_clip_animation"),
     Description("Preview changing one clip animationName; this never writes the file.")]
    public DiskClipAnimationChangePreview PreviewSetClipAnimation(
        string path, string objectId, string animationName)
    {
        if (!_paths.TryAuthorize(path, out string canonical, out string code, out string message))
            return new DiskClipAnimationChangePreview { Status = "error", Code = code, Message = message };
        return _disk.Preview(canonical, objectId, animationName);
    }

    private T Call<T>(string path, Func<string, T> operation, Func<string, string, T> failure)
        where T : AssistantInspectionResult
    {
        if (!_paths.TryAuthorize(path, out string canonical, out string code, out string message))
            return failure(code, message);
        return operation(canonical);
    }
}
