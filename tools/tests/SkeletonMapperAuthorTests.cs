using System.Numerics;
using OpenCommonwealth.Services.Hkx;
using Xunit;
using Xunit.Abstractions;

namespace BehaviourStudio.Tests;

public class SkeletonMapperAuthorTests
{
    private readonly ITestOutputHelper _out;

    public SkeletonMapperAuthorTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void AuthoredAnimationToRagdollRowsMatchTheShippedMapper()
    {
        var (forward, _) = Vanilla();
        Assert.Equal(SkeletonMapperAuthor.RagdollMappingType, forward.MappingType);

        var pairs = forward.SimpleMappings.Select(row => (row.BoneA, row.BoneB)).ToList();
        var authored = SkeletonMapperAuthor.FromReferencePose(forward.SkeletonA, forward.SkeletonB, pairs);

        Assert.Equal(forward.SimpleMappings.Count, authored.Count);
        Assert.True(authored.Count > 0);
        Compare(forward.SimpleMappings, authored);
    }

    [Fact]
    public void AuthoredRagdollToAnimationRowsMatchTheShippedMapper()
    {
        var (_, reverse) = Vanilla();

        var pairs = reverse.SimpleMappings.Select(row => (row.BoneA, row.BoneB)).ToList();
        var authored = SkeletonMapperAuthor.FromReferencePose(reverse.SkeletonA, reverse.SkeletonB, pairs);

        Assert.Equal(reverse.SimpleMappings.Count, authored.Count);
        Compare(reverse.SimpleMappings, authored);
    }

    [Fact]
    public void ReversingTheShippedRowsReproducesTheShippedReciprocalMapper()
    {
        var (forward, reverse) = Vanilla();

        var reversed = SkeletonMapperAuthor.Reverse(forward.SimpleMappings);
        var mirrored = reversed.ToDictionary(row => (row.BoneA, row.BoneB));

        Assert.Equal(reverse.SimpleMappings.Count, reversed.Count);
        foreach (var shipped in reverse.SimpleMappings)
        {
            Assert.True(mirrored.TryGetValue((shipped.BoneA, shipped.BoneB), out var authored),
                        $"no reversed row for {shipped.BoneA} -> {shipped.BoneB}");
            AssertClose(shipped.AFromBTranslation, authored.AFromBTranslation, 5e-4f,
                        $"translation {shipped.BoneA} -> {shipped.BoneB}");
            AssertRotationClose(shipped.AFromBRotation, authored.AFromBRotation, 1e-5f,
                                $"rotation {shipped.BoneA} -> {shipped.BoneB}");
        }
    }

    [Fact]
    public void ReciprocalPairCoversBothDirectionsOfTheSameBonePairs()
    {
        var (forward, _) = Vanilla();
        var pairs = forward.SimpleMappings.Select(row => (row.BoneA, row.BoneB)).ToList();

        var pair = SkeletonMapperAuthor.Reciprocal(forward.SkeletonA, forward.SkeletonB, pairs);

        Assert.Equal(pairs.Count, pair.Forward.Count);
        Assert.Equal(pairs.Count, pair.Reverse.Count);
        for (int i = 0; i < pairs.Count; i++)
        {
            Assert.Equal(pairs[i].BoneA, pair.Reverse[i].BoneB);
            Assert.Equal(pairs[i].BoneB, pair.Reverse[i].BoneA);
        }
    }

    [Fact]
    public void MappingOneBoneTwiceIsRefused()
    {
        var (forward, _) = Vanilla();

        string firstA = forward.SkeletonA.BoneNames[0];
        var refusals = SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB,
                                                     new[] { (0, 0), (0, 1) });
        Assert.Contains(refusals, refusal => refusal.Contains("already mapped", StringComparison.Ordinal) &&
                                             refusal.Contains(firstA, StringComparison.Ordinal));

        string firstB = forward.SkeletonB.BoneNames[0];
        refusals = SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB,
                                                 new[] { (0, 0), (1, 0) });
        Assert.Contains(refusals, refusal => refusal.Contains("already mapped", StringComparison.Ordinal) &&
                                             refusal.Contains(firstB, StringComparison.Ordinal));
    }

    [Fact]
    public void SelfPairingAcrossReciprocalSkeletonsIsNotARefusal()
    {
        var (forward, _) = Vanilla();

        var refusals = SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB,
                                                     forward.SimpleMappings.Select(row => (row.BoneA, row.BoneB)).ToList());

        Assert.Empty(refusals);
    }

    [Fact]
    public void A_BoneOutsideEitherSkeletonIsRefused()
    {
        var (forward, _) = Vanilla();

        Assert.Contains(SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB, new[] { (-1, 0) }),
                        refusal => refusal.Contains("not a bone of", StringComparison.Ordinal));
        Assert.Contains(SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB,
                                                      new[] { (forward.SkeletonA.BoneNames.Count, 0) }),
                        refusal => refusal.Contains("not a bone of", StringComparison.Ordinal));
        Assert.Contains(SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB, new[] { (0, 18) }),
                        refusal => refusal.Contains("not a bone of", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyPairListIsRefused()
    {
        var (forward, _) = Vanilla();

        Assert.Contains(SkeletonMapperAuthor.Refusals(forward.SkeletonA, forward.SkeletonB, Array.Empty<(int, int)>()),
                        refusal => refusal.Contains("at least one bone pair", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() =>
            SkeletonMapperAuthor.FromReferencePose(forward.SkeletonA, forward.SkeletonB, Array.Empty<(int, int)>()));
    }

    [Fact]
    public void ASkeletonWithABoneCountMismatchIsRefused()
    {
        var parents = Rig("a", 3);
        parents.ParentIndices.RemoveAt(2);
        Assert.Contains(SkeletonMapperAuthor.Refusals(parents),
                        refusal => refusal.Contains("3 bones and 2 parent indices", StringComparison.Ordinal));

        var poses = Rig("b", 3);
        poses.ReferencePose.RemoveAt(2);
        Assert.Contains(SkeletonMapperAuthor.Refusals(poses),
                        refusal => refusal.Contains("3 bones and 2 reference-pose transforms", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInvalidParentIndexIsRefused()
    {
        var a = Rig("a", 3);
        a.ParentIndices[1] = 3;
        Assert.Contains(SkeletonMapperAuthor.Refusals(a),
                        refusal => refusal.Contains("parent 3", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOutOfOrderParentIsRefusedRatherThanTreatedAsARoot()
    {
        var a = Rig("a", 3);
        a.ParentIndices[1] = 2;
        Assert.Contains(SkeletonMapperAuthor.Refusals(a),
                        refusal => refusal.Contains("stored at or after it", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonFiniteReferenceTransformIsRefused()
    {
        var translation = Rig("a", 2);
        translation.ReferencePose[1] = new HkxBonePose(new Vector3(float.NaN, 0f, 0f), Quaternion.Identity, Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(translation),
                        refusal => refusal.Contains("non-finite reference transform", StringComparison.Ordinal));

        var infinite = Rig("a", 2);
        infinite.ReferencePose[1] = new HkxBonePose(new Vector3(0f, float.PositiveInfinity, 0f),
                                                    Quaternion.Identity, Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(infinite),
                        refusal => refusal.Contains("non-finite reference transform", StringComparison.Ordinal));

        var rotation = Rig("a", 2);
        rotation.ReferencePose[1] = new HkxBonePose(Vector3.Zero,
                                                    new Quaternion(float.PositiveInfinity, 0f, 0f, 1f), Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(rotation),
                        refusal => refusal.Contains("non-finite reference transform", StringComparison.Ordinal));

        var nanRotation = Rig("a", 2);
        nanRotation.ReferencePose[1] = new HkxBonePose(Vector3.Zero,
                                                       new Quaternion(0f, 0f, float.NaN, 1f), Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(nanRotation),
                        refusal => refusal.Contains("non-finite reference transform", StringComparison.Ordinal));
    }

    [Fact]
    public void AReferenceRotationThatIsNotUnitLengthIsRefused()
    {
        var zero = Rig("a", 2);
        zero.ReferencePose[1] = new HkxBonePose(Vector3.Zero, new Quaternion(0f, 0f, 0f, 0f), Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(zero),
                        refusal => refusal.Contains("not a unit quaternion", StringComparison.Ordinal));

        var scaled = Rig("a", 2);
        scaled.ReferencePose[1] = new HkxBonePose(Vector3.Zero, new Quaternion(2f, 1f, 0f, 0f), Vector3.One);
        Assert.Contains(SkeletonMapperAuthor.Refusals(scaled),
                        refusal => refusal.Contains("not a unit quaternion", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonIdentityReferenceScaleIsRefused()
    {
        var a = Rig("a", 2);
        a.ReferencePose[1] = new HkxBonePose(Vector3.Zero, Quaternion.Identity, new Vector3(1f, 2f, 1f));
        Assert.Contains(SkeletonMapperAuthor.Refusals(a),
                        refusal => refusal.Contains("non-identity reference scale", StringComparison.Ordinal));

        var onePercent = Rig("b", 2);
        onePercent.ReferencePose[1] = new HkxBonePose(Vector3.Zero, Quaternion.Identity, new Vector3(1.01f, 1f, 1f));
        Assert.Contains(SkeletonMapperAuthor.Refusals(onePercent),
                        refusal => refusal.Contains("non-identity reference scale", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonFiniteReferenceScaleIsRefused()
    {
        foreach (var scale in new[]
                 {
                     new Vector3(float.NaN, 1f, 1f),
                     new Vector3(1f, float.NaN, 1f),
                     new Vector3(1f, 1f, float.NaN),
                     new Vector3(float.PositiveInfinity, 1f, 1f),
                     new Vector3(1f, float.NegativeInfinity, 1f),
                     new Vector3(1f, 1f, float.PositiveInfinity),
                 })
        {
            var a = Rig("a", 2);
            a.ReferencePose[1] = new HkxBonePose(Vector3.Zero, Quaternion.Identity, scale);
            var b = Rig("b", 2);

            Assert.Contains(SkeletonMapperAuthor.Refusals(a),
                            refusal => refusal.Contains("non-finite reference scale", StringComparison.Ordinal));
            Assert.Throws<ArgumentException>(() => SkeletonMapperAuthor.FromReferencePose(a, b, new[] { (0, 0) }));
        }
    }

    [Fact]
    public void ShippedReferenceScaleIsWithinTheAuthoringTolerance()
    {
        var (forward, reverse) = Vanilla();
        float worstComponent = 0f;
        float worstDistance = 0f;

        foreach (var skeleton in new[] { forward.SkeletonA, forward.SkeletonB, reverse.SkeletonA, reverse.SkeletonB })
        {
            float component = 0f;
            foreach (var pose in skeleton.ReferencePose)
                component = MathF.Max(component, MathF.Max(MathF.Abs(pose.Scale.X - 1f),
                                         MathF.Max(MathF.Abs(pose.Scale.Y - 1f), MathF.Abs(pose.Scale.Z - 1f))));

            float distance = skeleton.ReferencePose.Select(pose => Vector3.Distance(pose.Scale, Vector3.One))
                                                   .DefaultIfEmpty(0f).Max();

            _out.WriteLine($"{skeleton.Name} ({skeleton.BoneNames.Count} bones): worst component deviation " +
                           $"{component:0.####e+0}, worst distance from unit scale {distance:0.####e+0}");

            worstComponent = MathF.Max(worstComponent, component);
            worstDistance = MathF.Max(worstDistance, distance);
        }

        _out.WriteLine($"worst shipped reference scale deviation {worstComponent:0.####e+0} " +
                       $"(distance {worstDistance:0.####e+0})");

        Assert.InRange(worstComponent, 1.9e-4f, 2.1e-4f);
        Assert.True(worstComponent < SkeletonMapperAuthor.ScaleTolerance,
                    $"shipped scale deviation {worstComponent} exceeds the authoring tolerance " +
                    $"{SkeletonMapperAuthor.ScaleTolerance}; the shipped rig would be refused");
    }

    [Fact]
    public void AuthoringRefusesAMalformedSkeletonInsteadOfComputingFromIt()
    {
        var a = Rig("a", 3);
        var b = Rig("b", 2);
        a.ReferencePose[2] = new HkxBonePose(Vector3.Zero, new Quaternion(1f, 2f, 0f, 0f), Vector3.One);

        Assert.Throws<ArgumentException>(() => SkeletonMapperAuthor.FromReferencePose(a, b, new[] { (0, 0) }));
    }

    [Fact]
    public void ReverseRefusesAMalformedRowInsteadOfInvertingIt()
    {
        Assert.Contains(SkeletonMapperAuthor.Refusals(new[]
                        {
                            new HavokRagdollMapping(0, 0, new Vector3(float.NaN, 0f, 0f), Quaternion.Identity),
                        }),
                        refusal => refusal.Contains("cannot be inverted", StringComparison.Ordinal));

        Assert.Contains(SkeletonMapperAuthor.Refusals(new[]
                        {
                            new HavokRagdollMapping(0, 0, Vector3.Zero, new Quaternion(0f, 0f, 0f, 0f)),
                        }),
                        refusal => refusal.Contains("cannot be inverted", StringComparison.Ordinal));

        Assert.Throws<ArgumentException>(() => SkeletonMapperAuthor.Reverse(new[]
        {
            new HavokRagdollMapping(0, 0, Vector3.Zero, new Quaternion(0f, 0f, 0f, 0f)),
        }));
    }

    private static HavokRagdollSkeleton Rig(string name, int bones)
    {
        var skeleton = new HavokRagdollSkeleton { Name = name };
        for (int i = 0; i < bones; i++)
        {
            skeleton.BoneNames.Add($"bone{i}");
            skeleton.ParentIndices.Add(i - 1);
            skeleton.ReferencePose.Add(new HkxBonePose(new Vector3(i, 0f, 0f), Quaternion.Identity, Vector3.One));
        }
        return skeleton;
    }

    private void Compare(IReadOnlyList<HavokRagdollMapping> shipped, IReadOnlyList<HavokRagdollMapping> authored)
    {
        float worstTranslation = 0f;
        float worstRotation = 0f;

        for (int i = 0; i < shipped.Count; i++)
        {
            Assert.Equal(shipped[i].BoneA, authored[i].BoneA);
            Assert.Equal(shipped[i].BoneB, authored[i].BoneB);

            worstTranslation = MathF.Max(worstTranslation,
                Vector3.Distance(shipped[i].AFromBTranslation, authored[i].AFromBTranslation));
            worstRotation = MathF.Max(worstRotation,
                RotationDistance(shipped[i].AFromBRotation, authored[i].AFromBRotation));
        }

        _out.WriteLine($"worst authored translation error {worstTranslation:0.####e+0}, rotation error {worstRotation:0.####e+0} over {shipped.Count} rows");

        Assert.True(worstTranslation < 5e-4f, $"worst translation error {worstTranslation}");
        Assert.True(worstRotation < 1e-5f, $"worst rotation error {worstRotation}");
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance, string what) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"{what}: {expected} against {actual}");

    private static void AssertRotationClose(Quaternion expected, Quaternion actual, float tolerance, string what) =>
        Assert.True(RotationDistance(expected, actual) < tolerance, $"{what}: {expected} against {actual}");

    private static float RotationDistance(Quaternion a, Quaternion b)
    {
        if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f) return float.MaxValue;
        a = Quaternion.Normalize(a);
        b = Quaternion.Normalize(b);
        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), -1f, 1f));
    }

    private static (HavokSkeletonMapper Forward, HavokSkeletonMapper Reverse) Vanilla()
    {
        var objects = new PackfileObjects(PackfileImage.Read(FixturePath()));
        var mappers = SkeletonMapperReader.Read(objects);
        Assert.Equal(2, mappers.Count);

        var forward = Assert.Single(mappers, mapper => mapper.SkeletonB.BoneNames.Count == 18);
        var reverse = Assert.Single(mappers, mapper => mapper.SkeletonB.BoneNames.Count == 95);
        return (forward, reverse);
    }

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "vanilla", "Meshes", "Actors", "Character",
                     "CharacterAssets", "skeleton.hkx");
}
