using System;
using System.IO;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AnimationSaveTransactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Commit_PublishesVerifiedBytesAndKeepsAnExactBackup(bool spline)
    {
        using var scope = new AnimationScope(spline);
        byte[] source = File.ReadAllBytes(scope.Path);
        int frame = 2;
        var edited = new Vector3(11.5f, -22.25f, 33.75f);
        scope.Animation.Tracks[0].Translations[frame] = edited;

        var result = AnimationSaveTransaction.Commit(
            scope.Path,
            scope.Animation,
            DocumentSourceStamp.Capture(scope.Path),
            editedTrack: 0,
            editedFrame: frame);

        Assert.True(result.Committed);
        Assert.Equal(spline, result.Spline);
        Assert.NotNull(result.Written);
        Assert.Equal(source, File.ReadAllBytes(scope.Path + ".bak"));
        Assert.False(source.SequenceEqual(File.ReadAllBytes(scope.Path)));

        var reopened = new HkxBinaryReader().ParseHkx(File.ReadAllBytes(scope.Path));
        Assert.Equal(
            spline ? NativeAnimation.SplineClass : NativeAnimation.InterleavedClass,
            reopened.AnimationClass);
        float drift = (reopened.Tracks[0].Translations[frame] - edited).Length();
        Assert.True(drift < (spline ? 0.05f : 0.001f), $"edited frame drifted by {drift}");
        Assert.StartsWith("Saved ", result.Message, StringComparison.Ordinal);
        Assert.Contains(spline ? "spline compressed" : "uncompressed", result.Message,
                        StringComparison.Ordinal);
    }

    [Fact]
    public void Commit_RejectsASourceChangeInjectedBeforePublication()
    {
        using var scope = new AnimationScope(spline: false);
        byte[] source = File.ReadAllBytes(scope.Path);
        byte[] external = source.ToArray();
        external[^1] ^= 0x01;
        scope.Animation.Tracks[0].Translations[1] = new Vector3(9, 8, 7);

        var result = AnimationSaveTransaction.Commit(
            scope.Path,
            scope.Animation,
            DocumentSourceStamp.Capture(scope.Path),
            editedTrack: 0,
            editedFrame: 1,
            beforeSourceRecheck: () => File.WriteAllBytes(scope.Path, external));

        Assert.False(result.Committed);
        Assert.Equal(external, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("changed on disk", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Commit_RejectsAFileThatWasAlreadyChangedAfterOpen()
    {
        using var scope = new AnimationScope(spline: false);
        var opened = DocumentSourceStamp.Capture(scope.Path);
        byte[] external = File.ReadAllBytes(scope.Path);
        external[^1] ^= 0x01;
        File.WriteAllBytes(scope.Path, external);

        var result = AnimationSaveTransaction.Commit(
            scope.Path, scope.Animation, opened, editedTrack: 0, editedFrame: 1);

        Assert.False(result.Committed);
        Assert.Equal(external, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("changed on disk", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Commit_NeverPublishesBytesThatFailVerification()
    {
        using var scope = new AnimationScope(spline: false);
        byte[] source = File.ReadAllBytes(scope.Path);
        scope.Animation.Tracks[0].Translations[1] = new Vector3(4, 5, 6);

        var result = AnimationSaveTransaction.Commit(
            scope.Path,
            scope.Animation,
            DocumentSourceStamp.Capture(scope.Path),
            editedTrack: 0,
            editedFrame: 1,
            verificationFault: () => new InvalidDataException("injected verification fault"));

        Assert.False(result.Committed);
        Assert.Equal(source, File.ReadAllBytes(scope.Path));
        Assert.False(File.Exists(scope.Path + ".bak"));
        Assert.Contains("failed verification", result.Message, StringComparison.Ordinal);
        Assert.Contains("injected verification fault", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PathAndByteArrayEncodersProduceTheSameNativeResult(bool spline)
    {
        using var scope = new AnimationScope(spline);
        byte[] source = File.ReadAllBytes(scope.Path);

        NativeAnimation.Result fromPath = spline
            ? NativeAnimation.Recompress(scope.Path, scope.Animation)
            : NativeAnimation.Interleave(scope.Path, scope.Animation);
        NativeAnimation.Result fromBytes = spline
            ? NativeAnimation.Recompress(source, scope.Animation)
            : NativeAnimation.Interleave(source, scope.Animation);

        Assert.Equal(fromPath.Frames, fromBytes.Frames);
        Assert.Equal(fromPath.Tracks, fromBytes.Tracks);
        Assert.Equal(fromPath.From, fromBytes.From);
        Assert.Equal(fromPath.Grew, fromBytes.Grew);
        Assert.Equal(fromPath.Bytes, fromBytes.Bytes);
    }

    private sealed class AnimationScope : IDisposable
    {
        private readonly string _root =
            Directory.CreateTempSubdirectory("bgs-animation-save").FullName;

        public AnimationScope(bool spline)
        {
            Path = System.IO.Path.Combine(_root, "animation.hkx");
            (byte[] source, HkxAnimationData animation) = CreateSource(spline);
            Animation = animation;
            File.WriteAllBytes(Path, source);
        }

        public string Path { get; }
        public HkxAnimationData Animation { get; }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        private static (byte[] Source, HkxAnimationData Animation) CreateSource(bool spline)
        {
            string sourceClass = spline
                ? NativeAnimation.SplineClass
                : NativeAnimation.Compressed.Single(name => name != NativeAnimation.SplineClass);
            HkxAnimationData animation = MadeUpAnimation(sourceClass);

            var image = new PackfileImage { Predicates = new byte[16] };
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__types__") });
            image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });

            var source = NativeAppend.Object(image, sourceClass);
            var binding = NativeAppend.Object(image, "hkaAnimationBinding");
            NativeAppend.Attach(image, binding.Id, "animation", source.Id);

            var duration = HavokClassTypes.Shipped.Members(sourceClass)
                .Single(member => member.Name == "duration");
            var data = image.Section("__data__")!;
            BitConverter.GetBytes(animation.Duration)
                .CopyTo(data.Data, source.Offset + duration.Offset);
            FixupOrder.Reorder(image);

            return (image.Rebuild(), animation);
        }

        private static HkxAnimationData MadeUpAnimation(string sourceClass)
        {
            const int frames = 5;
            var animation = new HkxAnimationData
            {
                AnimationClass = sourceClass,
                NumFrames = frames,
                NumTracks = 1,
                FrameDuration = 1f / 30f,
                Duration = (frames - 1) / 30f,
            };
            var track = new HkxTrackData { RotationAnimated = true };
            track.TranslationAnimated[0] = true;
            track.TranslationAnimated[1] = true;
            track.TranslationAnimated[2] = true;

            for (int frame = 0; frame < frames; frame++)
            {
                track.Translations.Add(new Vector3(frame, frame * 2, -frame));
                track.Rotations.Add(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, frame * 0.1f));
                track.Scales.Add(Vector3.One);
            }

            animation.Tracks.Add(track);
            return animation;
        }

        private static byte[] Tag(string name)
        {
            var bytes = new byte[20];
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            return bytes;
        }
    }
}
