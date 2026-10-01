using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public static class SkeletonMirror
{
    public sealed class Pairing
    {
        public int[] Partner = Array.Empty<int>();
        public readonly List<(int Left, int Right)> Pairs = new();
        public readonly List<string> Ambiguous = new();
        public readonly List<string> Unconfirmed = new();

        public int Count => Pairs.Count;
        public bool Centre(int bone) => PartnerOf(bone) == bone;

        public int PartnerOf(int bone) =>
            bone >= 0 && bone < Partner.Length ? Partner[bone] : -1;

        public override string ToString() =>
            Pairs.Count == 0 ? "no bone in this rig pairs with an opposite one"
            : $"{Pairs.Count} left/right pair{(Pairs.Count == 1 ? "" : "s")}" +
              (Ambiguous.Count == 0 ? "" : $", {Ambiguous.Count} name a side in more than one place") +
              (Unconfirmed.Count == 0 ? "" : $", {Unconfirmed.Count} the rig did not bear out");
    }

    public readonly record struct Axis(int Lane, float Typical, float Worst)
    {
        public string Name => Lane switch { 0 => "X", 1 => "Y", 2 => "Z", _ => "?" };

        public override string ToString() =>
            Lane < 0 ? "no pair to measure an axis from"
                     : $"mirrored across {Name}, the middling pair off by {Typical:0.###} " +
                       $"and the worst by {Worst:0.###}";
    }

    public static Pairing Of(HkxSkeleton skeleton) => Of(skeleton.BoneNames);

    public static Pairing Of(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
            byName.TryAdd(names[i] ?? "", i);

        var pairing = new Pairing { Partner = new int[names.Count] };
        for (int i = 0; i < names.Count; i++)
            pairing.Partner[i] = i;

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i] ?? "";
            var flipped = Flipped(name).ToList();
            if (flipped.Count == 0)
                continue;

            var found = flipped
                .Where(value => byName.TryGetValue(value, out int at) && at != i)
                .Select(value => byName[value])
                .Distinct()
                .ToList();

            if (found.Count == 0)
                continue;

            if (found.Count > 1)
            {
                pairing.Partner[i] = -1;
                pairing.Ambiguous.Add(name);
                continue;
            }

            pairing.Partner[i] = found[0];
        }

        for (int i = 0; i < names.Count; i++)
        {
            int other = pairing.Partner[i];
            if (other < 0 || other == i || pairing.Partner[other] == i)
                continue;

            pairing.Partner[i] = -1;
            pairing.Ambiguous.Add(names[i] ?? "");
        }

        for (int i = 0; i < names.Count; i++)
        {
            int other = pairing.Partner[i];
            if (other > i)
                pairing.Pairs.Add((i, other));
        }

        return pairing;
    }

    public static IEnumerable<string> Flipped(string name)
    {
        if (string.IsNullOrEmpty(name))
            yield break;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            char other = c switch
            {
                'L' => 'R',
                'R' => 'L',
                'l' => 'r',
                'r' => 'l',
                _ => '\0'
            };

            if (other == '\0' || !StartsWord(name, i))
                continue;

            yield return name[..i] + other + name[(i + 1)..];
        }
    }

    public static Axis MirrorAxis(HkxSkeleton skeleton, Pairing pairing)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        ArgumentNullException.ThrowIfNull(pairing);
        if (pairing.Pairs.Count == 0)
            return new Axis(-1, 0, 0);

        var pose = AnimationPose.ReferencePose(skeleton);
        var best = new Axis(-1, float.MaxValue, 0);

        for (int lane = 0; lane < 3; lane++)
        {
            var apart = Distances(
                pairing,
                lane,
                bone => bone < pose.Bones.Count ? pose.Bones[bone].Position : (Vector3?)null);
            if (apart.Count == 0)
                continue;

            var axis = new Axis(lane, apart[apart.Count / 2], apart[^1]);
            if (axis.Typical < best.Typical)
                best = axis;
        }

        return best;
    }

    public static Pairing Confirm(
        Pairing pairing,
        IReadOnlyList<string> names,
        int lane,
        float tolerance,
        Func<int, Vector3?> position)
    {
        ArgumentNullException.ThrowIfNull(pairing);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(position);

        var confirmed = new Pairing { Partner = (int[])pairing.Partner.Clone() };
        confirmed.Ambiguous.AddRange(pairing.Ambiguous);
        confirmed.Unconfirmed.AddRange(pairing.Unconfirmed);

        void Drop(int bone)
        {
            if (confirmed.Partner[bone] < 0)
                return;

            confirmed.Partner[bone] = -1;
            confirmed.Unconfirmed.Add(
                bone < names.Count ? names[bone] ?? "" : $"bone {bone}");
        }

        for (int i = 0; i < confirmed.Partner.Length; i++)
        {
            int other = confirmed.Partner[i];
            if (other < 0 || position(i) is not { } here)
                continue;

            if (other == i)
            {
                if (MathF.Abs(Lane(here, lane)) > tolerance)
                    Drop(i);
                continue;
            }

            if (position(other) is not { } there)
                continue;

            if (Vector3.Distance(here, Reflect(there, lane)) > tolerance)
            {
                Drop(i);
                Drop(other);
            }
        }

        for (int i = 0; i < confirmed.Partner.Length; i++)
        {
            int other = confirmed.Partner[i];
            if (other > i && confirmed.Partner[other] == i)
                confirmed.Pairs.Add((i, other));
            else if (other >= 0 && other != i && confirmed.Partner[other] != i)
                Drop(i);
        }

        return confirmed;
    }

    public static float Lane(Vector3 value, int lane) =>
        lane switch { 0 => value.X, 1 => value.Y, _ => value.Z };

    public static Vector3 Reflect(Vector3 value, int lane) => lane switch
    {
        0 => value with { X = -value.X },
        1 => value with { Y = -value.Y },
        2 => value with { Z = -value.Z },
        _ => value
    };

    private static bool StartsWord(string name, int at)
    {
        if (at == 0)
            return true;

        char before = name[at - 1];
        if (!char.IsLetterOrDigit(before))
            return true;

        return char.IsUpper(name[at]) && !char.IsUpper(before);
    }

    private static List<float> Distances(
        Pairing pairing,
        int lane,
        Func<int, Vector3?> position)
    {
        var apart = new List<float>();
        foreach (var (left, right) in pairing.Pairs)
        {
            if (position(left) is not { } a || position(right) is not { } b)
                continue;

            apart.Add(Vector3.Distance(a, Reflect(b, lane)));
        }

        apart.Sort();
        return apart;
    }
}
