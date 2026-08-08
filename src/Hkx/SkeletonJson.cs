using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;

// The skeleton as the file states it, in a form something outside .NET can read.
//
// Nothing is converted. Translations are raw Havok units, not metres and not Blender units, and the
// axes are the game's. Whatever imports this owns the unit and axis change, because that choice
// belongs with the tool that knows what it is importing into, and burying it here would leave every
// consumer guessing whether it had already happened.
//
// Transforms are parent relative, verified rather than assumed: in the vanilla 95 bone character
// skeleton every arm bone stores a pure X offset and the collarbone's (19.153, -0.510, 1.695)
// composes to (-1.695, -0.628, 110.409), so the parent's rotation is turning an along-the-bone
// offset into height. World positions are the consumer's to compose, as local * parentWorld.
public static class SkeletonJson
{
    public const string Format = "fo4-skeleton";
    public const int Version = 1;

    public const string UnitsNote =
        "raw Havok units and game axes, unconverted; the importer owns any unit or axis change";

    public static string Write(HkxSkeleton skeleton, string sourcePath)
    {
        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", Version);
            w.WriteString("units", UnitsNote);
            w.WriteString("source", System.IO.Path.GetFileName(sourcePath));
            w.WriteString("name", skeleton.Name);
            w.WriteNumber("boneCount", skeleton.BoneNames.Count);

            w.WriteStartArray("bones");
            for (int i = 0; i < skeleton.BoneNames.Count; i++)
            {
                var pose = i < skeleton.ReferencePose.Count ? skeleton.ReferencePose[i] : new HkxBonePose();

                w.WriteStartObject();
                w.WriteString("name", skeleton.BoneNames[i]);
                w.WriteNumber("parent", i < skeleton.ParentIndices.Count ? skeleton.ParentIndices[i] : -1);
                WriteVector(w, "translation", pose.Translation);
                WriteQuaternion(w, "rotation", pose.Rotation);
                WriteVector(w, "scale", pose.Scale);
                if (i < skeleton.LockTranslation.Count && skeleton.LockTranslation[i])
                    w.WriteBoolean("lockTranslation", true);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (skeleton.FloatSlots.Count > 0)
            {
                w.WriteStartArray("floatSlots");
                foreach (string slot in skeleton.FloatSlots) w.WriteStringValue(slot);
                w.WriteEndArray();
            }

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // Reads back what Write produced, so the emitter can be checked against the reader it came from
    // rather than trusted.
    public static HkxSkeleton Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string format = root.GetProperty("format").GetString() ?? "";
        if (format != Format) throw new InvalidOperationException($"not a {Format} document: '{format}'");

        var skeleton = new HkxSkeleton { Name = root.GetProperty("name").GetString() ?? "" };
        foreach (var bone in root.GetProperty("bones").EnumerateArray())
        {
            skeleton.BoneNames.Add(bone.GetProperty("name").GetString() ?? "");
            skeleton.ParentIndices.Add(bone.GetProperty("parent").GetInt32());
            skeleton.ReferencePose.Add(new HkxBonePose(
                ReadVector(bone, "translation"),
                ReadQuaternion(bone, "rotation"),
                ReadVector(bone, "scale")));
        }
        return skeleton;
    }

    private static void WriteVector(Utf8JsonWriter w, string name, Vector3 v)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(v.X); w.WriteNumberValue(v.Y); w.WriteNumberValue(v.Z);
        w.WriteEndArray();
    }

    private static void WriteQuaternion(Utf8JsonWriter w, string name, Quaternion q)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(q.X); w.WriteNumberValue(q.Y); w.WriteNumberValue(q.Z); w.WriteNumberValue(q.W);
        w.WriteEndArray();
    }

    private static Vector3 ReadVector(JsonElement bone, string name)
    {
        var a = bone.GetProperty(name);
        return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
    }

    private static Quaternion ReadQuaternion(JsonElement bone, string name)
    {
        var a = bone.GetProperty(name);
        return new Quaternion(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle());
    }

    /// How many children each bone has, which is what a head and tail convention has to cope with.
    public static List<int> ChildCounts(HkxSkeleton skeleton)
    {
        var counts = new List<int>(new int[skeleton.BoneNames.Count]);
        foreach (int parent in skeleton.ParentIndices)
            if (parent >= 0 && parent < counts.Count) counts[parent]++;
        return counts;
    }
}
