using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Hkx;

public partial class HkxBinaryReader
{
    private static readonly byte[] HkxMagic = new byte[] { 0x57, 0xE0, 0xE0, 0x57 };
    #region Public API

    public static bool IsFo4Hkx(string filepath)
    {
        if (!File.Exists(filepath)) return false;
        try
        {
            using var fs = File.OpenRead(filepath);
            if (fs.Length < 64) return false;
            byte[] hdr = new byte[4];
            fs.Read(hdr, 0, 4);
            return hdr[0] == HkxMagic[0] && hdr[1] == HkxMagic[1] &&
                   hdr[2] == HkxMagic[2] && hdr[3] == HkxMagic[3];
        }
        catch { return false; }
    }

    public HkxAnimationData ReadAnimation(string filepath)
    {
        byte[] data = InputFilePolicy.ReadHkx(filepath);
        var parsed = ParseHkx(data);

        if (parsed.HasUnsupportedAnimation)
            throw new NotSupportedException(
                $"unsupported animation class: {parsed.AnimationClass}. " +
                $"Only {HkxAnimationData.SupportedAnimationClasses} are decoded, so no frame data was read from " +
                Path.GetFileName(filepath));

        return parsed;
    }

    public HkxAnimationData ReadAnimation(byte[] data)
    {
        var parsed = ParseHkx(data);

        if (parsed.HasUnsupportedAnimation)
            throw new NotSupportedException(
                $"unsupported animation class: {parsed.AnimationClass}. " +
                $"Only {HkxAnimationData.SupportedAnimationClasses} are decoded, so no frame data was read from " +
                "this archive entry.");

        return parsed;
    }

    public bool TryReadAnimation(string filepath, out HkxAnimationData data)
    {
        data = ParseHkx(InputFilePolicy.ReadHkx(filepath));
        return !data.HasUnsupportedAnimation;
    }

    public bool TryReadAnimation(byte[] data, out HkxAnimationData animation)
    {
        animation = ParseHkx(data);
        return !animation.HasUnsupportedAnimation;
    }

    public HkxSkeleton ReadSkeleton(string filepath)
    {
        byte[] data = InputFilePolicy.ReadHkx(filepath);
        var anim = ParseHkx(data);
        if (anim.Skeleton != null) return anim.Skeleton;
        throw new InvalidDataException($"No skeleton (hkaSkeleton) found in: {filepath}");
    }

    public HkxSkeleton ReadSkeleton(byte[] data)
    {
        var anim = ParseHkx(data);
        if (anim.Skeleton != null) return anim.Skeleton;
        throw new InvalidDataException("No skeleton (hkaSkeleton) found in this archive entry");
    }

    #endregion
}
