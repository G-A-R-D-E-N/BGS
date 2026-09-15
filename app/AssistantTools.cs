using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public delegate bool AssistantPathAuthorization(
    string requested, out string canonical, out string code, out string message);

public sealed class AssistantTools
{
    private readonly AssistantInspection _inspection;
    private readonly AssistantClipMutation _mutation;
    private readonly AssistantPathAuthorization _authorize;
    private readonly Dictionary<string, AIFunction> _functions;
    private ClipAnimationChangePreview? _pendingClipAnimation;

    public AssistantTools(
        AssistantInspection inspection,
        AssistantClipMutation mutation,
        AssistantPathAuthorization authorize)
    {
        _inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        _mutation = mutation ?? throw new ArgumentNullException(nameof(mutation));
        _authorize = authorize ?? throw new ArgumentNullException(nameof(authorize));

        var functions = new[]
        {
            AIFunctionFactory.Create(InspectBehavior, "bgs.inspect_behavior",
                "Inspect a bounded behavior HKX."),
            AIFunctionFactory.Create(ResolveProjectChain, "bgs.resolve_project_chain",
                "Resolve the bounded project chain for an HKX."),
            AIFunctionFactory.Create(CheckProject, "bgs.check_project",
                "Check the bounded behavior project."),
            AIFunctionFactory.Create(SearchProject, "bgs.search_project",
                "Search a behavior project with bounded results."),
            AIFunctionFactory.Create(InspectAnimation, "bgs.inspect_animation",
                "Inspect bounded animation metadata."),
            AIFunctionFactory.Create(InspectObject, "bgs.inspect_object",
                "Inspect one bounded HKX object."),
            AIFunctionFactory.Create(PreviewSetClipAnimation, "bgs.set_clip_animation",
                "Build a preview for changing one active clip; explicit approval is required to apply it."),
        };
        _functions = functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<AIFunction> Functions => _functions.Values.ToArray();
    public bool HasPendingApproval => _pendingClipAnimation is not null;

    public BehaviorInspection InspectBehavior(
        string path, int findingLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectBehavior(canonical, findingLimit),
            static (code, message) => new BehaviorInspection { Status = "error", Code = code, Message = message });

    public ProjectChainInspection ResolveProjectChain(
        string path, int animationLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.ResolveProjectChain(canonical, animationLimit),
            static (code, message) => new ProjectChainInspection { Status = "error", Code = code, Message = message });

    public ProjectInspection CheckProject(
        string path, int fileLimit = AssistantInspectionLimits.DefaultListLimit,
        int findingLimitPerFile = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.CheckProject(canonical, fileLimit, findingLimitPerFile),
            static (code, message) => new ProjectInspection { Status = "error", Code = code, Message = message });

    public SearchInspection SearchProject(
        string path, string query, int limit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.SearchProject(canonical, query, limit),
            static (code, message) => new SearchInspection { Status = "error", Code = code, Message = message });

    public AnimationInspection InspectAnimation(
        string path, int annotationLimit = AssistantInspectionLimits.DefaultListLimit,
        int boneLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectAnimation(canonical, annotationLimit, boneLimit),
            static (code, message) => new AnimationInspection { Status = "error", Code = code, Message = message });

    public ObjectInspection InspectObject(
        string path, string objectId, int fieldLimit = AssistantInspectionLimits.DefaultListLimit,
        int referenceLimit = AssistantInspectionLimits.DefaultListLimit) =>
        Call(path, canonical => _inspection.InspectObject(canonical, objectId, fieldLimit, referenceLimit),
            static (code, message) => new ObjectInspection { Status = "error", Code = code, Message = message });

    public ClipAnimationChangePreview PreviewSetClipAnimation(string objectId, string animationName)
    {
        var preview = _mutation.Preview(objectId, animationName);
        _pendingClipAnimation = preview.Accepted ? preview : null;
        return preview;
    }

    public ClipAnimationChangeResult ApprovePendingClipAnimation()
    {
        if (_pendingClipAnimation is not { } preview)
            return new(false, false, "error", "no_pending_approval",
                "there is no pending clip-animation approval", "", 0, false, 0, "", "", "", null);

        _pendingClipAnimation = null;
        return _mutation.Apply(preview);
    }

    public void RejectPendingClipAnimation() => _pendingClipAnimation = null;

    private T Call<T>(string path, Func<string, T> operation,
                      Func<string, string, T> failure)
        where T : AssistantInspectionResult
    {
        if (!_authorize(path, out string canonical, out string code, out string message))
            return failure(code, message);
        return operation(canonical);
    }
}
