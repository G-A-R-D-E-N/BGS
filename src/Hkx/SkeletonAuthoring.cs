using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public static class SkeletonValidator
{
    public enum Level { Error, Warning }

    public sealed record Finding(Level Level, string Where, string What)
    {
        public override string ToString() =>
            $"{(Level == Level.Error ? "error" : "warning")}  {Where}: {What}";
    }

    public const string ParentsFirst = "a bone's parent has to be stored before the bone itself";

    public static IReadOnlyList<Finding> Check(HkxSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var findings = new List<Finding>();
        int bones = skeleton.BoneNames.Count;

        if (bones == 0)
            findings.Add(new Finding(Level.Error, "skeleton", "it has no bones"));

        Parallel(findings, "parentIndices", skeleton.ParentIndices.Count, bones);
        Parallel(findings, "referencePose", skeleton.ReferencePose.Count, bones);
        if (skeleton.LockTranslation.Count != 0)
            Parallel(findings, "lockTranslations", skeleton.LockTranslation.Count, bones);
        if (skeleton.ReferenceFloats.Count != 0 || skeleton.FloatSlots.Count != 0)
            Parallel(findings, "referenceFloats", skeleton.ReferenceFloats.Count,
                     skeleton.FloatSlots.Count, "floatSlots");

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bones; i++)
        {
            string name = skeleton.BoneNames[i];
            string where = $"bone {i}" + (string.IsNullOrEmpty(name) ? "" : $" '{name}'");

            if (string.IsNullOrWhiteSpace(name))
                findings.Add(new Finding(Level.Error, where, "it has no name"));
            else if (seen.TryGetValue(name, out int first))
                findings.Add(new Finding(Level.Error, where, $"bone {first} is already called '{name}'"));
            else
                seen[name] = i;

            if (i >= skeleton.ParentIndices.Count)
                continue;

            int parent = skeleton.ParentIndices[i];
            if (parent >= bones)
                findings.Add(new Finding(Level.Error, where,
                    $"its parent is {parent}, past the last bone ({bones - 1})"));
            else if (parent == i)
                findings.Add(new Finding(Level.Error, where, "it is its own parent"));
            else if (parent >= 0 && parent > i)
                findings.Add(new Finding(Level.Error, where,
                    $"its parent is bone {parent}, stored after it; {ParentsFirst}"));

            if (i < skeleton.ReferencePose.Count)
                Pose(findings, where, skeleton.ReferencePose[i]);
        }

        Cycles(findings, skeleton);

        int roots = skeleton.ParentIndices.Count(parent => parent < 0);
        if (bones > 0 && roots == 0)
            findings.Add(new Finding(Level.Error, "skeleton", "no bone is a root, so every bone has a parent"));
        else if (roots > 1)
            findings.Add(new Finding(Level.Warning, "skeleton",
                $"{roots} bones are roots; a character rig normally has one"));

        return findings;
    }

    public static bool Valid(HkxSkeleton skeleton) =>
        Check(skeleton).All(finding => finding.Level != Level.Error);

    private static void Parallel(List<Finding> findings, string what, int count, int bones,
                                 string against = "boneNames")
    {
        if (count != bones)
            findings.Add(new Finding(Level.Error, what,
                $"it holds {count} entr{(count == 1 ? "y" : "ies")} against {bones} in {against}"));
    }

    private static void Pose(List<Finding> findings, string where, HkxBonePose pose)
    {
        if (!Finite(pose.Translation))
            findings.Add(new Finding(Level.Error, where, "its reference translation is not a finite number"));

        if (!Finite(pose.Scale))
            findings.Add(new Finding(Level.Error, where, "its reference scale is not a finite number"));
        else if (pose.Scale.X == 0 || pose.Scale.Y == 0 || pose.Scale.Z == 0)
            findings.Add(new Finding(Level.Error, where, "its reference scale collapses an axis to zero"));

        Quaternion rotation = pose.Rotation;
        if (!float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) ||
            !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W))
        {
            findings.Add(new Finding(Level.Error, where, "its reference rotation is not a finite number"));
            return;
        }

        float length = MathF.Sqrt(rotation.X * rotation.X + rotation.Y * rotation.Y +
                                  rotation.Z * rotation.Z + rotation.W * rotation.W);
        if (length < 1e-6f)
            findings.Add(new Finding(Level.Error, where, "its reference rotation has no length"));
        else if (MathF.Abs(length - 1f) > 1e-3f)
            findings.Add(new Finding(Level.Warning, where,
                $"its reference rotation is {length.ToString("0.####", CultureInfo.InvariantCulture)} long rather than 1"));
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static void Cycles(List<Finding> findings, HkxSkeleton skeleton)
    {
        int bones = skeleton.ParentIndices.Count;
        for (int i = 0; i < bones; i++)
        {
            int at = i;
            for (int step = 0; step <= bones; step++)
            {
                if (at < 0 || at >= bones)
                    break;

                at = skeleton.ParentIndices[at];
                if (at != i)
                    continue;

                string name = i < skeleton.BoneNames.Count ? skeleton.BoneNames[i] : "";
                findings.Add(new Finding(Level.Error,
                    $"bone {i}" + (name.Length > 0 ? $" '{name}'" : ""),
                    "its parent chain comes back to it"));
                break;
            }
        }
    }
}

public sealed class SkeletonEdit
{
    private readonly HkxSkeleton _skeleton;

    public SkeletonEdit(HkxSkeleton? skeleton = null)
    {
        _skeleton = skeleton ?? new HkxSkeleton();
        Normalise();
    }

    public HkxSkeleton Skeleton => _skeleton;
    public int BoneCount => _skeleton.BoneNames.Count;
    public string Name
    {
        get => _skeleton.Name;
        set => _skeleton.Name = value ?? "";
    }

    public int IndexOf(string boneName) =>
        _skeleton.BoneNames.FindIndex(name =>
            string.Equals(name, boneName, StringComparison.OrdinalIgnoreCase));

    public int ParentOf(int index) => _skeleton.ParentIndices[Require(index)];
    public HkxBonePose PoseOf(int index) => _skeleton.ReferencePose[Require(index)];
    public string NameOf(int index) => _skeleton.BoneNames[Require(index)];

    public IReadOnlyList<int> ChildrenOf(int index)
    {
        Require(index);
        var children = new List<int>();
        for (int i = 0; i < BoneCount; i++)
            if (_skeleton.ParentIndices[i] == index)
                children.Add(i);
        return children;
    }

    public int AddBone(string name, int parent = -1, HkxBonePose? pose = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("a bone needs a name", nameof(name));
        if (IndexOf(name) >= 0)
            throw new ArgumentException($"'{name}' is already a bone", nameof(name));
        if (parent >= BoneCount || parent < -1)
            throw new ArgumentOutOfRangeException(nameof(parent), $"there is no bone {parent}");

        int at = parent < 0 ? BoneCount : Last(parent) + 1;
        Insert(at, name, parent, pose ?? new HkxBonePose());
        return at;
    }

    public void Rename(int index, string name)
    {
        Require(index);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("a bone needs a name", nameof(name));

        int existing = IndexOf(name);
        if (existing >= 0 && existing != index)
            throw new ArgumentException($"bone {existing} is already called '{name}'", nameof(name));

        _skeleton.BoneNames[index] = name;
    }

    public void SetPose(int index, HkxBonePose pose)
    {
        Require(index);
        _skeleton.ReferencePose[index] = pose;
    }

    public int Reparent(int index, int parent)
    {
        Require(index);
        if (parent >= BoneCount || parent < -1)
            throw new ArgumentOutOfRangeException(nameof(parent), $"there is no bone {parent}");
        if (parent == index)
            throw new ArgumentException("a bone cannot be its own parent", nameof(parent));

        List<int> subtree = Subtree(index);
        if (parent >= 0 && subtree.Contains(parent))
            throw new ArgumentException(
                $"bone {parent} is under bone {index}, so making it the parent would close a loop",
                nameof(parent));

        var moved = subtree.Select(Snapshot).ToList();
        moved[0] = moved[0] with
        {
            Parent = parent < 0 ? null : _skeleton.BoneNames[parent]
        };

        foreach (int at in subtree.OrderByDescending(value => value))
            RemoveAt(at);

        foreach (Bone bone in moved)
        {
            int under = bone.Parent == null ? -1 : IndexOf(bone.Parent);
            int placed = under < 0 && bone.Parent != null
                ? throw new InvalidOperationException($"'{bone.Parent}' went missing while reparenting")
                : AddBone(bone.Name, under, bone.Pose);
            if (bone.Locked)
                _skeleton.LockTranslation[placed] = true;
        }

        return IndexOf(moved[0].Name);
    }

    public sealed record Removal(string Name, int Removed, IReadOnlyList<string> Reparented);

    public Removal RemoveBone(int index)
    {
        Require(index);
        string name = _skeleton.BoneNames[index];
        int parent = _skeleton.ParentIndices[index];

        var children = ChildrenOf(index).Select(i => _skeleton.BoneNames[i]).ToList();
        foreach (string child in children)
        {
            int at = IndexOf(child);
            _skeleton.ParentIndices[at] = parent;
        }

        RemoveAt(index);
        Reorder();
        return new Removal(name, index, children);
    }

    public IReadOnlyList<SkeletonValidator.Finding> Check() =>
        SkeletonValidator.Check(_skeleton);

    private sealed record Bone(string Name, string? Parent, HkxBonePose Pose, bool Locked);

    private Bone Snapshot(int index) => new(
        _skeleton.BoneNames[index],
        _skeleton.ParentIndices[index] < 0
            ? null
            : _skeleton.BoneNames[_skeleton.ParentIndices[index]],
        _skeleton.ReferencePose[index],
        index < _skeleton.LockTranslation.Count && _skeleton.LockTranslation[index]);

    private List<int> Subtree(int root)
    {
        var found = new List<int> { root };
        var seen = new HashSet<int> { root };
        for (int i = 0; i < found.Count; i++)
        {
            foreach (int child in ChildrenOf(found[i]))
            {
                if (!seen.Add(child))
                    throw new InvalidOperationException("the skeleton contains a parent cycle");
                found.Add(child);
            }
        }
        return found;
    }

    private int Last(int parent) => Subtree(parent).Max();

    private void Insert(int at, string name, int parent, HkxBonePose pose)
    {
        for (int i = 0; i < BoneCount; i++)
            if (_skeleton.ParentIndices[i] >= at)
                _skeleton.ParentIndices[i]++;

        _skeleton.BoneNames.Insert(at, name);
        _skeleton.ParentIndices.Insert(at, parent >= at ? parent + 1 : parent);
        _skeleton.ReferencePose.Insert(at, pose);
        _skeleton.LockTranslation.Insert(at, false);
    }

    private void RemoveAt(int at)
    {
        _skeleton.BoneNames.RemoveAt(at);
        _skeleton.ParentIndices.RemoveAt(at);
        _skeleton.ReferencePose.RemoveAt(at);
        if (at < _skeleton.LockTranslation.Count)
            _skeleton.LockTranslation.RemoveAt(at);

        for (int i = 0; i < BoneCount; i++)
        {
            int parent = _skeleton.ParentIndices[i];
            if (parent > at)
                _skeleton.ParentIndices[i] = parent - 1;
            else if (parent == at)
                _skeleton.ParentIndices[i] = -1;
        }
    }

    private void Reorder()
    {
        var order = new List<int>();

        void Walk(int parent)
        {
            for (int i = 0; i < BoneCount; i++)
            {
                if (_skeleton.ParentIndices[i] != parent || order.Contains(i))
                    continue;
                order.Add(i);
                Walk(i);
            }
        }

        Walk(-1);
        if (order.Count != BoneCount)
            return;

        var names = order.Select(i => _skeleton.BoneNames[i]).ToList();
        var poses = order.Select(i => _skeleton.ReferencePose[i]).ToList();
        var locked = order.Select(i =>
            i < _skeleton.LockTranslation.Count && _skeleton.LockTranslation[i]).ToList();
        var moved = new int[BoneCount];
        for (int i = 0; i < order.Count; i++)
            moved[order[i]] = i;
        var parents = order.Select(i =>
            _skeleton.ParentIndices[i] < 0 ? -1 : moved[_skeleton.ParentIndices[i]]).ToList();

        _skeleton.BoneNames = names;
        _skeleton.ParentIndices = parents;
        _skeleton.ReferencePose = poses;
        _skeleton.LockTranslation = locked;
    }

    private int Require(int index) =>
        index >= 0 && index < BoneCount
            ? index
            : throw new ArgumentOutOfRangeException(nameof(index), $"there is no bone {index}");

    private void Normalise()
    {
        int bones = _skeleton.BoneNames.Count;
        while (_skeleton.ParentIndices.Count < bones)
            _skeleton.ParentIndices.Add(-1);
        while (_skeleton.ReferencePose.Count < bones)
            _skeleton.ReferencePose.Add(new HkxBonePose());
        while (_skeleton.LockTranslation.Count < bones)
            _skeleton.LockTranslation.Add(false);
    }
}
