using System;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class PlaybackSessionTests
{
    [Fact]
    public void Tick_AdvancesAndWrapsOnlyWhilePlaying()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 3, frameDuration: 1f / 30f);

        Assert.Equal(0, session.Tick());
        Assert.True(session.Start(1f));
        Assert.Equal(1, session.Tick());
        Assert.Equal(2, session.Tick());
        Assert.Equal(0, session.Tick());

        session.Stop();
        Assert.Equal(0, session.Tick());
        Assert.False(session.IsPlaying);
    }

    [Fact]
    public void Show_ClampsToTheLoadedFrameRangeWithoutChangingPlayState()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 5, frameDuration: 0.1f);
        Assert.True(session.Start(1f));

        Assert.Equal(0, session.Show(-100));
        Assert.Equal(4, session.Show(100));
        Assert.Equal(2, session.Show(2));
        Assert.True(session.IsPlaying);
    }

    [Fact]
    public void Load_StopsAndResetsThePreviousSession()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 5, frameDuration: 0.1f);
        session.Start(2f);
        session.Show(4);

        session.Load(frameCount: 8, frameDuration: 0.25f);

        Assert.Equal(0, session.Frame);
        Assert.Equal(8, session.FrameCount);
        Assert.Equal(7, session.LastFrame);
        Assert.Equal(0.25f, session.FrameDuration);
        Assert.Equal(1f, session.Speed);
        Assert.False(session.IsPlaying);
    }

    [Fact]
    public void SingleOrEmptySessionsCannotStart()
    {
        var session = new PlaybackSession();

        session.Load(frameCount: 0, frameDuration: 0.1f);
        Assert.False(session.Start(1f));
        Assert.False(session.CanPlay);

        session.Load(frameCount: 1, frameDuration: 0.1f);
        Assert.False(session.Start(1f));
        Assert.False(session.CanPlay);
        Assert.Equal(0, session.Tick());
    }

    [Fact]
    public void Interval_UsesPlaybackSpeedAndExactSafetyClamps()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 30, frameDuration: 1f / 30f);

        session.SetSpeed(2f);
        Assert.Equal(1d / 60d, session.Interval.TotalSeconds, precision: 6);

        session.SetSpeed(float.PositiveInfinity);
        Assert.Equal(1f, session.Speed);
        Assert.Equal(1d / 30d, session.Interval.TotalSeconds, precision: 6);

        session.SetSpeed(1_000_000f);
        Assert.Equal(PlaybackSession.MinimumIntervalSeconds,
                     session.Interval.TotalSeconds, precision: 6);

        session.Load(frameCount: 30, frameDuration: 100f);
        session.SetSpeed(0.01f);
        Assert.Equal(PlaybackSession.MaximumIntervalSeconds,
                     session.Interval.TotalSeconds, precision: 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    public void InvalidSpeedsFallBackToOne(float speed)
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 3, frameDuration: 0.5f);

        session.SetSpeed(speed);

        Assert.Equal(1f, session.Speed);
        Assert.Equal(0.5, session.Interval.TotalSeconds, precision: 6);
    }

    [Fact]
    public void TimeAndFractionDescribeTheCurrentFrame()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 5, frameDuration: 0.25f);
        session.Show(2);

        Assert.Equal(0.5f, session.Time);
        Assert.Equal(0.5f, session.Fraction);

        session.Load(frameCount: 1, frameDuration: 0.25f);
        Assert.Equal(0f, session.Fraction);
    }

    [Fact]
    public void Clear_RemovesAllPlaybackState()
    {
        var session = new PlaybackSession();
        session.Load(frameCount: 4, frameDuration: 0.1f);
        session.Start(3f);
        session.Show(3);

        session.Clear();

        Assert.Equal(0, session.Frame);
        Assert.Equal(0, session.FrameCount);
        Assert.Equal(0f, session.FrameDuration);
        Assert.Equal(1f, session.Speed);
        Assert.False(session.IsPlaying);
        Assert.False(session.CanPlay);
    }
}
