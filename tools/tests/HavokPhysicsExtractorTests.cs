using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class HavokPhysicsExtractorTests
{
    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    private const string Skeleton =
        "Meshes/Actors/Character/CharacterAssets/skeleton.hkx";

    [Fact]
    public void VanillaSkeletonExtractsMeasuredRagdollCounts()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        Assert.Equal(18, model!.Bodies.Count);
        Assert.Equal(19, model.Shapes.Count);
        Assert.Equal(17, model.Constraints.Count);
        Assert.Equal(18, model.BoneBindings.Count);
    }

    [Fact]
    public void BodyFieldsMatchXmlValues()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var body = Assert.Single(model!.Bodies, b => b.Name == "Ragdoll_NPC L Thigh");
        Assert.Equal(1, body.Id);
        Assert.Equal(99, body.ShapeId);
        AssertNear(new Vector3(-6.6150898933410645f, 4.546909185592085E-4f, 68.91129302978516f),
                   body.Position, 1e-3f);
        AssertNear(new Quaternion(-0.10920292884111404f, 0.7402823567390442f,
                                  0.09750235825777054f, 0.6561630964279175f),
                   body.Rotation, 1e-3f);
    }

    [Fact]
    public void CapsuleShapeCarriesMeasuredPolytope()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var shape = model!.Shapes.FirstOrDefault(s => s.Id == 96);
        Assert.NotNull(shape);
        Assert.Equal(HavokPhysicsShapeKind.Capsule, shape!.Kind);
        Assert.Equal(7, shape.Vertices.Count);
        Assert.Equal(5, shape.FaceRings.Count);
        AssertNear(new Vector3(0.16652747988700867f, -0.16613319516181946f, 1.934882402420044f),
                   shape.Vertices[0], 1e-3f);
        Assert.Equal(new[] { 7, 6, 2, 3 }, shape.FaceRings[0]);
    }

    [Fact]
    public void ConstraintCinfosProduceMeasuredGraph()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var pairs = model!.Constraints.Select(c => (c.BodyA, c.BodyB)).ToList();
        Assert.Equal(new[]
        {
            (1, 0), (2, 0), (3, 0), (4, 1), (5, 2), (6, 3), (7, 4), (8, 5), (9, 6),
            (10, 9), (11, 9), (12, 9), (13, 10), (14, 11), (15, 12), (16, 13), (17, 15),
        }, pairs);

        Assert.Equal(9, model.Constraints.Count(c => c.Kind == HavokConstraintKind.Ragdoll));
        Assert.Equal(8, model.Constraints.Count(c => c.Kind == HavokConstraintKind.LimitedHinge));
        Assert.DoesNotContain(model.Constraints, c => c.Kind == HavokConstraintKind.Unknown);
    }

    [Fact]
    public void RagdollConstraintCarriesMeasuredPivotsAndLimits()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var constraint = Assert.Single(model!.Constraints, c => c.Id == 0);
        Assert.Equal(HavokConstraintKind.Ragdoll, constraint.Kind);
        Assert.Equal(1, constraint.BodyA);
        Assert.Equal(0, constraint.BodyB);

        Assert.NotNull(constraint.FrameA);
        Assert.Equal(16, constraint.FrameA!.Length);
        AssertNear(new Vector3(0f, 0f, 0f), Pivot(constraint.FrameA), 1e-3f);

        Assert.NotNull(constraint.FrameB);
        AssertNear(new Vector3(2.897924787248485E-6f, 4.2483024299144745E-4f, 6.615072250366211f),
                   Pivot(constraint.FrameB!), 1e-3f);

        Assert.Equal(0, constraint.TwistAxis);
        Assert.Equal(1, constraint.TwistRefAxis);
        AssertNear(-0.2617993950843811f, constraint.TwistMinAngle!.Value, 1e-4f);
        AssertNear(0.2617993950843811f, constraint.TwistMaxAngle!.Value, 1e-4f);

        Assert.Equal(0, constraint.ConeTwistAxis);
        Assert.Equal(0, constraint.ConeRefAxis);
        AssertNear(0.5235987901687622f, constraint.ConeMaxAngle!.Value, 1e-4f);
    }

    [Fact]
    public void LimitedHingeConstraintCarriesMeasuredHingeLimit()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var constraint = Assert.Single(model!.Constraints, c => c.Kind == HavokConstraintKind.LimitedHinge && c.BodyA == 4);
        Assert.Equal(1, constraint.BodyB);
        Assert.Equal(0, constraint.LimitAxis);
        AssertNear(-1.919862151145935f, constraint.MinAngle, 1e-4f);
        AssertNear(0.031415924429893494f, constraint.MaxAngle, 1e-4f);

        Assert.NotNull(constraint.FrameB);
        AssertNear(new Vector3(31.592397689819336f, -0.03480081632733345f, -0.41684818267822266f),
                   Pivot(constraint.FrameB!), 1e-3f);
    }

    [Fact]
    public void EveryMeasuredConstraintKindCarriesItsLimits()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        foreach (var constraint in model!.Constraints.Where(c => c.Kind == HavokConstraintKind.Ragdoll))
        {
            Assert.NotNull(constraint.FrameA);
            Assert.NotNull(constraint.FrameB);
            Assert.True(constraint.TwistMinAngle.HasValue);
            Assert.True(constraint.TwistMaxAngle.HasValue);
            Assert.True(constraint.ConeMaxAngle.HasValue);
        }

        foreach (var constraint in model.Constraints.Where(c => c.Kind == HavokConstraintKind.LimitedHinge))
        {
            Assert.NotNull(constraint.FrameA);
            Assert.NotNull(constraint.FrameB);
            Assert.True(constraint.MinAngle < constraint.MaxAngle);
        }
    }

    [Fact]
    public void BoneToBodyMapIsMeasured()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        Assert.All(model!.BoneBindings, binding =>
        {
            Assert.Equal(binding.BoneIndex, binding.BodyId);
            Assert.InRange(binding.BoneIndex, 0, 17);
            Assert.InRange(binding.BodyId, 0, model.Bodies.Count - 1);
        });
    }

    [Fact]
    public void PhysicsSkeletonCarriesMeasuredBoneNames()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var skeleton = model!.Skeleton;
        Assert.NotNull(skeleton);
        Assert.Equal("Ragdoll_NPC COM", skeleton!.Name);
        Assert.Equal(18, skeleton.BoneNames.Count);
        Assert.Equal("Ragdoll_NPC COM", skeleton.BoneNames[0]);
        Assert.Equal("Ragdoll_NPC L Thigh", skeleton.BoneNames[1]);
        Assert.Equal("Ragdoll_NPC R Calf", skeleton.BoneNames[5]);
        Assert.Equal("Ragdoll_NPC R Foot", skeleton.BoneNames[8]);
        Assert.Equal("Ragdoll_NPC L Forearm", skeleton.BoneNames[13]);
    }

    [Fact]
    public void PhysicsSkeletonParentIndicesMatchXml()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var parents = model!.Skeleton!.ParentIndices;
        Assert.Equal(18, parents.Count);
        Assert.Equal(new[]
        {
            -1, 0, 0, 0, 1, 2, 3, 4, 5, 6, 9, 9, 9, 10, 11, 12, 13, 15,
        }, parents);
    }

    [Fact]
    public void PhysicsSkeletonReferencePoseMatchesXml()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var poses = model!.Skeleton!.ReferencePose;
        Assert.Equal(18, poses.Count);

        // Translations (the translation column of each measured hkQsTransform).
        AssertNear(new Vector3(-1.668929871811997E-5f, 2.1100042431498878E-5f, 68.91128540039062f),
                   poses[0].Translation, 1e-3f);
        AssertNear(new Vector3(7.62939453125E-6f, 4.249164485372603E-4f, 6.615072250366211f),
                   poses[1].Translation, 1e-3f);
        AssertNear(new Vector3(3.7916259765625f, 2.0265579223632812E-6f, -7.011654815869406E-6f),
                   poses[3].Translation, 1e-3f);
        AssertNear(new Vector3(31.592391967773438f, -0.03480100631713867f, -0.41684722900390625f),
                   poses[4].Translation, 1e-3f);

        // Rotations (the quaternion column of each measured hkQsTransform).
        AssertNear(new Quaternion(-4.6361520844584447E-7f, -0.7071065306663513f,
                                  -4.6361529371097276E-7f, 0.7071069478988647f),
                   poses[0].Rotation, 1e-4f);
        AssertNear(new Quaternion(-0.008273639716207981f, 0.9874358773231506f,
                                  0.14616334438323975f, -0.05948099493980408f),
                   poses[1].Rotation, 1e-4f);
        AssertNear(new Quaternion(7.332672566917608E-7f, -4.470348358154297E-7f,
                                  0.06121326982975006f, 0.9981245994567871f),
                   poses[3].Rotation, 1e-4f);

        // Scale is identity in the measured file (the hkQsTransform scale column).
        Assert.All(poses, pose => AssertNear(Vector3.One, pose.Scale, 1e-3f));
    }

    [Fact]
    public void MotionCinfosProvideMeasuredWorldCenterOfMass()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var com = Assert.Single(model!.Bodies, b => b.Id == 0);
        AssertNear(new Vector3(0.020323412492871284f, -0.0491427443921566f, 69.19477844238281f),
                   com.CenterOfMass, 1e-3f);

        // The motion id mapping is not identity in this file (R Thigh body 2 uses motion 4,
        // L Calf body 4 uses motion 2), so these pin both the lookup and the values.
        var leftThigh = Assert.Single(model.Bodies, b => b.Id == 1);
        AssertNear(new Vector3(-8.432622909545898f, -0.5323612093925476f, 53.22739028930664f),
                   leftThigh.CenterOfMass, 1e-3f);

        var rightThigh = Assert.Single(model.Bodies, b => b.Id == 2);
        AssertNear(new Vector3(8.431289672851562f, -0.5326367616653442f, 53.22724914550781f),
                   rightThigh.CenterOfMass, 1e-3f);

        var leftCalf = Assert.Single(model.Bodies, b => b.Id == 4);
        AssertNear(new Vector3(-11.501089096069336f, -3.5246798992156982f, 21.728782653808594f),
                   leftCalf.CenterOfMass, 1e-3f);
    }

    [Fact]
    public void EveryBodyCarriesAFiniteMeasuredCom()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        Assert.All(model!.Bodies, body =>
        {
            // The fixture measures a center of mass for every body; zero is the sentinel
            // for "no measured COM", so a non-zero finite value proves the read happened.
            Assert.NotEqual(Vector3.Zero, body.CenterOfMass);
            Assert.True(Finite(body.CenterOfMass), $"{body.Name} center of mass is not finite");
        });

        // The COM is genuinely the motion cinfo value, not a copy of the body position:
        // the L Thigh body sits at z 68.91 while its measured COM is at z 53.23, down the
        // capsule toward the knee.
        var thigh = Assert.Single(model.Bodies, b => b.Id == 1);
        Assert.True(MathF.Abs(thigh.CenterOfMass.Z - thigh.Position.Z) > 10f);
    }

    [Fact]
    public void PhysicsSkeletonWorldPoseAgreesWithBodyPositions()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // The composed reference pose and the measured body cinfo positions are two
        // independent sources for the same file-space placement; the bindings say which
        // body a bone owns, so each bone's composed world position must sit on its body.
        var world = model!.Skeleton!.WorldReferencePositions();
        Assert.Equal(18, world.Count);

        foreach (var binding in model.BoneBindings)
        {
            var body = Assert.Single(model.Bodies, b => b.Id == binding.BodyId);
            Assert.True(Vector3.Distance(world[binding.BoneIndex], body.Position) <= 5e-2f,
                        $"bone {binding.BoneIndex} world {world[binding.BoneIndex]} vs body {body.Id} " +
                        $"{body.Position}");
        }

        // Pin one explicitly to the XML-derived hand-computed value: the root sits at its
        // local translation and the thigh bone hangs below it along the parent's -90 degree
        // Y rotation, landing on the measured L Thigh body position.
        AssertNear(new Vector3(-1.668929871811997E-5f, 2.1100042431498878E-5f, 68.91128540039062f),
                   world[0], 1e-3f);
        AssertNear(new Vector3(-6.6150898933410645f, 4.546909185592E-4f, 68.91129302978516f),
                   world[1], 1e-3f);
    }

    [Fact]
    public void PhysicsSkeletonChainIsTopologicallyOrdered()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var skeleton = model!.Skeleton!;
        var world = skeleton.WorldReferencePositions();

        // Every non-root link connects two finite, distinct file-space points and always
        // reaches back to an earlier bone (valid skeletons are topologically ordered).
        for (int i = 0; i < skeleton.ParentIndices.Count; i++)
        {
            int parent = skeleton.ParentIndices[i];
            if (parent < 0) continue;

            Assert.True(parent < i, $"bone {i} parent {parent} is not topologically ordered");
            Assert.True(Finite(world[i]), $"bone {i} world position is not finite");
            Assert.True(Finite(world[parent]), $"bone {parent} world position is not finite");
            Assert.True(Vector3.Distance(world[i], world[parent]) > 0f,
                        $"bone {i} sits on its parent {parent}");
        }
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.X) && !float.IsInfinity(value.X) &&
        !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) &&
        !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);

    [Fact]
    public void BoneBindingsResolveBodyNamesForLabels()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // Every binding must point at a body we can label with the skeleton bone's name.
        foreach (var binding in model!.BoneBindings)
        {
            var body = Assert.Single(model.Bodies, b => b.Id == binding.BodyId);
            var boneName = model.Skeleton!.BoneNames[binding.BoneIndex];
            Assert.NotEmpty(boneName);
            Assert.NotEmpty(body.Name);
        }

        var thigh = Assert.Single(model.BoneBindings, binding => binding.BoneIndex == 1);
        Assert.Equal(1, thigh.BodyId);
        Assert.Equal("Ragdoll_NPC L Thigh", model.Skeleton!.BoneNames[thigh.BoneIndex]);
        Assert.Equal("Ragdoll_NPC L Thigh", model.Bodies.Single(b => b.Id == thigh.BodyId).Name);
    }

    [Fact]
    public void ValidatorFlagsBrokenSkeletonConsistency()
    {
        var model = new HavokRagdollModel
        {
            Skeleton = new HavokRagdollSkeleton { Name = "broken" },
        };
        model.Skeleton!.BoneNames.AddRange(new[] { "A", "B" });
        model.Skeleton.ParentIndices.Add(5);
        model.Skeleton.ReferencePose.Add(new HkxBonePose(new Vector3(0, 0, 0), Quaternion.Identity, Vector3.One));
        model.Skeleton.ReferencePose.Add(new HkxBonePose(
            new Vector3(float.NaN, 0, 0), Quaternion.Identity, new Vector3(2, 2, 2)));
        model.BoneBindings.Add(new HavokRagdollBoneBinding(7, 0));

        var findings = HavokPhysicsValidator.Check(model);
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("parent index 5"));
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("2-bone physics skeleton"));
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("1 parent indices"));
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("reference pose"));
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("missing body 0"));
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Warning && f.Message.Contains("not identity"));
    }

    [Fact]
    public void ExtractedModelPassesTheValidator()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var findings = HavokPhysicsValidator.Check(model!, skeletonBoneCount: 18);
        Assert.DoesNotContain(findings, finding => finding.Level == HavokPhysicsValidationLevel.Error);
    }

    [Fact]
    public void ClothHairFixtureHasNoRagdollModel()
    {
        string fixture = Fixture("Meshes/Actors/Character/CharacterAssets/Hair/Female/FemaleHair04.hkx");
        Assert.Null(HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(fixture)));
    }

    [Fact]
    public void UnreadableInputFailsClosed()
    {
        Assert.Null(HavokPhysicsExtractor.TryExtract(null));
        Assert.Null(HavokPhysicsExtractor.TryExtract(Array.Empty<byte>()));
        Assert.Null(HavokPhysicsExtractor.TryExtract(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
    }

    private static Vector3 Pivot(float[] frame) => new(frame[12], frame[13], frame[14]);

    private static void AssertNear(float expected, float actual, float tolerance) =>
        Assert.True(MathF.Abs(expected - actual) <= tolerance, $"expected {expected}, measured {actual}");

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance,
                    $"expected {expected}, measured {actual}");

    private static void AssertNear(Quaternion expected, Quaternion actual, float tolerance)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= tolerance &&
                    MathF.Abs(expected.Y - actual.Y) <= tolerance &&
                    MathF.Abs(expected.Z - actual.Z) <= tolerance &&
                    MathF.Abs(expected.W - actual.W) <= tolerance,
                    $"expected {expected}, measured {actual}");
    }

    [Fact]
    public void AnimationToRagdollMapperIsMeasured()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        Assert.Equal(18, model!.Mappings.Count);

        // The mapper is animation (skeletonA = Root, 95 bones) -> ragdoll
        // (skeletonB = the ragdoll skeleton hknpRagdollData references).
        var animation = model.AnimationSkeleton;
        Assert.NotNull(animation);
        Assert.Equal("Root", animation!.Name);
        Assert.Equal(95, animation.BoneNames.Count);
        Assert.Equal(model.Skeleton!.BoneNames.Count, model.Skeleton.BoneNames.Count);

        // Every physics bone is covered exactly once by the measured mappings.
        var covered = model.Mappings.Select(m => m.BoneB).OrderBy(b => b).ToArray();
        Assert.Equal(Enumerable.Range(0, model.Skeleton.BoneNames.Count), covered);

        // Pin individual mappings to the byte-backed XML values: the aFromB translation is
        // the first three floats of the measured hkQsTransform, the rotation the quaternion.
        var root = model.Mappings[0];
        Assert.Equal(1, root.BoneA);
        Assert.Equal(0, root.BoneB);

        var thigh = model.Mappings[1];
        Assert.Equal(3, thigh.BoneA);
        Assert.Equal(1, thigh.BoneB);
        AssertNear(new Vector3(7.62939453125E-6f, -9.5367431640625E-7f, -3.62396240234375E-5f),
                   thigh.AFromBTranslation, 1e-6f);
        AssertNear(new Quaternion(-3.6200135946273804E-4f, -0.006596028804779053f,
                                  5.562007427215576E-4f, 0.9999775886535645f),
                   thigh.AFromBRotation, 1e-6f);

        var forearm = model.Mappings[17];
        Assert.Equal(26, forearm.BoneA);
        Assert.Equal(17, forearm.BoneB);
    }

    // Compose a skeleton's local reference pose down its parent chain into world frames, in
    // the same space the app's AnimationPose produces (position + rotation per bone).
    private static HavokBoneFrame[] WorldFrames(HavokRagdollSkeleton skeleton)
    {
        var frames = new HavokBoneFrame[skeleton.BoneNames.Count];
        for (int i = 0; i < skeleton.BoneNames.Count; i++)
        {
            var local = skeleton.ReferencePose[i];
            int parent = skeleton.ParentIndices[i];
            if (parent >= 0 && parent < i)
                frames[i] = new HavokBoneFrame(
                    frames[parent].Position + Vector3.Transform(local.Translation, frames[parent].Rotation),
                    Quaternion.Normalize(frames[parent].Rotation * local.Rotation));
            else
                frames[i] = new HavokBoneFrame(local.Translation, local.Rotation);
        }
        return frames;
    }

    [Fact]
    public void MappingTheAnimationReferencePoseYieldsTheRagdollReference()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        Assert.True(model!.Mappings.Count > 0);
        Assert.NotNull(model.AnimationSkeleton);

        var mapped = model.MapPose(WorldFrames(model.AnimationSkeleton!));
        Assert.NotNull(mapped);
        Assert.Equal(model.Skeleton!.BoneNames.Count, mapped!.Length);

        // The measured composition worldB = worldA * aFromBTransform, fed with the animation
        // reference pose, must land on the ragdoll's own measured reference world: two
        // independent measured sources for the same file-space placement.
        var expected = model.Skeleton.WorldReferencePositions();
        var expectedRotations = WorldRotations(model.Skeleton);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.True(Vector3.Distance(expected[i], mapped[i].Position) <= 5e-3f,
                        $"bone {i} mapped {mapped[i].Position} vs reference {expected[i]}");
            Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(mapped[i].Rotation),
                                                 expectedRotations[i])) >= 0.999f,
                        $"bone {i} mapped rotation {mapped[i].Rotation} vs {expectedRotations[i]}");
        }
    }

    private static Quaternion[] WorldRotations(HavokRagdollSkeleton skeleton)
    {
        var rotations = new Quaternion[skeleton.BoneNames.Count];
        for (int i = 0; i < skeleton.BoneNames.Count; i++)
        {
            int parent = skeleton.ParentIndices[i];
            rotations[i] = parent >= 0 && parent < i
                ? Quaternion.Normalize(rotations[parent] * skeleton.ReferencePose[i].Rotation)
                : skeleton.ReferencePose[i].Rotation;
        }
        return rotations;
    }

    [Fact]
    public void MapPoseFollowsAWholeRagdollTranslation()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        Assert.True(model!.Mappings.Count > 0);
        Assert.NotNull(model.AnimationSkeleton);

        // worldB = worldA * aFromB is equivariant under a rigid translation of the whole
        // animation pose, so moving every animation bone by a constant shift must move every
        // mapped ragdoll bone by exactly that shift. This pins the composition orientation:
        // the aFromB offset rides along with the source frame.
        var reference = model.MapPose(WorldFrames(model.AnimationSkeleton!))!;
        var offset = new Vector3(10f, -3f, 4.5f);

        var shifted = WorldFrames(model.AnimationSkeleton!);
        for (int i = 0; i < shifted.Length; i++)
            shifted[i] = new HavokBoneFrame(shifted[i].Position + offset, shifted[i].Rotation);

        var moved = model.MapPose(shifted)!;
        for (int i = 0; i < moved.Length; i++)
            Assert.True(Vector3.Distance(reference[i].Position + offset, moved[i].Position) <= 1e-3f,
                        $"bone {i} did not ride the {offset} shift");
    }

    [Fact]
    public void MapPoseFollowsAWholeRagdollRotation()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        Assert.True(model!.Mappings.Count > 0);
        Assert.NotNull(model.AnimationSkeleton);

        // Rotating every animation bone about the world origin by a quarter turn must rotate
        // every mapped ragdoll frame by the same quarter turn: the composition is
        // rotation-equivariant too, so the ragdoll follows the animated character's turn.
        var reference = model.MapPose(WorldFrames(model.AnimationSkeleton!))!;
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);

        var rotated = WorldFrames(model.AnimationSkeleton!);
        for (int i = 0; i < rotated.Length; i++)
            rotated[i] = new HavokBoneFrame(Vector3.Transform(rotated[i].Position, turn),
                                            Quaternion.Normalize(turn * rotated[i].Rotation));

        var moved = model.MapPose(rotated)!;
        for (int i = 0; i < moved.Length; i++)
        {
            Assert.True(Vector3.Distance(Vector3.Transform(reference[i].Position, turn),
                                         moved[i].Position) <= 2e-3f,
                        $"bone {i} did not follow the turn");
            Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Normalize(turn * reference[i].Rotation),
                                                 Quaternion.Normalize(moved[i].Rotation))) >= 0.999f,
                        $"bone {i} rotation did not follow the turn");
        }
    }

    [Fact]
    public void MapPoseFailsClosedWithoutAUsableMapper()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        Assert.Null(model!.MapPose(null));
        Assert.Null(model.MapPose(Array.Empty<HavokBoneFrame>()));

        // A truncated pose cannot drive every mapping's boneA, so nothing is posed.
        Assert.Null(model.MapPose(new[] { new HavokBoneFrame(Vector3.Zero, Quaternion.Identity) }));

        var bare = new HavokRagdollModel { Skeleton = new HavokRagdollSkeleton() };
        Assert.Null(bare.MapPose(new[] { new HavokBoneFrame(Vector3.Zero, Quaternion.Identity) }));
    }

    [Fact]
    public void ReleasedRagdollSettlesUnderGravityWithoutASolver()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(Fixture(Skeleton));
        Assert.NotNull(skeleton);

        // Release from an airborne mapped pose (the reference lifted 20 units), so there is
        // a real fall: the release state is the ragdoll's current physics frames, and the
        // settle is the solver-free constrained gravity drop - each body integrates its own
        // velocity, pivots are re-projected to the captured offset, and every joint's
        // relative rotation is clamped to the measured twist/cone/hinge limits.
        var reference = AnimationPose.ReferencePose(skeleton);
        var lifted = new AnimationPose.Pose();
        foreach (var b in reference.Bones)
            lifted.Bones.Add(new AnimationPose.Bone(b.Index, b.Name, b.Parent,
                                                    b.Position + new Vector3(0, 0, 20), b.Rotation));

        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(lifted);
        view.StartDrop();
        Assert.True(view.IsDropped);

        int steps = 0;
        while (!view.DropResting && steps < 600) { view.AdvanceDrop(1f / 60f); steps++; }
        Assert.True(view.DropResting, "the drop settles");
        Assert.True(steps is > 5 and < 600, $"the drop moved and finished in {steps} steps");

        var settled = view.DroppedPositions!;
        var settledRot = view.DroppedRotations!;
        Assert.Equal(18, settled.Count);
        foreach (var p in settled)
        {
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
            Assert.True(p.Z >= -0.05f, $"bone settled underground at z={p.Z}");
        }

        // This slice replaces the rigid drop of #262: with per-body gravity the ragdoll
        // folds and buckles, so the root falls well beyond the rigid ground-stop distance
        // (which only lowers the whole pose by its lowest vertex clearance).
        var mapped = model.MapPose(lifted.Bones
            .Select(b => new HavokBoneFrame(b.Position, b.Rotation)).ToArray())!;
        float fell = mapped[0].Position.Z - settled[0].Z;
        Assert.True(fell > view.DropDistance + 5f,
            $"the root folded {fell:F2} units, well beyond the rigid drop {view.DropDistance:F2}");
        Assert.True(view.DropDistance > 15f, "the release was not a no-op");

        // Every constraint keeps its pivots within the projection residual and holds its
        // measured limits exactly, and the settle is deterministic.
        const float pivotEps = 1.5f;   // measured 0.87 with the fixed 20-pass iteration count
        const float limitEps = 0.01f;  // radians, measured at the limit boundary exactly
        float worstPivot = 0;
        foreach (var c in model.Constraints.Where(c => c.FrameA != null && c.FrameB != null))
        {
            int ba = -1, bb = -1;
            foreach (var binding in model.BoneBindings)
            {
                if (binding.BodyId == c.BodyA) ba = binding.BoneIndex;
                if (binding.BodyId == c.BodyB) bb = binding.BoneIndex;
            }
            if (ba < 0 || bb < 0) continue;
            var offA = new Vector3(c.FrameA![12], c.FrameA[13], c.FrameA[14]);
            var offB = new Vector3(c.FrameB![12], c.FrameB[13], c.FrameB[14]);
            float captured = Vector3.Distance(
                mapped[ba].Position + Vector3.Transform(offA, mapped[ba].Rotation),
                mapped[bb].Position + Vector3.Transform(offB, mapped[bb].Rotation));
            float settledDist = Vector3.Distance(
                settled[ba] + Vector3.Transform(offA, settledRot[ba]),
                settled[bb] + Vector3.Transform(offB, settledRot[bb]));
            worstPivot = Math.Max(worstPivot, Math.Abs(settledDist - captured));

            AssertLimitsWithin(model, c, settled, settledRot, limitEps);
        }
        Assert.True(worstPivot < pivotEps, $"a joint stretched by {worstPivot:F3} units");

        // A second run must land on the exact same arrays.
        var view2 = new SkeletonView();
        view2.SetBodies(model);
        view2.Show(lifted);
        view2.StartDrop();
        int steps2 = 0;
        while (!view2.DropResting && steps2 < 600) { view2.AdvanceDrop(1f / 60f); steps2++; }
        Assert.True(view2.DroppedPositions!.SequenceEqual(settled) &&
                    view2.DroppedRotations!.SequenceEqual(settledRot),
            "the settle is deterministic");
    }

    // Assert the joint's relative rotation respects the measured limits, measured in the
    // same convention the simulation clamps with: ragdoll twist/cone around the file's A
    // frame axes, hinge sweep of the tree child's local X against the parent->child
    // direction around the limit axis.
    private static void AssertLimitsWithin(HavokRagdollModel model, HavokConstraint c,
        IReadOnlyList<Vector3> settled, IReadOnlyList<Quaternion> settledRot, float eps)
    {
        int ba = -1, bb = -1;
        foreach (var binding in model.BoneBindings)
        {
            if (binding.BodyId == c.BodyA) ba = binding.BoneIndex;
            if (binding.BodyId == c.BodyB) bb = binding.BoneIndex;
        }
        if (ba < 0 || bb < 0) return;

        if (c.Kind == HavokConstraintKind.Ragdoll)
        {
            var rel = Quaternion.Normalize(Quaternion.Inverse(settledRot[ba]) * settledRot[bb]);
            if (c.TwistMinAngle is float mn && c.TwistMaxAngle is float mx)
            {
                float twist = SkeletonView.TwistAngle(rel, SkeletonView.Column(c.FrameA!, c.TwistAxis));
                Assert.True(twist >= mn - eps && twist <= mx + eps,
                    $"constraint {c.Id} twist {twist * 57.2958f:F1}° outside [{mn * 57.2958f:F1}°,{mx * 57.2958f:F1}°]");
            }
            if (c.ConeMaxAngle is float cone)
            {
                float swing = SkeletonView.SwingAngle(rel, SkeletonView.Column(c.FrameA!, c.ConeTwistAxis));
                Assert.True(swing <= cone + eps,
                    $"constraint {c.Id} swing {swing * 57.2958f:F1}° exceeds cone {cone * 57.2958f:F1}°");
            }
            return;
        }

        // Hinge: the sweep of the tree child's local X against the parent->child direction
        // around the limit axis. The tree child is the side further from the root body in a
        // BFS over the constraint graph (the same map the simulation clamps with), because
        // the file's A side is not always the kinematic child.
        if (c.Kind is not (HavokConstraintKind.Hinge or HavokConstraintKind.LimitedHinge)) return;
        if (c.MinAngle == 0 && c.MaxAngle == 0) return;
        int ai = model.Bodies.FindIndex(b => b.Id == c.BodyA);
        int bi = model.Bodies.FindIndex(b => b.Id == c.BodyB);
        if (ai < 0 || bi < 0 || Depth(model, ai) == Depth(model, bi)) return;
        bool childIsA = Depth(model, ai) > Depth(model, bi);
        int childBone = childIsA ? ba : bb;
        int parentBone = childIsA ? bb : ba;

        var axis = Vector3.Normalize(SkeletonView.WorldDir(c.FrameA!, c.LimitAxis, settledRot[ba]));
        var toward = settled[childBone] - settled[parentBone];
        var perpendicular = toward - axis * Vector3.Dot(toward, axis);
        if (perpendicular.LengthSquared() < 1e-8f) return;
        var reference = Vector3.Normalize(perpendicular);
        var childFrame = childIsA ? c.FrameA! : c.FrameB!;
        var childLocal = Vector3.Normalize(SkeletonView.WorldDir(childFrame, 0, settledRot[childBone]));
        var childPerp = childLocal - axis * Vector3.Dot(childLocal, axis);
        if (childPerp.LengthSquared() < 1e-8f) return;
        float sweep = SkeletonView.SignedAngleAround(axis, reference, Vector3.Normalize(childPerp));
        Assert.True(sweep >= c.MinAngle - eps && sweep <= c.MaxAngle + eps,
            $"constraint {c.Id} hinge {sweep * 57.2958f:F1}° outside [{c.MinAngle * 57.2958f:F1}°,{c.MaxAngle * 57.2958f:F1}°]");
    }

    // BFS depth from the first body over the constraint graph: the kinematic parent is the
    // body closer to the root, so the child is the side this simulation clamps.
    private static int Depth(HavokRagdollModel model, int start)
    {
        var depth = new Dictionary<int, int> { [model.Bodies[0].Id] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(model.Bodies[0].Id);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int d = depth[current];
            foreach (var constraint in model.Constraints)
            {
                int other = constraint.BodyA == current ? constraint.BodyB
                    : constraint.BodyB == current ? constraint.BodyA : -1;
                if (other < 0 || depth.ContainsKey(other)) continue;
                depth[other] = d + 1;
                queue.Enqueue(other);
            }
        }
        return depth.TryGetValue(model.Bodies[start].Id, out int value) ? value : int.MaxValue;
    }

    [Fact]
    public void DropClearsBackToTheMappedPoseAndResetsWithTheView()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);
        var skeleton = new HkxBinaryReader().ReadSkeleton(Fixture(Skeleton));
        Assert.NotNull(skeleton);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.Show(AnimationPose.ReferencePose(skeleton!));
        view.StartDrop();
        Assert.True(view.IsDropped);
        view.AdvanceDrop(1f);
        Assert.True(view.IsDropped);

        view.ClearDrop();
        Assert.False(view.IsDropped);
        Assert.Null(view.DroppedPositions);

        view.StartDrop();
        view.Reset();
        Assert.False(view.IsDropped);
    }

    [Fact]
    public void RagdollFallsBackToTheSiblingSkeletonForAnAnimationFile()
    {
        // A real animation clip laid out next to its character skeleton exactly like the
        // vanilla fixture tree: Animations/<clip>.hkx + CharacterAssets/skeleton.hkx. The
        // clip itself has no ragdoll, so the app resolves the sibling skeleton - the same
        // file the pose pipeline poses with - and extracts the ragdoll + mapper from it.
        string root = Path.Combine(Path.GetTempPath(), "bgs-sibling-ragdoll-" + Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(root, "Meshes", "Actors", "Character", "CharacterAssets");
        string anims = Path.Combine(root, "Meshes", "Actors", "Character", "Animations", "Paired");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(anims);
        string clipPath = Path.Combine(anims, "clip.hkx");
        File.Copy(Fixture("Meshes/Actors/Character/Animations/Paired/" +
                          "PairedKill2HMBashKneeAndHead_AttackerLead.hkx"), clipPath);
        string skeletonPath = Path.Combine(assets, "skeleton.hkx");
        File.Copy(Fixture("Meshes/Actors/Character/CharacterAssets/skeleton.hkx"), skeletonPath);

        try
        {
            Assert.Equal(skeletonPath, MainWindow.SiblingSkeletonPath(clipPath));

            var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(skeletonPath));
            Assert.NotNull(model);
            Assert.True(model!.Bodies.Count > 0);
            Assert.Equal(18, model.Mappings.Count);

            // The mapper bridges the clip's rig: posing the sibling skeleton's reference
            // pose (what an animation would animate) through it drives all 18 ragdoll bones.
            var skeleton = new HkxBinaryReader().ReadSkeleton(skeletonPath);
            Assert.NotNull(skeleton);
            var pose = AnimationPose.ReferencePose(skeleton!);
            var source = pose.Bones.Select(b => new HavokBoneFrame(b.Position, b.Rotation)).ToArray();
            var mapped = model.MapPose(source);
            Assert.NotNull(mapped);
            Assert.Equal(18, mapped!.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConstraintInspectionLineShowsMeasuredLimitsAndPivots()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // The Playback summary line's per-constraint inspection: kind, ids, body names,
        // every measured limit in degrees and the local pivot offsets, all from the file.
        var ragdoll = model.Constraints.First(c => c.Id == 0);
        Assert.Equal("ragdoll: c0 Ragdoll_NPC L Thigh(1) -> Ragdoll_NPC COM(0) - " +
                     "twist -15.0..15.0 deg, cone 30.0 deg, " +
                     "pivotA (0.00, 0.00, 0.00), pivotB (0.00, 0.00, 6.62)",
                     SkeletonView.ConstraintInspectionLine(ragdoll, model.Bodies));

        var hinge = model.Constraints.First(c => c.Id == 3);
        Assert.Equal("limited hinge: c3 Ragdoll_NPC L Calf(4) -> Ragdoll_NPC L Thigh(1) - " +
                     "hinge -110.0..1.8 deg, " +
                     "pivotA (0.00, 0.00, 0.00), pivotB (31.59, -0.03, -0.42)",
                     SkeletonView.ConstraintInspectionLine(hinge, model.Bodies));

        // A kind with no measured limit atoms and no frames says so plainly, and unknown
        // body ids fall back to numbered names.
        var bare = new HavokConstraint { Id = 9, Kind = HavokConstraintKind.Hinge, BodyA = 42, BodyB = 43 };
        Assert.Equal("hinge: c9 body 42(42) -> body 43(43) - no measured limits",
                     SkeletonView.ConstraintInspectionLine(bare, model.Bodies));
    }

    [Fact]
    public void ConstraintPinCyclesOnClick()
    {
        // Click a constraint to pin it; click it again to release; click another to move
        // the pin; click with no constraint under the cursor to clear it.
        Assert.Equal(0, SkeletonView.PinnedAfterClick(-1, 0));
        Assert.Equal(2, SkeletonView.PinnedAfterClick(0, 2));
        Assert.Equal(-1, SkeletonView.PinnedAfterClick(0, 0));
        Assert.Equal(-1, SkeletonView.PinnedAfterClick(0, -1));
        Assert.Equal(-1, SkeletonView.PinnedAfterClick(-1, -1));
    }

    [Fact]
    public void ConstraintPinIsIndependentOfHoverAndClearsOnReset()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // A pinned constraint keeps its label without any hover (HoveredConstraintId stays
        // -1), and the pin clears on reset like every other transient view state.
        var view = new SkeletonView();
        view.SetBodies(model);
        view.ShowConstraints = true;
        view.PinConstraintForTest(0);
        Assert.Equal(0, view.PinnedConstraintId);
        Assert.Equal(-1, view.HoveredConstraintId);

        view.Reset();
        Assert.Equal(-1, view.PinnedConstraintId);
        Assert.Equal(-1, view.HoveredConstraintId);
    }

    [Fact]
    public void EngineeringFrameModeClearsWithTheView()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // The engineering view survives a re-load of another ragdoll file (TryLoadRagdoll
        // leaves the mode on), but Reset - the clear path - must switch it off so a new
        // session never draws frames-only behind the user's back.
        var view = new SkeletonView { EngineeringFrames = true };
        view.SetBodies(model);
        Assert.True(view.EngineeringFrames);

        view.Reset();
        Assert.False(view.EngineeringFrames);
        Assert.Equal(0, view.DrawnBodies);
    }

    [Fact]
    public void BodyFrameLabelShowsMeasuredComAndStub()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // Body 1 is the L Thigh: measured COM (-8.4326, -0.5324, 53.2274), shown with the
        // fixture's stub length (fit 60.237 * 0.06) and two-decimal file-unit formatting.
        var thigh = Assert.Single(model!.Bodies, b => b.Id == 1);
        Assert.Equal("COM (-8.43, -0.53, 53.23)  stub 3.61",
                     SkeletonView.BodyFrameLabel(thigh, 3.614246f));

        var root = Assert.Single(model.Bodies, b => b.Id == 0);
        Assert.Equal("COM (0.02, -0.05, 69.19)  stub 3.61",
                     SkeletonView.BodyFrameLabel(root, 3.614246f));

        // Zero is the sentinel for a missing COM; the label says so rather than printing a
        // false origin.
        var bare = new HavokRigidBody { Id = 42 };
        Assert.Equal("no measured COM  stub 3.61", SkeletonView.BodyFrameLabel(bare, 3.614246f));
    }

    [Fact]
    public void FrameTableReadsEveryBodysOriginAndCom()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        Assert.Equal(18, model!.Bodies.Count);
        var rows = model.Bodies.Select(SkeletonView.FrameTableRow).ToList();
        Assert.Equal(model.Bodies.Count, rows.Count);

        Assert.Equal("  0  Ragdoll_NPC COM   frame (-0.00, 0.00, 68.91)  COM (0.02, -0.05, 69.19)",
                     rows[0]);
        Assert.Equal("  1  Ragdoll_NPC L Thigh  frame (-6.62, 0.00, 68.91)  COM (-8.43, -0.53, 53.23)",
                     rows[1]);
        Assert.Equal(" 16  Ragdoll_NPC L Hand  frame (-38.12, 7.71, 83.98)  COM (-39.39, 11.19, 80.34)",
                     rows[16]);

        var bare = new HavokRigidBody { Id = 42 };
        Assert.Equal(" 42  body 42           frame (0.00, 0.00, 0.00)  COM no measured COM",
                     SkeletonView.FrameTableRow(bare));
    }

    [Fact]
    public void BodyFramePinsPersistTogetherAndClear()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // Body frame pins are multi-select: clicking several bodies keeps all their
        // measured COM + stub labels on screen at once, with no hover required.
        var view = new SkeletonView();
        view.SetBodies(model);
        view.ShowFrames = true;
        view.ToggleBodyPinForTest(0);
        view.ToggleBodyPinForTest(1);
        view.ToggleBodyPinForTest(2);
        Assert.Equal(new[] { 0, 1, 2 }, view.PinnedBodyIds.OrderBy(x => x).ToArray());
        Assert.Equal(-1, view.HoveredBodyId);

        // Clicking a pinned body again unpins just that one; the rest stay for comparison.
        view.ToggleBodyPinForTest(1);
        Assert.Equal(new[] { 0, 2 }, view.PinnedBodyIds.OrderBy(x => x).ToArray());

        // The set bookkeeping is independent of the overlays (the real click path is gated
        // by the overlay visibility instead); Reset clears everything like the constraint pin.
        view.ToggleBodyPinForTest(0);
        view.ToggleBodyPinForTest(2);
        Assert.Empty(view.PinnedBodyIds);

        view.ToggleBodyPinForTest(1);
        view.Reset();
        Assert.Empty(view.PinnedBodyIds);
        Assert.False(view.ShowFrames);
    }

    [Fact]
    public void BodyFrameDistanceUsesTwoPickedFramesAndCanBeReplacedOrCleared()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var view = new SkeletonView();
        view.SetBodies(model);
        view.EngineeringFrames = true;
        view.ToggleBodyPinForTest(0);
        Assert.Equal("", view.FrameDistanceText);

        view.ToggleBodyPinForTest(1);
        Assert.Equal((0, 1), view.FrameDistanceBodies);
        var first = model!.Bodies.Single(b => b.Id == 0);
        var second = model.Bodies.Single(b => b.Id == 1);
        float firstDistance = Vector3.Distance(first.Position, second.Position);
        Assert.Equal($"distance {first.Name} to {second.Name}  {firstDistance:0.00} file units",
                     view.FrameDistanceText);

        view.ToggleBodyPinForTest(2);
        Assert.Equal((1, 2), view.FrameDistanceBodies);

        view.ClearFrameDistance();
        Assert.Null(view.FrameDistanceBodies);
        Assert.Equal("", view.FrameDistanceText);
    }

    [Fact]
    public void ConstraintHoverLabelShowsMeasuredLimits()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // Ragdoll constraint #0: twist -0.2618..+0.2618 rad and a 0.5236 rad cone half-angle,
        // reported as degrees exactly as the viewport hover label formats them.
        var ragdoll = Assert.Single(model!.Constraints, c => c.Kind == HavokConstraintKind.Ragdoll && c.Id == 0);
        string ragdollLabel = SkeletonView.ConstraintLabel(ragdoll, model.Bodies);
        Assert.Contains("ragdoll: ", ragdollLabel);
        Assert.Contains(" -> ", ragdollLabel);
        Assert.Contains("twist -15.0..15.0 deg", ragdollLabel);
        Assert.Contains("cone 30.0 deg", ragdollLabel);

        // The limited hinge between bodies 4 and 1: -1.9199..+0.0314 rad.
        var hinge = Assert.Single(model.Constraints, c => c.Kind == HavokConstraintKind.LimitedHinge && c.BodyA == 4);
        string hingeLabel = SkeletonView.ConstraintLabel(hinge, model.Bodies);
        Assert.Contains("limited hinge: ", hingeLabel);
        Assert.Contains("hinge -110.0..1.8 deg", hingeLabel);

        // A kind with no measured limits says so plainly, and unnamed bodies fall back to
        // their ids so the label never reads half-empty.
        var bare = new HavokConstraint
        {
            Id = 99,
            Kind = HavokConstraintKind.Hinge,
            BodyA = 0,
            BodyB = 1,
            MinAngle = 0,
            MaxAngle = 0,
        };
        string bareLabel = SkeletonView.ConstraintLabel(bare, new List<HavokRigidBody>());
        Assert.Contains("no measured limits", bareLabel);
        Assert.Contains("body 0 -> body 1", bareLabel);
    }

    [Fact]
    public void RagdollBodyFitAndFrameStubAreMeasurable()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        // The viewport's FitBodies computation is deterministic over the measured inputs
        // (world hull vertices plus body centres), so a fixture-backed replica pins the
        // same numbers the Playback scale readout reports.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var body in model!.Bodies)
        {
            var shape = model.Shapes.FirstOrDefault(s => s.Id == body.ShapeId);
            if (shape != null)
                foreach (var vertex in shape.Vertices)
                {
                    var world = body.Position + Vector3.Transform(vertex, body.Rotation);
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                }
            min = Vector3.Min(min, body.Position);
            max = Vector3.Max(max, body.Position);
        }

        var size = max - min;
        float fit = Math.Max(0.001f, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 0.5f);
        float stub = Math.Max(0.5f, fit * 0.06f);

        // Pinned to the vanilla fixture: the torso/head spans z 3.75..124.22, so the fitted
        // half-extent is 60.24 file units and the frame triads draw 3.61-unit stubs.
        AssertNear(60.237434f, fit, 2e-2f);
        AssertNear(3.614246f, stub, 2e-3f);
        Assert.Equal(3.614246f, Math.Max(0.5f, fit * 0.06f), 3);
    }

    [Fact]
    public void ValidatorFlagsBrokenAnimationMappings()
    {
        var model = new HavokRagdollModel
        {
            Skeleton = new HavokRagdollSkeleton { Name = "physics" },
            AnimationSkeleton = new HavokRagdollSkeleton { Name = "animation" },
        };
        model.Skeleton!.BoneNames.AddRange(new[] { "A", "B" });
        model.Skeleton.ParentIndices.AddRange(new[] { -1, 0 });
        model.Skeleton.ReferencePose.Add(new HkxBonePose(new Vector3(0, 0, 0), Quaternion.Identity, Vector3.One));
        model.Skeleton.ReferencePose.Add(new HkxBonePose(new Vector3(0, 0, 1), Quaternion.Identity, Vector3.One));
        model.AnimationSkeleton!.BoneNames.AddRange(new[] { "X" });
        model.AnimationSkeleton.ParentIndices.Add(-1);
        model.AnimationSkeleton.ReferencePose.Add(new HkxBonePose(Vector3.Zero, Quaternion.Identity, Vector3.One));

        model.Mappings.Add(new HavokRagdollMapping(7, 0, Vector3.Zero, Quaternion.Identity));

        var findings = HavokPhysicsValidator.Check(model);
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("outside the animation skeleton"));

        // Covering only bone 0 leaves bone 1 unmapped: flagged as a warning, and the bone
        // would keep its reference position rather than silently disappearing.
        Assert.Contains(findings, f =>
            f.Level == HavokPhysicsValidationLevel.Warning && f.Message.Contains("not covered"));

        // A second mapping for the same physics bone is an error (never measured so far).
        model.Mappings.Add(new HavokRagdollMapping(1, 0, Vector3.Zero, Quaternion.Identity));
        var duplicate = HavokPhysicsValidator.Check(model);
        Assert.Contains(duplicate, f =>
            f.Level == HavokPhysicsValidationLevel.Error && f.Message.Contains("mapped more than once"));
    }
}
