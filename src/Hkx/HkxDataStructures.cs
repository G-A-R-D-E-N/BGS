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

    public HkxSkeleton? Skeleton { get; set; }

    public string GetSummary()
    {
        float fps = FrameDuration > 0 ? 1.0f / FrameDuration : 0;
        return $"Duration: {Duration:F4}s, Frames: {NumFrames}, FPS: {fps:F1}, Tracks: {NumTracks}, Bones: {BoneNames.Count}, Annotations: {Annotations.Count}";
    }
}
