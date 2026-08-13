using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

internal static class AnimationSaveTransaction
{
    internal sealed record Result(
        bool Committed,
        bool Spline,
        NativeAnimation.Result? Written,
        string Message);

    internal delegate NativeAnimation.Result Encoder(byte[] source, HkxAnimationData animation);
    internal delegate void Verifier(
        NativeAnimation.Result written,
        bool spline,
        HkxAnimationData animation,
        int editedTrack,
        int editedFrame);

    internal static Result Commit(
        string path,
        HkxAnimationData animation,
        DocumentSourceStamp? openedStamp,
        int editedTrack = -1,
        int editedFrame = -1,
        Func<Exception>? verificationFault = null,
        Action? beforeSourceRecheck = null,
        Encoder? encoder = null,
        Verifier? verifier = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        byte[] source;
        try
        {
            source = InputFilePolicy.ReadHkx(path);
        }
        catch (Exception error)
            when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Refused("Not saved: " + DocumentSourceStamp.ReadFailure(error));
        }

        if (openedStamp != null && !openedStamp.Matches(source, out string externalChange))
            return Refused("Not saved: " + externalChange);

        var transactionStamp = DocumentSourceStamp.Capture(source);
        string? blocked = HkxTextEdit.WhyNotWritable(path);
        if (blocked != null)
            return Refused("Cannot save: " + blocked);

        bool spline = animation.AnimationClass == NativeAnimation.SplineClass;
        NativeAnimation.Result written;
        try
        {
            written = encoder != null
                ? encoder(source, animation)
                : spline
                    ? NativeAnimation.Recompress(source, animation)
                    : NativeAnimation.Interleave(source, animation);
        }
        catch (Exception error)
        {
            return Refused(
                "Not saved, and the original is untouched: " + error.Message);
        }

        try
        {
            (verifier ?? Verify)(written, spline, animation, editedTrack, editedFrame);
            if (verificationFault != null) throw verificationFault();
        }
        catch (Exception error)
        {
            return Refused(
                "The rebuilt animation failed verification, so nothing was written: " +
                error.Message);
        }

        beforeSourceRecheck?.Invoke();
        if (!transactionStamp.Matches(path, out externalChange))
            return Refused("Not saved: " + externalChange);

        try
        {
            FileSafety.Backup(path);
            FileSafety.Replace(path, written.Bytes);
        }
        catch (Exception error)
        {
            return Refused("Not saved: the file could not be written: " + error.Message);
        }

        string size = written.Grew >= 0
            ? $"{written.Grew} bytes larger"
            : $"{-written.Grew} bytes smaller";
        string message =
            $"Saved {written.Frames} frame(s) of {written.Tracks} track(s) " +
            (spline ? "spline compressed" : "uncompressed") +
            $", {size}. The original is kept as {Path.GetFileName(path + ".bak")}.";

        return new Result(true, spline, written, message);
    }

    internal static void Verify(
        NativeAnimation.Result written,
        bool spline,
        HkxAnimationData animation,
        int editedTrack,
        int editedFrame)
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(animation);

        var rebuilt = new HkxBinaryReader().ParseHkx(written.Bytes);
        if (rebuilt.HasUnsupportedAnimation)
            throw new InvalidDataException(
                $"the rebuilt file decodes as {rebuilt.AnimationClass}, which is not supported");

        var objects = new PackfileObjects(PackfileImage.Read(written.Bytes));
        var mismatched = HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames());
        if (mismatched.Count > 0)
            throw new InvalidDataException(
                "rebuilt class signatures do not match: " + mismatched[0]);

        string expectedClass = spline
            ? NativeAnimation.SplineClass
            : NativeAnimation.InterleavedClass;
        if (rebuilt.AnimationClass != expectedClass)
            throw new InvalidDataException(
                $"rebuilt decodes as {rebuilt.AnimationClass}, expected {expectedClass}");
        if (rebuilt.NumTracks != written.Tracks)
            throw new InvalidDataException(
                $"rebuilt decodes to {rebuilt.NumTracks} track(s), expected {written.Tracks}");
        if (rebuilt.NumFrames != written.Frames)
            throw new InvalidDataException(
                $"rebuilt decodes to {rebuilt.NumFrames} frame(s), expected {written.Frames}");
        if (Math.Abs(rebuilt.Duration - animation.Duration) > 1e-3f)
            throw new InvalidDataException(
                $"rebuilt duration {rebuilt.Duration} differs from the edited {animation.Duration}");

        if (editedTrack < 0 || editedFrame < 0 ||
            editedTrack >= rebuilt.Tracks.Count ||
            editedTrack >= animation.Tracks.Count ||
            editedFrame >= rebuilt.Tracks[editedTrack].Translations.Count ||
            editedFrame >= animation.Tracks[editedTrack].Translations.Count)
            return;

        var wanted = animation.Tracks[editedTrack].Translations[editedFrame];
        var landed = rebuilt.Tracks[editedTrack].Translations[editedFrame];
        float drift = (landed - wanted).Length();
        float limit = spline ? 0.05f : 0.001f;
        if (drift > limit)
            throw new InvalidDataException(
                $"the edited frame did not survive re-encoding (drift {drift})");
    }

    private static Result Refused(string message) =>
        new(false, false, null, message);
}
