using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;
using OpenCommonwealth.Services;

namespace BehaviourStudio.App;

public partial class MainWindow : Window
{
    private Control BuildAnimationTab()
    {
        var earlier = Ux.Secondary("Earlier frames");
        earlier.Click += (_, _) => PageFrames(-FramesPerPage);
        var later = Ux.Secondary("Later frames");
        later.Click += (_, _) => PageFrames(FramesPerPage);
        var first = Ux.Secondary("First");
        first.Click += (_, _) => PageFrames(int.MinValue);
        var last = Ux.Secondary("Last");
        last.Click += (_, _) => PageFrames(int.MaxValue);

        var panel = new DockPanel();
        AddTop(panel, Ux.Pill(_animationSummary));
        AddTop(panel, Ux.Group("Frame navigation", Ux.Wrap(Ux.Pill(_framePage), first, earlier, later, last)));

        var aim = Ux.Secondary("Find frame");
        aim.Click += (_, _) => AimAtFraction();
        _fraction.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) AimAtFraction(); };
        _boneFilter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) ShowAnimationFrames();
        };

        AddTop(panel, Ux.Group("Find a frame", Ux.Wrap(Ux.Pill(_fractionAnswer), _boneFilter, _fraction, aim)));

        var apply = Ux.Secondary("Set frame");
        apply.Click += (_, _) => SetFrame();
        var write = Ux.Primary("Save changes");
        write.Click += (_, _) => SaveAnimation();

        foreach (var box in new[] { _framePosition, _frameRotation, _frameScale })
            box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) SetFrame(); };

        _animation.SelectionChanged += ShowSelectedFrame;

        AddTop(panel, Ux.Group("Edit selected frame",
            Ux.Wrap(_framePosition, _frameRotation, _frameScale, apply, write, Ux.Pill(_frameEditAnswer))));

        panel.Children.Add(_animation);
        return panel;
    }

    private void ShowSelectedFrame()
    {
        var anim = _animationData;
        _editTrack = _editFrame = -1;

        if (anim == null || _animation.SelectedTag is not string tag) { Clear(); return; }

        var parts = tag.Split(':');
        if (parts.Length != 3 || parts[0] != "f" ||
            !int.TryParse(parts[1], out int track) || !int.TryParse(parts[2], out int frame))
        {
            Clear();
            return;
        }

        if (track >= anim.Tracks.Count || frame >= anim.Tracks[track].Translations.Count) { Clear(); return; }

        _editTrack = track;
        _editFrame = frame;

        var data = anim.Tracks[track];
        _framePosition.Text = Triple(data.Translations[frame]);
        _frameRotation.Text = frame < data.Rotations.Count
            ? $"{F(data.Rotations[frame].X)}, {F(data.Rotations[frame].Y)}, " +
              $"{F(data.Rotations[frame].Z)}, {F(data.Rotations[frame].W)}"
            : "";
        _frameScale.Text = frame < data.Scales.Count ? Triple(data.Scales[frame]) : "";

        _frameEditAnswer.Text = $"{TrackName(anim, _animationSkeleton, track)}, frame {frame}";
        _frameEditAnswer.Foreground = Ux.MetaBrush;

        void Clear()
        {
            _framePosition.Text = _frameRotation.Text = _frameScale.Text = "";
            _frameEditAnswer.Text = "Pick a frame row to change it.";
            _frameEditAnswer.Foreground = Ux.MutedBrush;
        }
    }

    internal static string TempDirKey(string path)
    {
        string full = Path.GetFullPath(path);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(full));
        return Path.GetFileNameWithoutExtension(full) + "-" + Convert.ToHexString(hash)[..12];
    }

    private static bool ContainsNonFinite(string? text)
    {
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
            if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float f) &&
                (float.IsNaN(f) || float.IsInfinity(f)))
                return true;
        return false;
    }

    private static string Triple(System.Numerics.Vector3 v) => $"{F(v.X)}, {F(v.Y)}, {F(v.Z)}";

    private static string F(float value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private void SetFrame()
    {
        var anim = _animationData;
        if (anim == null || _editTrack < 0)
        {
            _frameEditAnswer.Text = "Pick a frame row first.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        var track = anim.Tracks[_editTrack];

        if (ContainsNonFinite(_framePosition.Text) || ContainsNonFinite(_frameRotation.Text) ||
            ContainsNonFinite(_frameScale.Text))
        {
            _frameEditAnswer.Text = "NaN and Infinity are not allowed here — every number must be finite.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        if (!Numbers(_framePosition.Text, 3, out float[] position) ||
            !Numbers(_frameRotation.Text, 4, out float[] rotation) ||
            (_frameScale.Text?.Trim().Length > 0 && !Numbers(_frameScale.Text, 3, out _)))
        {
            _frameEditAnswer.Text = "Position takes three numbers and rotation four, separated by commas.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return;
        }

        track.Translations[_editFrame] = new System.Numerics.Vector3(position[0], position[1], position[2]);

        if (_editFrame < track.Rotations.Count)
            track.Rotations[_editFrame] =
                new System.Numerics.Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);

        if (Numbers(_frameScale.Text, 3, out float[] scale) && _editFrame < track.Scales.Count)
            track.Scales[_editFrame] = new System.Numerics.Vector3(scale[0], scale[1], scale[2]);

        _animationEdited = true;
        _frameEditAnswer.Text = $"{TrackName(anim, _animationSkeleton, _editTrack)}, frame {_editFrame} " +
                                "changed   (unsaved)";
        _frameEditAnswer.Foreground = Ux.CodeBrush;

        int track_ = _editTrack, frame_ = _editFrame;
        ShowAnimationFrames();
        _animation.SelectByTag($"f:{track_}:{frame_}");
    }

    private static bool Numbers(string? text, int wanted, out float[] values)
    {
        values = new float[wanted];
        var parts = (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != wanted) return false;

        for (int i = 0; i < wanted; i++)
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out values[i]) ||
                float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                return false;

        return true;
    }

    private void AimAtFraction()
    {
        var anim = _animationData;
        if (anim == null || anim.NumFrames <= 0)
        {
            _fractionAnswer.Text = "Open an animation first.";
            _fractionAnswer.Foreground = Ux.MutedBrush;
            return;
        }

        string typed = (_fraction.Text ?? "").Trim();
        if (!float.TryParse(typed, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float fraction) ||
            float.IsNaN(fraction) || float.IsInfinity(fraction))
        {
            _fractionAnswer.Text = $"\"{typed}\" is not a number between 0 and 1.";
            _fractionAnswer.Foreground = Ux.BadBrush;
            _aimedFrame = -1;
            ShowAnimationFrames();
            return;
        }

        _aimedFrame = anim.FrameAt(fraction);
        string clamped = fraction is < 0f or > 1f ? $", clamped from {fraction:0.###}" : "";
        _fractionAnswer.Text =
            $"userControlledTimeFraction {Math.Clamp(fraction, 0f, 1f):0.###} is frame {_aimedFrame} " +
            $"of {Math.Max(anim.NumFrames - 1, 0)}, at {_aimedFrame * anim.FrameDuration:F3}s{clamped}";
        _fractionAnswer.Foreground = Ux.MetaBrush;

        _frameStart = _aimedFrame / FramesPerPage * FramesPerPage;
        ShowAnimationFrames();
    }

    private void PageFrames(int by)
    {
        if (_animationData == null) return;
        int frames = _animationData.NumFrames;
        int lastStart = Math.Max(0, ((frames - 1) / FramesPerPage) * FramesPerPage);

        _frameStart = by switch
        {
            int.MinValue => 0,
            int.MaxValue => lastStart,
            _ => Math.Clamp(_frameStart + by, 0, lastStart),
        };
        ShowAnimationFrames();
    }

    private bool BuildAnimation(string path)
    {
        _animation.Clear();
        _animationData = null;
        _animationSkeleton = null;
        _frameStart = 0;

        _aimedFrame = -1;
        _editTrack = _editFrame = -1;
        _animationEdited = false;
        _fractionAnswer.Text = "";
        _framePosition.Text = _frameRotation.Text = _frameScale.Text = "";
        _frameEditAnswer.Text = "";

        HkxAnimationData anim;
        try
        {
            if (!new HkxBinaryReader().TryReadAnimation(path, out anim))
            {
                _animationSummary.Text =
                    $"Unsupported: {anim.AnimationClass} (decode not implemented yet). " +
                    $"Only {HkxAnimationData.SupportedAnimationClasses} are read, so there is no frame data to show.";
                _animationSummary.Foreground = Ux.BadBrush;
                _animation.Add(null, anim.AnimationClass, "", "", "no frame data was read from this file", "")
                          .Colour(0, Ux.BadBrush).Colour(3, Ux.MutedBrush);
                return true;
            }
        }
        catch (Exception ex)
        {
            _animationSummary.Text = "Could not read this file as an animation: " + ex.Message.Split('\n')[0];
            _animationSummary.Foreground = Ux.BadBrush;
            return false;
        }

        bool anyFrames = anim.Tracks.Any(t => t.Rotations.Count > 0 || t.Translations.Count > 0);
        if (!anyFrames || anim.NumFrames <= 0)
        {
            _animationSummary.Text = anim.AnimationClass.Length == 0
                ? "This file holds no animation."
                : $"{anim.AnimationClass} is present but decoded to no frames, so the file is an empty container.";
            _animationSummary.Foreground = Ux.MutedBrush;
            return anim.AnimationClass.Length > 0;
        }

        _animationData = anim;
        _animationSkeleton = SiblingSkeleton(path);
        _frameStart = 0;
        ShowAnimationFrames();
        return true;
    }

    private void ShowAnimationFrames()
    {
        _animation.Clear();
        var anim = _animationData;
        if (anim == null) { _framePage.Text = ""; return; }

        var skeleton = _animationSkeleton;
        _animationSummary.Text =
            $"{anim.AnimationClass}   {anim.GetSummary()}" +
            (skeleton != null ? $"   bones named from a sibling skeleton of {skeleton.BoneNames.Count}" : "   no sibling skeleton, tracks are numbered");
        _animationSummary.Foreground = Ux.MetaBrush;

        int last = Math.Min(_frameStart + FramesPerPage, anim.NumFrames);
        _framePage.Text = anim.NumFrames <= FramesPerPage
            ? $"all {anim.NumFrames} frames"
            : $"frames {_frameStart} to {last - 1} of {anim.NumFrames}";

        foreach (var note in anim.Annotations)
            _animation.Add(null, "annotation", "", $"{note.Time:F3}s", note.Text, "", "").Colour(0, Ux.MutedBrush);

        string needle = (_boneFilter.Text ?? "").Trim();
        int shown = 0;

        for (int t = 0; t < anim.Tracks.Count; t++)
        {
            string name = TrackName(anim, skeleton, t);
            if (needle.Length > 0 && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown++;

            var track = anim.Tracks[t];
            int frames = Math.Max(Math.Max(track.Translations.Count, track.Rotations.Count), track.Scales.Count);
            bool scaled = HkxTrackData.IsScaled(track);

            var head = _animation.Add(null, name, frames.ToString(), "", "", "", scaled ? "scaled" : "")
                                 .Colour(0, Ux.TitleBrush).Colour(1, Ux.DisabledBrush).Colour(5, Ux.BadBrush);
            if (needle.Length == 0) head.Collapse();

            for (int f = _frameStart; f < Math.Min(last, frames); f++)
            {
                string pos = f < track.Translations.Count
                    ? $"{track.Translations[f].X:F3}, {track.Translations[f].Y:F3}, {track.Translations[f].Z:F3}" : "";
                string rot = f < track.Rotations.Count
                    ? $"{track.Rotations[f].X:F4}, {track.Rotations[f].Y:F4}, {track.Rotations[f].Z:F4}, {track.Rotations[f].W:F4}" : "";

                string scl = scaled && f < track.Scales.Count
                    ? $"{track.Scales[f].X:F4}, {track.Scales[f].Y:F4}, {track.Scales[f].Z:F4}" : "";
                bool aimed = f == _aimedFrame;

                _animation.Add(head, aimed ? "->" : "", f.ToString(), $"{f * anim.FrameDuration:F3}s", pos, rot, scl)
                          .Tag($"f:{t}:{f}")
                          .Colour(0, Ux.AccentBrush)
                          .Colour(1, aimed ? Ux.AccentBrush : Ux.DisabledBrush).Colour(2, Ux.MutedBrush)
                          .Colour(3, Ux.CodeBrush).Colour(4, Ux.MetaBrush).Colour(5, Ux.BadBrush);
            }
        }

        if (shown == 0 && anim.Tracks.Count > 0)
            _animation.Add(null, $"no track matches \"{needle}\"", anim.Tracks.Count + " in the file")
                      .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush);
    }

    internal static string? SiblingSkeletonPath(string primaryPath, string? fallbackPath = null)
    {
        string? assets = FindPoseSkeletonFolder(primaryPath, fallbackPath);
        if (assets != null)
        {
            foreach (string file in Directory.EnumerateFiles(assets, "*.hkx").OrderBy(f => f))
            {
                try { new HkxBinaryReader().ReadSkeleton(file); return file; }
                catch { }
            }
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(primaryPath)) ?? "";
        foreach (string file in Directory.EnumerateFiles(folder, "*.hkx").OrderBy(f => f))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(primaryPath),
                              StringComparison.OrdinalIgnoreCase)) continue;
            try { new HkxBinaryReader().ReadSkeleton(file); return file; }
            catch { }
        }
        return null;
    }

    private static HkxSkeleton? SiblingSkeleton(string primaryPath, string? fallbackPath = null)
    {
        string? file = SiblingSkeletonPath(primaryPath, fallbackPath);
        if (file == null) return null;
        try { return new HkxBinaryReader().ReadSkeleton(file); }
        catch { return null; }
    }

    internal static string? FindSiblingSkeletonFolder(string animationPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(animationPath)) ?? "");
        while (dir != null)
        {
            if (dir.Name.Equals("Animations", StringComparison.OrdinalIgnoreCase))
            {
                var characterRoot = dir.Parent;
                if (characterRoot == null) return null;

                string assets = Path.Combine(characterRoot.FullName, "CharacterAssets");
                return Directory.Exists(assets) ? assets : null;
            }
            dir = dir.Parent;
        }
        return null;
    }

    internal static string? FindPoseSkeletonFolder(string primaryPath, string? fallbackPath = null)
    {
        string? primary = FindSiblingSkeletonFolder(primaryPath);
        return primary ?? (string.IsNullOrWhiteSpace(fallbackPath)
            ? null
            : FindSiblingSkeletonFolder(fallbackPath));
    }

    private static string TrackName(HkxAnimationData anim, HkxSkeleton? skeleton, int track)
    {
        if (skeleton != null && track < anim.TrackToBoneIndices.Count)
        {
            int bone = anim.TrackToBoneIndices[track];
            if (bone >= 0 && bone < skeleton.BoneNames.Count) return skeleton.BoneNames[bone];
        }

        string annotation = track < anim.BoneNames.Count ? anim.BoneNames[track] : "";
        return annotation.Length > 0 ? annotation : $"track {track}";
    }

}
