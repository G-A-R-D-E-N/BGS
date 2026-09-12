using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenCommonwealth.Services.Hkx;

public sealed record HavokSkeletonMapper(
    int ObjectId,
    int SkeletonAObjectId,
    int SkeletonBObjectId,
    HavokRagdollSkeleton SkeletonA,
    HavokRagdollSkeleton SkeletonB,
    int MappingType,
    IReadOnlyList<HavokRagdollMapping> SimpleMappings);

public static class SkeletonMapperReader
{
    private const string MapperClass = "hkaSkeletonMapper";
    private const string MapperDataClass = "hkaSkeletonMapperData";
    private const string SimpleMappingClass = "hkaSkeletonMapperDataSimpleMapping";
    private const string SkeletonClass = "hkaSkeleton";

    public static IReadOnlyList<HavokSkeletonMapper> Read(PackfileObjects objects)
    {
        ArgumentNullException.ThrowIfNull(objects);

        var result = new List<HavokSkeletonMapper>();
        foreach (var instance in objects.Instances.Where(item => item.ClassName == MapperClass))
            if (ReadMapper(objects, instance) is { } mapper)
                result.Add(mapper);
        return result;
    }

    internal static HavokRagdollSkeleton? ReadSkeleton(
        PackfileObjects objects,
        PackfileObjects.Instance instance)
    {
        if (instance.ClassName != SkeletonClass)
            return null;

        var reader = new MemberReader(objects);
        string name = "";
        if (reader.At(instance.Offset, SkeletonClass, "name") is { } nameAt)
            name = objects.ReadStringAt(nameAt) ?? "";

        var skeleton = new HavokRagdollSkeleton { Name = name };

        if (reader.At(instance.Offset, SkeletonClass, "parentIndices") is { } parentsAt &&
            objects.ArrayAt(parentsAt, 2) is { } parents)
            for (int i = 0; i < parents.Count; i++)
            {
                int? raw = objects.ReadNarrowAt(parents.At + i * 2, 2);
                if (raw == null) return null;
                skeleton.ParentIndices.Add(unchecked((short)raw.Value));
            }

        if (reader.At(instance.Offset, SkeletonClass, "bones") is { } bonesAt &&
            objects.ArrayAt(bonesAt, 16) is { } bones)
            for (int i = 0; i < bones.Count; i++)
            {
                string? boneName = objects.ReadStringAt(bones.At + i * 16);
                if (boneName == null) return null;
                skeleton.BoneNames.Add(boneName);
            }

        if (skeleton.BoneNames.Count == 0)
            return null;

        if (reader.At(instance.Offset, SkeletonClass, "referencePose") is { } poseAt &&
            objects.ArrayAt(poseAt, 48) is { } poses)
            for (int i = 0; i < poses.Count; i++)
            {
                float[]? row = objects.ReadFloatsAt(poses.At + i * 48, 12);
                if (row == null) return null;
                skeleton.ReferencePose.Add(new HkxBonePose(
                    new Vector3(row[0], row[1], row[2]),
                    new Quaternion(row[4], row[5], row[6], row[7]),
                    new Vector3(row[8], row[9], row[10])));
            }

        return skeleton;
    }

    private static HavokSkeletonMapper? ReadMapper(
        PackfileObjects objects,
        PackfileObjects.Instance instance)
    {
        var mapperLayout = LayoutWalker.Active(HavokClassTypes.Shipped, MapperClass, objects.PointerWidth);
        int? mappingAt = mapperLayout?.OffsetOf("mapping") is { } mappingOffset
            ? instance.Offset + mappingOffset
            : null;
        if (mappingAt == null)
            return null;

        var dataLayout = LayoutWalker.Active(HavokClassTypes.Shipped, MapperDataClass, objects.PointerWidth);
        if (dataLayout == null ||
            dataLayout.OffsetOf("skeletonA") is not { } skeletonAOffset ||
            dataLayout.OffsetOf("skeletonB") is not { } skeletonBOffset ||
            dataLayout.OffsetOf("simpleMappings") is not { } rowsOffset ||
            dataLayout.OffsetOf("mappingType") is not { } typeOffset)
            return null;

        var skeletonAInstance = objects.ReadRefAt(mappingAt.Value + skeletonAOffset, out _);
        var skeletonBInstance = objects.ReadRefAt(mappingAt.Value + skeletonBOffset, out _);
        if (skeletonAInstance?.ClassName != SkeletonClass || skeletonBInstance?.ClassName != SkeletonClass)
            return null;

        var skeletonA = ReadSkeleton(objects, skeletonAInstance);
        var skeletonB = ReadSkeleton(objects, skeletonBInstance);
        int? mappingType = objects.ReadNarrowAt(mappingAt.Value + typeOffset, 4);
        if (skeletonA == null || skeletonB == null || mappingType == null)
            return null;

        var simpleLayout = LayoutWalker.Active(HavokClassTypes.Shipped, SimpleMappingClass, objects.PointerWidth);
        if (simpleLayout == null || simpleLayout.Size <= 0 ||
            simpleLayout.OffsetOf("boneA") is not { } boneAOffset ||
            simpleLayout.OffsetOf("boneB") is not { } boneBOffset ||
            simpleLayout.OffsetOf("aFromBTransform") is not { } transformOffset)
            return null;

        var rows = objects.ArrayAt(mappingAt.Value + rowsOffset, simpleLayout.Size);
        if (rows == null)
            return null;

        var mappings = new List<HavokRagdollMapping>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            int row = rows.At + i * simpleLayout.Size;
            int? boneA = objects.ReadNarrowAt(row + boneAOffset, 2);
            int? boneB = objects.ReadNarrowAt(row + boneBOffset, 2);
            float[]? values = objects.ReadFloatsAt(row + transformOffset, 12);
            if (boneA == null || boneB == null || values == null)
                return null;

            var translation = new Vector3(values[0], values[1], values[2]);
            var rotation = new Quaternion(values[4], values[5], values[6], values[7]);
            if (!Finite(translation) || !Finite(rotation) || rotation.LengthSquared() < 1e-10f)
                return null;

            mappings.Add(new HavokRagdollMapping(
                unchecked((short)boneA.Value),
                unchecked((short)boneB.Value),
                translation,
                Quaternion.Normalize(rotation)));
        }

        return new HavokSkeletonMapper(
            NativeGraphModel.FirstId + objects.IndexOf(instance),
            NativeGraphModel.FirstId + objects.IndexOf(skeletonAInstance),
            NativeGraphModel.FirstId + objects.IndexOf(skeletonBInstance),
            skeletonA,
            skeletonB,
            mappingType.Value,
            mappings);
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private sealed class MemberReader
    {
        public MemberReader(PackfileObjects objects) => Objects = objects;

        private PackfileObjects Objects { get; }

        public int? At(int baseAt, string className, string member) =>
            LayoutWalker.Active(HavokClassTypes.Shipped, className, Objects.PointerWidth)?.OffsetOf(member) is { } offset
                ? baseAt + offset
                : null;
    }
}
