using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public sealed record AssistantEditorSnapshot(
    string DocumentId,
    long Revision,
    string Xml,
    bool IsReadOnly,
    bool IsDirty,
    int UndoDepth,
    ProjectChain? ProjectChain = null);

public interface IAssistantEditorDocument
{
    AssistantEditorSnapshot Snapshot();
    bool TryCommit(string xml, out string failure);
}

public sealed record AssistantValidationDelta(
    IReadOnlyList<AssistantFinding> Introduced,
    IReadOnlyList<AssistantFinding> Resolved,
    IReadOnlyList<AssistantFinding> RemainingBlocking,
    bool Truncated);

public sealed class ClipAnimationChangePreview
{
    public bool Accepted { get; init; }
    public bool RequiresApproval { get; init; }
    public string Status { get; init; } = "error";
    public string Code { get; init; } = "internal_failure";
    public string Message { get; init; } = "";
    public string DocumentId { get; init; } = "";
    public long DocumentRevision { get; init; }
    public string ObjectId { get; init; } = "";
    public string ObjectClass { get; init; } = "";
    public string OldAnimationName { get; init; } = "";
    public string NewAnimationName { get; init; } = "";
    public bool WasDirty { get; init; }
    public int UndoDepth { get; init; }
    public AssistantValidationDelta ValidationDelta { get; init; } =
        new(Array.Empty<AssistantFinding>(), Array.Empty<AssistantFinding>(),
            Array.Empty<AssistantFinding>(), false);

    internal string CandidateXml { get; init; } = "";
}

public sealed record ClipAnimationChangeResult(
    bool Applied,
    bool Saved,
    string Status,
    string Code,
    string Message,
    string DocumentId,
    long NewDocumentRevision,
    bool IsDirty,
    int UndoStepsAdded,
    string ObjectId,
    string OldAnimationName,
    string NewAnimationName,
    AssistantValidationDelta? ValidationDelta);

public sealed class AssistantClipMutation
{
    private readonly IAssistantEditorDocument _document;

    public AssistantClipMutation(IAssistantEditorDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public ClipAnimationChangePreview Preview(string objectId, string animationName)
    {
        AssistantEditorSnapshot snapshot;
        try { snapshot = _document.Snapshot(); }
        catch (Exception) { return Failure("internal_failure", "active document state could not be read"); }

        if (snapshot.DocumentId.Length == 0 || snapshot.Xml.Length == 0)
            return Failure(snapshot, "no_active_document", "no editable behavior document is active");
        if (snapshot.IsReadOnly)
            return Failure(snapshot, "read_only_document", "the active document is read only");
        if (string.IsNullOrWhiteSpace(objectId))
            return Failure(snapshot, "invalid_argument", "objectId is required");
        if (string.IsNullOrWhiteSpace(animationName))
            return Failure(snapshot, "invalid_argument", "animationName must not be blank");

        string requested = animationName.Trim();
        try
        {
            var model = BehaviourGraphModel.Parse(snapshot.Xml);
            var target = model.Get(objectId);
            if (target == null)
                return Failure(snapshot, "object_not_found", $"object #{objectId} was not found");
            if (target.Class != "hkbClipGenerator")
                return Failure(snapshot, "unsupported_object", $"object #{objectId} is {target.Class}, not hkbClipGenerator");

            string old = target.Str("animationName");
            if (old == requested)
                return Failure(snapshot, "no_change", "the clip already uses that animationName");

            string candidate = HkxTextEdit.SetParamAt(snapshot.Xml, objectId, "animationName", requested);
            var before = Findings(snapshot.Xml, snapshot.ProjectChain);
            var after = Findings(candidate, snapshot.ProjectChain);

            return new ClipAnimationChangePreview
            {
                Accepted = true,
                RequiresApproval = true,
                Status = "approval_required",
                Code = "approval_required",
                Message = "BGS built the candidate. Approve it to change the active editor; it will remain unsaved.",
                DocumentId = snapshot.DocumentId,
                DocumentRevision = snapshot.Revision,
                ObjectId = objectId,
                ObjectClass = target.Class,
                OldAnimationName = old,
                NewAnimationName = requested,
                WasDirty = snapshot.IsDirty,
                UndoDepth = snapshot.UndoDepth,
                ValidationDelta = Delta(before, after),
                CandidateXml = candidate,
            };
        }
        catch (ArgumentException error)
        {
            return Failure(snapshot, "invalid_argument", error.Message.Split('\n')[0]);
        }
        catch (Exception)
        {
            return Failure(snapshot, "internal_failure", "clip change preview failed");
        }
    }

    public ClipAnimationChangeResult Apply(ClipAnimationChangePreview preview)
    {
        if (preview == null)
            return Refused("invalid_argument", "approval preview is required");
        if (!preview.Accepted || !preview.RequiresApproval || preview.CandidateXml.Length == 0)
            return Refused(preview.Code, "the supplied preview is not approvable");

        AssistantEditorSnapshot current;
        try { current = _document.Snapshot(); }
        catch (Exception) { return Refused("internal_failure", "active document state could not be read"); }

        if (current.DocumentId != preview.DocumentId || current.Revision != preview.DocumentRevision ||
            current.Xml.Length == 0)
            return Refused("stale_approval", "the active document changed; build and approve a new preview");

        try
        {
            var model = BehaviourGraphModel.Parse(current.Xml);
            var target = model.Get(preview.ObjectId);
            if (target == null || target.Class != preview.ObjectClass ||
                target.Str("animationName") != preview.OldAnimationName)
                return Refused("stale_approval", "the clip changed; build and approve a new preview");
        }
        catch (Exception)
        {
            return Refused("internal_failure", "the active clip could not be rechecked");
        }

        if (!_document.TryCommit(preview.CandidateXml, out string failure))
            return Refused("commit_refused", failure.Length > 0 ? failure : "the editor refused the candidate");

        AssistantEditorSnapshot after;
        try { after = _document.Snapshot(); }
        catch (Exception) { return Refused("internal_failure", "the applied document state could not be read"); }

        return new ClipAnimationChangeResult(
            Applied: true,
            Saved: false,
            Status: "applied",
            Code: "ok",
            Message: "The active editor changed and is dirty; the HKX is not saved yet.",
            DocumentId: after.DocumentId,
            NewDocumentRevision: after.Revision,
            IsDirty: after.IsDirty,
            UndoStepsAdded: Math.Max(0, after.UndoDepth - current.UndoDepth),
            ObjectId: preview.ObjectId,
            OldAnimationName: preview.OldAnimationName,
            NewAnimationName: preview.NewAnimationName,
            ValidationDelta: preview.ValidationDelta);
    }

    private static List<AssistantFinding> Findings(string xml, ProjectChain? chain) =>
        GraphValidator.Check(xml, chain).Select(Finding).ToList();

    private static AssistantFinding Finding(GraphValidator.Finding finding) =>
        new(finding.Level == GraphValidator.Level.Error ? "error" : "warning",
            finding.Where, finding.What, finding.ObjectId, finding.BlocksSave);

    private static AssistantValidationDelta Delta(
        IReadOnlyList<AssistantFinding> before, IReadOnlyList<AssistantFinding> after)
    {
        var beforeKeys = before.Select(Key).ToHashSet(StringComparer.Ordinal);
        var afterKeys = after.Select(Key).ToHashSet(StringComparer.Ordinal);
        var introduced = after.Where(f => !beforeKeys.Contains(Key(f)))
            .Take(AssistantInspectionLimits.HardListLimit).ToList();
        var resolved = before.Where(f => !afterKeys.Contains(Key(f)))
            .Take(AssistantInspectionLimits.HardListLimit).ToList();
        var blocking = after.Where(f => f.BlocksSave)
            .Take(AssistantInspectionLimits.HardListLimit).ToList();
        bool truncated = after.Count(f => f.BlocksSave) > AssistantInspectionLimits.HardListLimit ||
                         introduced.Count < after.Count(f => !beforeKeys.Contains(Key(f))) ||
                         resolved.Count < before.Count(f => !afterKeys.Contains(Key(f)));
        return new AssistantValidationDelta(introduced, resolved, blocking, truncated);
    }

    private static string Key(AssistantFinding finding) =>
        string.Join('\u001f', finding.Level, finding.Where, finding.What,
                    finding.ObjectId, finding.BlocksSave);

    private static ClipAnimationChangePreview Failure(string code, string message) =>
        new() { Code = code, Message = message, Status = "error" };

    private static ClipAnimationChangePreview Failure(
        AssistantEditorSnapshot snapshot, string code, string message) =>
        new()
        {
            DocumentId = snapshot.DocumentId,
            DocumentRevision = snapshot.Revision,
            WasDirty = snapshot.IsDirty,
            UndoDepth = snapshot.UndoDepth,
            Code = code,
            Message = message,
            Status = "error",
        };

    private static ClipAnimationChangeResult Refused(string code, string message) =>
        new(false, false, "error", code, message, "", 0, false, 0, "", "", "", null);
}
