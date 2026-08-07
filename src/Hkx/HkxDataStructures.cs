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

    // Which channels the animation actually drives, per axis, because a Havok track mask is per axis.
    // A channel left clear is not a channel set to zero: the bone keeps whatever the skeleton's
    // reference pose says. Both decoders prefill a cleared channel with 0, identity or 1, which is
    // what the engine does to the transform before it fills anything in, and is indistinguishable
    // afterwards from a bone genuinely sitting at the origin. Posing without this collapses every
    // rotation-only bone onto its parent, which is most of a character.
    public bool[] TranslationAnimated { get; } = new bool[3];
    public bool[] ScaleAnimated { get; } = new bool[3];
    public bool RotationAnimated { get; set; }

    public bool AnyTranslationAnimated => TranslationAnimated[0] || TranslationAnimated[1] || TranslationAnimated[2];

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
        "hkaInterleavedUncompressedAnimation",
    };

    public static string SupportedAnimationClasses => string.Join(" and ", DecodedAnimationClasses);

    public bool HasUnsupportedAnimation =>
        AnimationClass.Length > 0 && Array.IndexOf(DecodedAnimationClasses, AnimationClass) < 0;

    public HkxSkeleton? Skeleton { get; set; }

    /// Which frame a clip driven by userControlledTimeFraction is sitting on. The fraction runs 0 to 1
    /// across the whole clip, so it maps onto the last frame's index rather than the frame count: on
    /// 41 frames, 1.0 is frame 40, and 0.5 is frame 20 rather than 20.5.
    public int FrameAt(float fraction)
    {
        if (NumFrames <= 1) return 0;
        return (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * (NumFrames - 1));
    }

    public string GetSummary()
    {
        float fps = FrameDuration > 0 ? 1.0f / FrameDuration : 0;
        return $"Duration: {Duration:F4}s, Frames: {NumFrames}, FPS: {fps:F1}, Tracks: {NumTracks}, Bones: {BoneNames.Count}, Annotations: {Annotations.Count}";
    }
}
