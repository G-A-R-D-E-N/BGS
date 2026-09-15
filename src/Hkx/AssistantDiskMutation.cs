using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

public sealed class DiskClipAnimationChangePreview
{
    public bool Accepted { get; init; }
    public bool RequiresApproval { get; init; }
    public string Status { get; init; } = "error";
    public string Code { get; init; } = "internal_failure";
    public string Message { get; init; } = "";
    public string Path { get; init; } = "";
    public string SourceSha256 { get; init; } = "";
    public string ObjectId { get; init; } = "";
    public string ObjectClass { get; init; } = "";
    public string OldAnimationName { get; init; } = "";
    public string NewAnimationName { get; init; } = "";
    public AssistantValidationDelta ValidationDelta { get; init; } =
        new(Array.Empty<AssistantFinding>(), Array.Empty<AssistantFinding>(),
            Array.Empty<AssistantFinding>(), false);

    internal string CurrentXml { get; init; } = "";
    internal string CandidateXml { get; init; } = "";
}

public sealed record DiskClipAnimationChangeRequest(
    string Path,
    string ObjectId,
    string OldAnimationName,
    string NewAnimationName,
    string SourceSha256,
    bool Approved);

public sealed record DiskClipAnimationChangeResult(
    bool Committed,
    bool Saved,
    string Status,
    string Code,
    string Message,
    string Path,
    string SourceSha256,
    string ObjectId,
    string OldAnimationName,
    string NewAnimationName,
    AssistantValidationDelta? ValidationDelta);

public sealed class AssistantDiskMutation
{
    public DiskClipAnimationChangePreview Preview(string path, string objectId, string animationName)
    {
        if (string.IsNullOrWhiteSpace(path)) return Failure("invalid_argument", "path is required");
        if (string.IsNullOrWhiteSpace(objectId)) return Failure("invalid_argument", "objectId is required");
        if (string.IsNullOrWhiteSpace(animationName))
            return Failure("invalid_argument", "animationName must not be blank");

        byte[] bytes;
        string xml;
        DocumentSourceStamp stamp;
        try
        {
            bytes = InputFilePolicy.ReadHkx(path);
            stamp = DocumentSourceStamp.Capture(bytes);
            xml = HkxTextEdit.TextOf(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failure("unreadable_file", "the HKX could not be read");
        }

        if (xml.Length == 0) return Failure("unsupported_hkx", "the HKX has no editable text model");

        ProjectChain? chain = null;
        try { chain = ProjectChain.Resolve(path); }
        catch (Exception) { }

        var document = new DiskDocument(path, xml, chain);
        var clip = new AssistantClipMutation(document).Preview(objectId, animationName);
        return new DiskClipAnimationChangePreview
        {
            Accepted = clip.Accepted,
            RequiresApproval = clip.RequiresApproval,
            Status = clip.Status,
            Code = clip.Code,
            Message = clip.Message,
            Path = path,
            SourceSha256 = stamp.Token,
            ObjectId = clip.ObjectId,
            ObjectClass = clip.ObjectClass,
            OldAnimationName = clip.OldAnimationName,
            NewAnimationName = clip.NewAnimationName,
            ValidationDelta = clip.ValidationDelta,
            CurrentXml = xml,
            CandidateXml = clip.CandidateXml,
        };
    }

    public DiskClipAnimationChangeResult Apply(DiskClipAnimationChangeRequest request)
    {
        if (request is null)
            return FailureResult("invalid_argument", "a change request is required");
        if (!request.Approved)
            return FailureResult("approval_required", "set approved=true only after showing the preview to the user");
        if (string.IsNullOrWhiteSpace(request.SourceSha256))
            return FailureResult("invalid_argument", "sourceSha256 is required");

        byte[] currentBytes;
        DocumentSourceStamp currentStamp;
        try
        {
            currentBytes = InputFilePolicy.ReadHkx(request.Path);
            currentStamp = DocumentSourceStamp.Capture(currentBytes);
        }
        catch (Exception) { return FailureResult("unreadable_file", "the HKX could not be read"); }

        if (!string.Equals(currentStamp.Token, request.SourceSha256, StringComparison.OrdinalIgnoreCase))
            return FailureResult("stale_approval", "the source changed; create and approve a new preview");

        var preview = Preview(request.Path, request.ObjectId, request.NewAnimationName);
        if (!preview.Accepted || preview.SourceSha256 != currentStamp.Token ||
            preview.OldAnimationName != request.OldAnimationName ||
            preview.NewAnimationName != request.NewAnimationName)
            return FailureResult("stale_approval", "the requested clip no longer matches the approved preview");

        DocumentSaveTransaction.Result saved;
        try
        {
            saved = DocumentSaveTransaction.Commit(
                request.Path, preview.CurrentXml, preview.CandidateXml, currentStamp);
        }
        catch (Exception) { return FailureResult("save_refused", "the verified HKX save failed"); }

        if (saved.Committed)
            return new(true, true, "committed", "ok", saved.Message, request.Path,
                currentStamp.Token, request.ObjectId, request.OldAnimationName,
                request.NewAnimationName, preview.ValidationDelta);

        return new(false, false, "error", saved.Unchanged ? "no_change" : "save_refused",
            saved.Message, request.Path, currentStamp.Token, request.ObjectId,
            request.OldAnimationName, request.NewAnimationName, preview.ValidationDelta);
    }

    private static DiskClipAnimationChangePreview Failure(string code, string message) =>
        new() { Code = code, Message = message, Status = "error" };

    private static DiskClipAnimationChangeResult FailureResult(string code, string message) =>
        new(false, false, "error", code, message, "", "", "", "", "", null);

    private sealed class DiskDocument : IAssistantEditorDocument
    {
        private readonly AssistantEditorSnapshot _snapshot;

        public DiskDocument(string path, string xml, ProjectChain? chain) =>
            _snapshot = new(path, 0, xml, false, false, 0, chain);

        public AssistantEditorSnapshot Snapshot() => _snapshot;

        public bool TryCommit(string xml, out string failure)
        {
            failure = "disk preview documents cannot commit directly";
            return false;
        }
    }
}
