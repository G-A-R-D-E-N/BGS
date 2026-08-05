using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace OpenCommonwealth.Services.Nif;

// One drawable piece of a mesh, with the weights that tie it to a skeleton.
//
// Positions are in the mesh's own space and unconverted, the same as everything the Havok readers
// hand out. Whatever draws it owns the fit to screen.
public sealed class NifShape
{
    public string Name = "";
    public readonly List<Vector3> Vertices = new();

    /// Triangle corners as indices into Vertices, three per triangle.
    public readonly List<int> Indices = new();

    /// Four bone slots per vertex, flattened. A weight of zero means the slot is unused; the game's
    /// own meshes leave the unused slots pointing at bone 0 rather than at anything meaningful, so
    /// the weight is what decides, never the index.
    public readonly List<int> BoneIndices = new();
    public readonly List<float> BoneWeights = new();

    /// The bones this shape is weighted to, in the order its indices address them. These are the
    /// mesh's names for them, which is what has to be matched against the skeleton.
    public readonly List<string> BoneNames = new();

    /// Skin to bone, one per entry in BoneNames. A vertex sits in skin space, so it has to be taken
    /// into each bone's space before that bone's animated transform means anything.
    public readonly List<Matrix4x4> SkinToBone = new();

    public bool IsSkinned => BoneNames.Count > 0 && BoneWeights.Count == Vertices.Count * 4;

    public int TriangleCount => Indices.Count / 3;

    public override string ToString() =>
        $"{Name}: {Vertices.Count} vertices, {TriangleCount} triangles" +
        (IsSkinned ? $", weighted to {BoneNames.Count} bones" : ", not skinned");
}

// Reads the shapes out of a NIF. Only what a wireframe needs: where the vertices are, which ones
// join up, and how they follow the skeleton.
public static class NifGeometry
{
    // The attribute bits in a BSVertexDesc's top twenty. Read off the game's files rather than
    // recalled: Dogmeat's shapes declare 0x5b, which is vertex, uv, normal, tangent and skinned, and
    // walking those in this order produces a stride of exactly 32, which is the stride the file's own
    // dataSize arithmetic requires.
    private const int HasVertex = 1 << 0;
    private const int HasUv = 1 << 1;
    private const int HasUv2 = 1 << 2;
    private const int HasNormal = 1 << 3;
    private const int HasTangent = 1 << 4;
    private const int HasColour = 1 << 5;
    private const int HasSkin = 1 << 6;
    private const int HasLandData = 1 << 7;
    private const int HasEyeData = 1 << 8;
    private const int FullPrecision = 1 << 10;

    public static List<NifShape> Shapes(NifFile nif)
    {
        var found = new List<NifShape>();
        foreach (int block in nif.BlocksOfType("BSTriShape", "BSSubIndexTriShape", "BSMeshLODTriShape"))
        {
            var shape = ReadShape(nif, block);
            if (shape != null) found.Add(shape);
        }
        return found;
    }

    private static NifShape? ReadShape(NifFile nif, int block)
    {
        byte[] d = nif.Data;
        int at = nif.BlockStart[block];

        int nameIndex = (int)NifFile.U32(d, ref at);
        int extras = (int)NifFile.U32(d, ref at);
        at += 4 * extras;
        at += 4;                    // controller
        at += 4;                    // flags
        at += 12 + 36 + 4;          // translation, 3x3 rotation, scale
        at += 4;                    // collision object
        at += 16;                   // bounding sphere

        int skin = NifFile.I32(d, ref at);
        at += 4;                    // shader property
        at += 4;                    // alpha property

        ulong desc = NifFile.U64(d, ref at);
        int triangles = (int)NifFile.U32(d, ref at);
        int vertices = NifFile.U16(d, ref at);
        int dataSize = (int)NifFile.U32(d, ref at);

        int stride = (int)(desc & 0xF) * 4;
        int flags = (int)(desc >> 44);

        // A shape whose own numbers disagree is not one to guess at: every byte read after this point
        // would be at the wrong place, and a wrong mesh drawn confidently is worse than none.
        if (dataSize == 0 || vertices == 0 || stride == 0) return null;
        if (dataSize != vertices * stride + triangles * 6)
            throw new InvalidDataException(
                $"{nif.Strings[nameIndex]}: dataSize is {dataSize} but {vertices} vertices at a stride of " +
                $"{stride} plus {triangles} triangles needs {vertices * stride + triangles * 6}");

        var shape = new NifShape { Name = nameIndex < nif.Strings.Count ? nif.Strings[nameIndex] : "" };
        var layout = Layout(flags, stride);

        for (int v = 0; v < vertices; v++)
        {
            int o = at + v * stride;

            shape.Vertices.Add((flags & FullPrecision) != 0
                ? new Vector3(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4),
                              BitConverter.ToSingle(d, o + 8))
                : new Vector3(NifFile.Half(d, o), NifFile.Half(d, o + 2), NifFile.Half(d, o + 4)));

            if (layout.Weights < 0) continue;
            for (int s = 0; s < 4; s++)
            {
                shape.BoneWeights.Add(NifFile.Half(d, o + layout.Weights + s * 2));
                shape.BoneIndices.Add(d[o + layout.Indices + s]);
            }
        }

        at += vertices * stride;
        for (int t = 0; t < triangles * 3; t++)
            shape.Indices.Add(BitConverter.ToUInt16(d, at + t * 2));

        if (skin >= 0) ReadSkin(nif, skin, shape);
        return shape;
    }

    // Where the weights and the bone indices sit inside one vertex, worked out by walking the
    // attributes the descriptor declares rather than assuming one layout. The walk is checked against
    // the stride the same descriptor states, so a file laid out differently refuses instead of
    // reading somebody else's bytes as bone indices.
    private static (int Weights, int Indices) Layout(int flags, int stride)
    {
        if ((flags & HasSkin) == 0) return (-1, -1);

        int o = (flags & FullPrecision) != 0 ? 12 : 8;   // position, plus the half that pads it
        if ((flags & HasUv) != 0) o += 4;
        if ((flags & HasUv2) != 0) o += 4;
        if ((flags & HasNormal) != 0) o += 4;
        if ((flags & HasTangent) != 0) o += 4;
        if ((flags & HasColour) != 0) o += 4;

        int weights = o;
        int indices = o + 8;
        if (indices + 4 > stride)
            throw new InvalidDataException(
                $"a skinned vertex of {stride} bytes has no room for weights at {weights}; " +
                $"the vertex descriptor declares attributes this does not lay out");

        return (weights, indices);
    }

    private static void ReadSkin(NifFile nif, int block, NifShape shape)
    {
        if (nif.BlockType[block] != "BSSkin::Instance") return;

        byte[] d = nif.Data;
        int at = nif.BlockStart[block];
        at += 4;                                        // skeleton root
        int data = NifFile.I32(d, ref at);
        int bones = (int)NifFile.U32(d, ref at);

        for (int b = 0; b < bones; b++)
            shape.BoneNames.Add(nif.NameOf(NifFile.I32(d, ref at)));

        if (data < 0 || data >= nif.BlockCount || nif.BlockType[data] != "BSSkin::BoneData") return;

        int bd = nif.BlockStart[data];
        int count = (int)NifFile.U32(d, ref bd);
        if (count != bones) return;

        for (int b = 0; b < count; b++)
        {
            bd += 16;                                   // this bone's bounding sphere
            var m = new Matrix4x4(
                NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), 0,
                NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), 0,
                NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), 0,
                0, 0, 0, 1);
            var t = new Vector3(NifFile.F32(d, ref bd), NifFile.F32(d, ref bd), NifFile.F32(d, ref bd));
            float scale = NifFile.F32(d, ref bd);

            m.M11 *= scale; m.M12 *= scale; m.M13 *= scale;
            m.M21 *= scale; m.M22 *= scale; m.M23 *= scale;
            m.M31 *= scale; m.M32 *= scale; m.M33 *= scale;
            m.M41 = t.X; m.M42 = t.Y; m.M43 = t.Z;
            shape.SkinToBone.Add(m);
        }
    }
}
