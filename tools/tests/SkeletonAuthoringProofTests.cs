using System;
using System.Linq;
using System.Numerics;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class SkeletonAuthoringProofTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingParentCyclesAreRejectedBeforeMutation(bool reparent)
    {
        var rig = Rig();
        rig.Skeleton.ParentIndices[0] = 2;
        var names = rig.Skeleton.BoneNames.ToArray();
        var parents = rig.Skeleton.ParentIndices.ToArray();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (reparent) rig.Reparent(0, -1);
            else rig.AddBone("Arm", 0);
        });
        Assert.Equal(names, rig.Skeleton.BoneNames);
        Assert.Equal(parents, rig.Skeleton.ParentIndices);
    }

    [Fact]
    public void SkeletonAuthoringBuildsAnOrderedValidRig()
    {
        var rig = Rig();

        Assert.Equal(new[] { "Root", "Spine", "Head" }, rig.Skeleton.BoneNames);
        Assert.Equal(new[] { -1, 0, 1 }, rig.Skeleton.ParentIndices);
        Assert.True(SkeletonValidator.Valid(rig.Skeleton));
    }

    [Fact]
    public void AddingUnderAnEarlierParentKeepsParentsBeforeChildren()
    {
        var rig = new SkeletonEdit();
        int root = rig.AddBone("Root");
        int left = rig.AddBone("LeftArm", root);
        rig.AddBone("LeftHand", left);
        rig.AddBone("RightArm", root);
        rig.AddBone("LeftFinger", rig.IndexOf("LeftHand"));

        Assert.True(ParentsComeFirst(rig.Skeleton));
        Assert.Equal(rig.IndexOf("LeftHand"), rig.ParentOf(rig.IndexOf("LeftFinger")));
        Assert.Equal(rig.IndexOf("Root"), rig.ParentOf(rig.IndexOf("RightArm")));
        Assert.Empty(Errors(rig.Skeleton));
    }

    [Fact]
    public void RenameRefusesDuplicateAndEmptyNames()
    {
        var rig = Rig();

        Assert.Throws<ArgumentException>(() => rig.Rename(rig.IndexOf("Head"), "Spine"));
        Assert.Throws<ArgumentException>(() => rig.Rename(rig.IndexOf("Head"), "  "));

        rig.Rename(rig.IndexOf("Head"), "Neck");
        Assert.Equal("Neck", rig.NameOf(rig.IndexOf("Neck")));
        Assert.Equal(-1, rig.IndexOf("Head"));
    }

    [Fact]
    public void ReparentMovesTheSubtreeAndRefusesCycles()
    {
        var rig = Rig();
        int arm = rig.AddBone("Arm", rig.IndexOf("Spine"));
        rig.AddBone("Hand", arm);
        rig.AddBone("Finger", rig.IndexOf("Hand"));

        int moved = rig.Reparent(rig.IndexOf("Arm"), rig.IndexOf("Root"));

        Assert.Equal(rig.IndexOf("Root"), rig.ParentOf(moved));
        Assert.Equal(rig.IndexOf("Arm"), rig.ParentOf(rig.IndexOf("Hand")));
        Assert.Equal(rig.IndexOf("Hand"), rig.ParentOf(rig.IndexOf("Finger")));
        Assert.True(ParentsComeFirst(rig.Skeleton));
        Assert.Empty(Errors(rig.Skeleton));
        Assert.Throws<ArgumentException>(() =>
            rig.Reparent(rig.IndexOf("Arm"), rig.IndexOf("Finger")));
    }

    [Fact]
    public void RemoveHandsChildrenToTheRemovedBonesParent()
    {
        var rig = Rig();
        rig.AddBone("Hair", rig.IndexOf("Head"));

        var removal = rig.RemoveBone(rig.IndexOf("Head"));

        Assert.Equal("Head", removal.Name);
        Assert.Equal(new[] { "Hair" }, removal.Reparented);
        Assert.Equal(-1, rig.IndexOf("Head"));
        Assert.Equal(rig.IndexOf("Spine"), rig.ParentOf(rig.IndexOf("Hair")));
        Assert.True(ParentsComeFirst(rig.Skeleton));
        Assert.Empty(Errors(rig.Skeleton));
    }

    [Fact]
    public void EditsKeepParallelBoneArraysAligned()
    {
        var rig = Rig();
        rig.AddBone("Arm", rig.IndexOf("Spine"));
        rig.Reparent(rig.IndexOf("Arm"), rig.IndexOf("Root"));
        rig.RemoveBone(rig.IndexOf("Spine"));
        rig.SetPose(rig.IndexOf("Root"),
            new HkxBonePose(new Vector3(0, 1, 0), Quaternion.Identity, Vector3.One));

        int bones = rig.Skeleton.BoneNames.Count;
        Assert.Equal(bones, rig.Skeleton.ParentIndices.Count);
        Assert.Equal(bones, rig.Skeleton.ReferencePose.Count);
        Assert.Equal(bones, rig.Skeleton.LockTranslation.Count);
        Assert.Empty(Errors(rig.Skeleton));
    }

    [Fact]
    public void ValidatorFindsParentOrderAndRangeErrors()
    {
        var skeleton = Rig().Skeleton;
        skeleton.ParentIndices[0] = 1;

        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("stored after it", StringComparison.Ordinal));

        skeleton = Rig().Skeleton;
        skeleton.ParentIndices[2] = 7;
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("past the last bone", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorFindsCyclesAndMissingRoots()
    {
        var skeleton = Rig().Skeleton;
        skeleton.ParentIndices[0] = 2;
        skeleton.ParentIndices[1] = 0;
        skeleton.ParentIndices[2] = 1;

        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("comes back to it", StringComparison.Ordinal));
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("no bone is a root", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorFindsParallelArrayAndNameErrors()
    {
        var skeleton = Rig().Skeleton;
        skeleton.ReferencePose.RemoveAt(2);
        Assert.Contains(Errors(skeleton), finding => finding.Where == "referencePose");

        skeleton = Rig().Skeleton;
        skeleton.BoneNames[2] = "Spine";
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("already called", StringComparison.Ordinal));

        skeleton.BoneNames[2] = "";
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("has no name", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsUnusableReferencePoseValues()
    {
        var skeleton = Rig().Skeleton;
        skeleton.ReferencePose[1] = new HkxBonePose(
            new Vector3(float.NaN, 0, 0), Quaternion.Identity, Vector3.One);
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("translation is not a finite", StringComparison.Ordinal));

        skeleton.ReferencePose[1] = new HkxBonePose(
            Vector3.Zero, Quaternion.Identity, new Vector3(1, 0, 1));
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("collapses an axis", StringComparison.Ordinal));

        skeleton.ReferencePose[1] = new HkxBonePose(
            Vector3.Zero, new Quaternion(0, 0, 0, 0), Vector3.One);
        Assert.Contains(Errors(skeleton), finding =>
            finding.What.Contains("no length", StringComparison.Ordinal));
    }

    [Fact]
    public void SlightlyNonUnitRotationIsWarningOnly()
    {
        var skeleton = Rig().Skeleton;
        skeleton.ReferencePose[1] = new HkxBonePose(
            Vector3.Zero, new Quaternion(0, 0, 0, 1.05f), Vector3.One);

        Assert.Empty(Errors(skeleton));
        Assert.Contains(SkeletonValidator.Check(skeleton), finding =>
            finding.Level == SkeletonValidator.Level.Warning &&
            finding.What.Contains("long rather than 1", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptySkeletonIsInvalid()
    {
        Assert.False(SkeletonValidator.Valid(new HkxSkeleton()));
    }

    private static SkeletonEdit Rig()
    {
        var rig = new SkeletonEdit();
        int root = rig.AddBone("Root");
        int spine = rig.AddBone("Spine", root);
        rig.AddBone("Head", spine);
        return rig;
    }

    private static SkeletonValidator.Finding[] Errors(HkxSkeleton skeleton) =>
        SkeletonValidator.Check(skeleton)
            .Where(finding => finding.Level == SkeletonValidator.Level.Error)
            .ToArray();

    private static bool ParentsComeFirst(HkxSkeleton skeleton)
    {
        for (int i = 0; i < skeleton.ParentIndices.Count; i++)
            if (skeleton.ParentIndices[i] >= i)
                return false;
        return true;
    }
}
