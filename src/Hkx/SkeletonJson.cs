using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace OpenCommonwealth.Services.Hkx;












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


    public static List<int> ChildCounts(HkxSkeleton skeleton)
    {
        var counts = new List<int>(new int[skeleton.BoneNames.Count]);
        foreach (int parent in skeleton.ParentIndices)
            if (parent >= 0 && parent < counts.Count) counts[parent]++;
        return counts;
    }
}
