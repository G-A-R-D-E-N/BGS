using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

public static class AssistantInspectionLimits
{
    public const int DefaultListLimit = 50;
    public const int HardListLimit = 200;
    public const int MaxDisplayString = 500;
}

public abstract class AssistantInspectionResult
{
    public string Source { get; set; } = "disk";
    public string Status { get; set; } = "ok";
    public string Code { get; set; } = "ok";
    public string Message { get; set; } = "";
    public List<string> Warnings { get; } = new();
    public bool Truncated { get; set; }
}

public sealed record AssistantFinding(
    string Level, string Where, string What, string ObjectId, bool BlocksSave);

public sealed record AssistantRoundTripLoss(
    string Kind, long? ObjectId, string Member, string Message);

public sealed record AssistantClassCount(string ClassName, int Count);

public sealed class BehaviorInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public bool Readable { get; init; }
    public int ObjectCount { get; init; }
    public string RootObjectId { get; init; } = "";
    public string RootClass { get; init; } = "";
    public List<AssistantClassCount> ClassCounts { get; } = new();
    public int ReferenceCount { get; init; }
    public int Errors { get; init; }
    public int GraphWarnings { get; init; }
    public int RoundTripLosses { get; init; }
    public List<AssistantFinding> Findings { get; } = new();
    public List<AssistantRoundTripLoss> RoundTrip { get; } = new();
}

public sealed record AssistantLink(
    string Role, string Declared, string Resolved, bool Exists, string Note);

public sealed record AssistantAnimationSource(string Animation, string Source);

public sealed class ProjectChainInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public string Root { get; init; } = "";
    public List<AssistantLink> Links { get; } = new();
    public List<string> Animations { get; } = new();
    public List<AssistantAnimationSource> AnimationSources { get; } = new();
    public int BoneCount { get; init; }
    public string SkeletonPath { get; init; } = "";
    public List<string> Problems { get; } = new();
}

public sealed class ProjectFileInspection
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public string Error { get; init; } = "";
    public int Errors { get; init; }
    public int GraphErrors { get; init; }
    public int GraphWarnings { get; init; }
    public int RoundTripLosses { get; init; }
    public List<AssistantFinding> Findings { get; } = new();
    public List<AssistantRoundTripLoss> RoundTrip { get; } = new();
    public bool Truncated { get; set; }
}

public sealed class ProjectInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public string Root { get; init; } = "";
    public int FilesFound { get; init; }
    public int FilesReturned { get; init; }
    public int Errors { get; init; }
    public int GraphWarnings { get; init; }
    public int Unreadable { get; init; }
    public int RoundTripLosses { get; init; }
    public int FilesWithRoundTripLosses { get; init; }
    public List<ProjectFileInspection> Files { get; } = new();
}

public sealed record AssistantSearchHit(
    string Path, string File, string Kind, string ObjectId,
    string ClassName, string Field, string Value);

public sealed record AssistantSearchProblem(string Path, string File, string Error);

public sealed class SearchInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public string Query { get; init; } = "";
    public int FilesFound { get; init; }
    public int FilesRead { get; init; }
    public int FilesUnreadable { get; init; }
    public List<AssistantSearchHit> Hits { get; } = new();
    public List<AssistantSearchProblem> Problems { get; } = new();
}

public sealed record AssistantAnnotation(float Time, string Text);

public sealed class AnimationInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public string AnimationClass { get; init; } = "";
    public bool Supported { get; init; }
    public float Duration { get; init; }
    public int NumFrames { get; init; }
    public float Fps { get; init; }
    public int NumTracks { get; init; }
    public int NumBlocks { get; init; }
    public int BoneCount { get; init; }
    public List<string> Bones { get; } = new();
    public int AnnotationCount { get; init; }
    public List<AssistantAnnotation> Annotations { get; } = new();
    public string OriginalSkeletonName { get; init; } = "";
    public int BlendHint { get; init; }
    public List<AssistantRoundTripLoss> RoundTrip { get; } = new();
}

public sealed record AssistantObjectField(
    string Name, string Kind, int Count, string Value, IReadOnlyList<string> Sample);

public sealed record AssistantReference(
    string HolderId, string Target, string Field, int Index, string Member, string How, string Path);

public sealed record AssistantElementSummary(string Name, string Value);

public sealed class ObjectInspection : AssistantInspectionResult
{
    public string Path { get; init; } = "";
    public string File { get; init; } = "";
    public string ObjectId { get; init; } = "";
    public string ClassName { get; init; } = "";
    public List<AssistantObjectField> Scalars { get; } = new();
    public List<AssistantObjectField> Lists { get; } = new();
    public List<AssistantObjectField> Structs { get; } = new();
    public List<AssistantObjectField> StructLists { get; } = new();
    public List<AssistantReference> OutgoingReferences { get; } = new();
    public List<AssistantReference> IncomingReferences { get; } = new();
    public List<AssistantElementSummary> ElementSummaries { get; } = new();
}
