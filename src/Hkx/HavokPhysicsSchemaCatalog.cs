using System;
using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

public enum HavokPhysicsClassFamily
{
    Cloth,
    NewPhysics,
    LegacyPhysics,
}

public static class HavokPhysicsSchemaCatalog
{
    private static readonly IReadOnlyDictionary<string, uint> Signatures =
        new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["hclBendLinkConstraintSet"] = 0x26824757u,
        ["hclBendStiffnessConstraintSet"] = 0x11315f09u,
        ["hclBoneSpaceDeformer"] = 0x694fac94u,
        ["hclBoneSpaceMeshMeshDeformPNOperator"] = 0x2a74c429u,
        ["hclBoneSpaceMeshMeshDeformPNTOperator"] = 0x3d533d8bu,
        ["hclBoneSpaceMeshMeshDeformPOperator"] = 0xc9311cbdu,
        ["hclBoneSpaceSkinPNOperator"] = 0x7dd2c2a1u,
        ["hclBoneSpaceSkinPNTOperator"] = 0x7dd2c2a1u,
        ["hclBufferDefinition"] = 0x7f4a5bfcu,
        ["hclBufferLayout"] = 0xd26825a7u,
        ["hclBufferLayoutBufferElement"] = 0x3e0b1ef4u,
        ["hclBufferLayoutSlot"] = 0xc485fa70u,
        ["hclBufferUsage"] = 0xf02067bau,
        ["hclCapsuleShape"] = 0xdd03f524u,
        ["hclClothContainer"] = 0x3512912bu,
        ["hclClothData"] = 0xf943cea2u,
        ["hclClothState"] = 0x7b02cd1bu,
        ["hclCollidable"] = 0xf14068beu,
        ["hclCompressibleLinkConstraintSet"] = 0x51e7d475u,
        ["hclConvexGeometryShape"] = 0x8e10aa76u,
        ["hclCopyVerticesOperator"] = 0xe6db074cu,
        ["hclGatherAllVerticesOperator"] = 0xda737296u,
        ["hclGatherSomeVerticesOperator"] = 0x85597cf0u,
        ["hclInputConvertOperator"] = 0xed50bb9fu,
        ["hclLocalRangeConstraintSet"] = 0x82567805u,
        ["hclMeshMeshDeformOperator"] = 0xff4f25c3u,
        ["hclMoveParticlesOperator"] = 0xe65a701cu,
        ["hclObjectSpaceDeformer"] = 0x342813e1u,
        ["hclObjectSpaceMeshMeshDeformPNOperator"] = 0x16bdb41au,
        ["hclObjectSpaceMeshMeshDeformPNTBOperator"] = 0x83e96a2fu,
        ["hclObjectSpaceMeshMeshDeformPNTOperator"] = 0xe20c4d3eu,
        ["hclObjectSpaceMeshMeshDeformPOperator"] = 0x574824cfu,
        ["hclObjectSpaceSkinPNOperator"] = 0x84488b96u,
        ["hclObjectSpaceSkinPNTOperator"] = 0xb6099e3fu,
        ["hclObjectSpaceSkinPOperator"] = 0xb5705b89u,
        ["hclOutputConvertOperator"] = 0xed50bb9fu,
        ["hclPlaneShape"] = 0x4d60b010u,
        ["hclRuntimeConversionInfo"] = 0xe36985b8u,
        ["hclRuntimeConversionInfoElementConversion"] = 0xa19a443au,
        ["hclRuntimeConversionInfoSlotConversion"] = 0xd8db9c79u,
        ["hclScratchBufferDefinition"] = 0xa0130a2cu,
        ["hclShadowBufferDefinition"] = 0x4b5133e0u,
        ["hclSimClothData"] = 0xe6105187u,
        ["hclSimClothDataCollidableTransformMap"] = 0x8addbbbbu,
        ["hclSimClothDataLandscapeCollisionData"] = 0x1d595840u,
        ["hclSimClothDataOverridableSimulationInfo"] = 0x383b984fu,
        ["hclSimClothDataTransferMotionData"] = 0x0d49be55u,
        ["hclSimClothPose"] = 0x1b254ca1u,
        ["hclSimpleMeshBoneDeformOperator"] = 0x80d9769fu,
        ["hclSimulateOperator"] = 0x75c72f0fu,
        ["hclSphereShape"] = 0xd779f2c5u,
        ["hclStandardLinkConstraintSet"] = 0x426b3354u,
        ["hclStretchLinkConstraintSet"] = 0x426b3354u,
        ["hclTaperedCapsuleShape"] = 0xdfa58f21u,
        ["hclTransformSetDefinition"] = 0x18fd4565u,
        ["hclTransformSetUsage"] = 0xbf809381u,
        ["hclTransitionConstraintSet"] = 0x9cfe2c7du,
        ["hclUpdateAllVertexFramesOperator"] = 0x9f7b4db7u,
        ["hclUpdateSomeVertexFramesOperator"] = 0x9aadaeecu,
        ["hclVolumeConstraint"] = 0x5478425eu,
        ["hclVolumeConstraintMx"] = 0x037582f2u,
        ["hknpBreakableConstraintData"] = 0xc40485c7u,
        ["hknpCapsuleShape"] = 0x60a75f4cu,
        ["hknpCompressedMeshShape"] = 0x5f60d536u,
        ["hknpCompressedMeshShapeData"] = 0xa2bdfc59u,
        ["hknpCompressedMeshShapeTree"] = 0xed062659u,
        ["hknpCompressedMeshShapeTreeDataRunData"] = 0xc253682bu,
        ["hknpConvexPolytopeShape"] = 0x3ce9b3e3u,
        ["hknpPhysicsSceneData"] = 0x701ce72cu,
        ["hknpPhysicsSystemData"] = 0xb857718bu,
        ["hknpRagdollData"] = 0xdc8f20abu,
        ["hknpShapeMassProperties"] = 0xe9191728u,
        ["hknpSparseCompactMapunsignedshort"] = 0x4558127cu,
        ["hknpSphereShape"] = 0x741e9012u,
        ["hkp2dAngConstraintAtom"] = 0xd277c114u,
        ["hkpAngFrictionConstraintAtom"] = 0x89f70523u,
        ["hkpAngLimitConstraintAtom"] = 0x01c5a0ddu,
        ["hkpAngMotorConstraintAtom"] = 0x42498456u,
        ["hkpBallAndSocketConstraintData"] = 0xd093f6ecu,
        ["hkpBallAndSocketConstraintDataAtoms"] = 0xe51e4bccu,
        ["hkpBallSocketConstraintAtom"] = 0x6ba88f7au,
        ["hkpConeLimitConstraintAtom"] = 0x159ea5c9u,
        ["hkpHingeConstraintData"] = 0x901a5ffau,
        ["hkpHingeConstraintDataAtoms"] = 0x1f6f4807u,
        ["hkpLimitedHingeConstraintData"] = 0x51ea603au,
        ["hkpLimitedHingeConstraintDataAtoms"] = 0x28231532u,
        ["hkpPositionConstraintMotor"] = 0x143dd400u,
        ["hkpRagdollConstraintData"] = 0xb77d2036u,
        ["hkpRagdollConstraintDataAtoms"] = 0xe11fb3acu,
        ["hkpRagdollMotorConstraintAtom"] = 0x9d94d42cu,
        ["hkpSetLocalTransformsConstraintAtom"] = 0x13cd1821u,
        ["hkpSetLocalTranslationsConstraintAtom"] = 0x3d4c316au,
        ["hkpSetupStabilizationAtom"] = 0x870ee10au,
        ["hkpTwistLimitConstraintAtom"] = 0xda910271u,
    };

    public static int Count => Signatures.Count;
    public static IEnumerable<string> Names => Signatures.Keys;

    public static bool TryGetSignature(string className, out uint signature) =>
        Signatures.TryGetValue(className, out signature);

    public static bool Matches(string className, uint signature) =>
        Signatures.TryGetValue(className, out uint expected) && expected == signature;

    public static HavokPhysicsClassFamily? FamilyOf(string className)
    {
        if (className.StartsWith("hcl", StringComparison.Ordinal))
            return HavokPhysicsClassFamily.Cloth;
        if (className.StartsWith("hknp", StringComparison.Ordinal))
            return HavokPhysicsClassFamily.NewPhysics;
        if (className.StartsWith("hkp", StringComparison.Ordinal))
            return HavokPhysicsClassFamily.LegacyPhysics;
        return null;
    }
}
