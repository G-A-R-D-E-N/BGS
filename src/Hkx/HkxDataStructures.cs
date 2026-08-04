using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public struct HkxBonePose
{
    public Vector3 Translation;
    public Quaternion Rotation;
    public Vector3 Scale;

    public HkxBonePose()
    {
        Translation = Vector3.Zero;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    public HkxBonePose(Vector3 translation, Quaternion rotation, Vector3 scale)
    {
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
    }
}

public class HkxSkeleton
{
    public string Name { get; set; } = "";
    public List<string> BoneNames { get; set; } = new();
    public List<int> ParentIndices { get; set; } = new();
    public List<HkxBonePose> ReferencePose { get; set; } = new();
    public List<bool> LockTranslation { get; set; } = new();
    public List<string> FloatSlots { get; set; } = new();
    public List<float> ReferenceFloats { get; set; } = new();
}

public class HkxTrackData
{
    public List<Vector3> Translations { get; set; } = new();
    public List<Quaternion> Rotations { get; set; } = new();
    public List<Vector3> Scales { get; set; } = new();

    /// Whether this track's scale is worth showing. Almost every track in the game is a flat 1,1,1,
    /// so printing all of them hides the ones that are not, and a track that only looks unscaled
    /// because the decode returned nothing is a different thing from one that really is 1,1,1.
    public static bool IsScaled(HkxTrackData track)
    {
        foreach (var s in track.Scales)
            if (Math.Abs(s.X - 1f) > ScaleEpsilon
             || Math.Abs(s.Y - 1f) > ScaleEpsilon
             || Math.Abs(s.Z - 1f) > ScaleEpsilon) return true;
        return false;
    }

    public const float ScaleEpsilon = 0.0001f;
}

public class HkxAnnotation
{
    public float Time { get; set; }
    public string Text { get; set; } = "";
}

public class HkxAnimationData
{
    public float Duration { get; set; }
    public int NumFrames { get; set; }
    public int NumTracks { get; set; }
    public int NumBlocks { get; set; }
    public int MaxFramesPerBlock { get; set; } = 256;
    public float BlockDuration { get; set; } = 8.5f;
    public float FrameDuration { get; set; } = 1.0f / 30.0f;

    public List<string> BoneNames { get; set; } = new();
    public List<HkxTrackData> Tracks { get; set; } = new();
    public List<HkxAnnotation> Annotations { get; set; } = new();
    public List<int> TrackToBoneIndices { get; set; } = new();
    public string OriginalSkeletonName { get; set; } = "";
    public int BlendHint { get; set; }

    /// The hka*Animation class found in the file, or empty if it holds no animation object at all.
    /// Havok defines several and Fallout 4 ships the two below; anything else parses to no tracks,
    /// and callers need to tell that apart from an animation that is genuinely empty.
    public string AnimationClass { get; set; } = "";

    public static readonly string[] DecodedAnimationClasses =
    {
        "hkaSplineCompressedAnimation",
        "hkaLosslessCompressedAnimation",
    };

    public static string SupportedAnimationClasses => string.Join(" and ", DecodedAnimationClasses);

    public bool HasUnsupportedAnimation =>
        AnimationClass.Length > 0 && Array.IndexOf(DecodedAnimationClasses, AnimationClass) < 0;

    public HkxSkeleton? Skeleton { get; set; }

    public string GetSummary()
    {
        float fps = FrameDuration > 0 ? 1.0f / FrameDuration : 0;
        return $"Duration: {Duration:F4}s, Frames: {NumFrames}, FPS: {fps:F1}, Tracks: {NumTracks}, Bones: {BoneNames.Count}, Annotations: {Annotations.Count}";
    }
}
