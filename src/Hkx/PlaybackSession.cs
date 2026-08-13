using System;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class PlaybackSession
{
    internal const double MinimumIntervalSeconds = 1d / 120d;
    internal const double MaximumIntervalSeconds = 4d;

    internal int Frame { get; private set; }
    internal int FrameCount { get; private set; }
    internal float FrameDuration { get; private set; }
    internal float Speed { get; private set; } = 1f;
    internal bool IsPlaying { get; private set; }

    internal bool CanPlay => FrameCount > 1;
    internal int LastFrame => Math.Max(FrameCount - 1, 0);
    internal float Time => Frame * FrameDuration;
    internal float Fraction => FrameCount > 1 ? (float)Frame / LastFrame : 0f;

    internal TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(
        FrameDuration / Speed,
        MinimumIntervalSeconds,
        MaximumIntervalSeconds));

    internal void Load(int frameCount, float frameDuration)
    {
        FrameCount = Math.Max(frameCount, 0);
        FrameDuration = float.IsFinite(frameDuration) && frameDuration > 0f
            ? frameDuration
            : 0f;
        Frame = 0;
        Speed = 1f;
        IsPlaying = false;
    }

    internal void Clear()
    {
        Frame = 0;
        FrameCount = 0;
        FrameDuration = 0f;
        Speed = 1f;
        IsPlaying = false;
    }

    internal bool Start(float speed)
    {
        SetSpeed(speed);
        if (!CanPlay)
        {
            IsPlaying = false;
            return false;
        }

        IsPlaying = true;
        return true;
    }

    internal void Stop() => IsPlaying = false;

    internal int Show(int frame)
    {
        Frame = Math.Clamp(frame, 0, LastFrame);
        return Frame;
    }

    internal int Tick()
    {
        if (!IsPlaying || !CanPlay) return Frame;
        Frame = Frame >= LastFrame ? 0 : Frame + 1;
        return Frame;
    }

    internal void SetSpeed(float speed) =>
        Speed = float.IsFinite(speed) && speed > 0f ? speed : 1f;
}
