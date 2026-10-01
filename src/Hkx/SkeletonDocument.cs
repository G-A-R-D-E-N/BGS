using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace OpenCommonwealth.Services.Hkx;

public sealed class SkeletonDocument
{
    private byte[] _savedBytes;
    private string _savedXml;
    private string _xml;
    private DocumentSourceStamp _stamp;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public SkeletonDocument(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _savedBytes = InputFilePolicy.ReadHkx(Path);
        _savedXml = _xml = NativeXml.From(_savedBytes);
        _stamp = DocumentSourceStamp.Capture(_savedBytes);
        if (Skeletons.Count == 0) throw new InvalidDataException("This file contains no skeleton.");
    }

    public string Path { get; }
    public bool Dirty => _xml != _savedXml;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool CanChangeBoneCount => Standalone(XDocument.Parse(_xml));
    public IReadOnlyList<HkxSkeleton> Skeletons => Objects(XDocument.Parse(_xml)).Select(Read).ToArray();

    public void Edit(int skeletonIndex, Action<SkeletonEdit> edit)
    {
        var document = XDocument.Parse(_xml);
        XElement element = Objects(document).ElementAt(skeletonIndex);
        HkxSkeleton model = Read(element);
        string[] names = model.BoneNames.ToArray();
        float[] originalPose = Numbers(Param(element, "referencePose").Value);
        edit(new SkeletonEdit(model));
        var errors = SkeletonValidator.Check(model).Where(f => f.Level == SkeletonValidator.Level.Error).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(errors[0].ToString());
        bool reordered = names.Length != model.BoneNames.Count || names.Where((name, i) =>
            model.BoneNames.IndexOf(name) is int index && index >= 0 && index != i).Any();
        if (reordered && !Standalone(document))
            throw new NotSupportedException("Changing bone order or count requires remapping dependent assets; this file editor preserves bone indices.");

        Param(element, "name").Value = model.Name;
        XElement slots = Param(element, "floatSlots");
        slots.ReplaceNodes(model.FloatSlots.Select(value => new XElement("hkcstring", value)));
        slots.SetAttributeValue("numelements", model.FloatSlots.Count);
        XElement floats = Param(element, "referenceFloats");
        floats.Value = string.Join(" ", model.ReferenceFloats.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        floats.SetAttributeValue("numelements", model.ReferenceFloats.Count);

        XElement boneArray = Param(element, "bones");
        var originalBones = boneArray.Elements("hkobject").ToArray();
        var bones = new List<XElement>();
        for (int i = 0; i < model.BoneNames.Count; i++)
        {
            int old = reordered ? Array.IndexOf(names, model.BoneNames[i]) : i;
            XElement bone = old >= 0 ? new XElement(originalBones[old]) : new XElement("hkobject",
                new XElement("hkparam", new XAttribute("name", "name"), ""),
                new XElement("hkparam", new XAttribute("name", "lockTranslation"), "false"));
            Param(bone, "name").Value = model.BoneNames[i];
            Param(bone, "lockTranslation").Value = model.LockTranslation[i] ? "true" : "false";
            bones.Add(bone);
        }
        boneArray.ReplaceNodes(bones);
        boneArray.SetAttributeValue("numelements", bones.Count);
        Param(element, "parentIndices").Value = string.Join(" ", model.ParentIndices);
        Param(element, "parentIndices").SetAttributeValue("numelements", model.ParentIndices.Count);
        XElement pose = Param(element, "referencePose");
        float[] values = new float[model.ReferencePose.Count * 12];
        for (int i = 0; i < model.ReferencePose.Count; i++)
        {
            HkxBonePose p = model.ReferencePose[i];
            int at = i * 12;
            int old = reordered ? Array.IndexOf(names, model.BoneNames[i]) : i;
            if (old >= 0) Array.Copy(originalPose, old * 12, values, at, 12);
            values[at] = p.Translation.X; values[at + 1] = p.Translation.Y; values[at + 2] = p.Translation.Z;
            values[at + 4] = p.Rotation.X; values[at + 5] = p.Rotation.Y;
            values[at + 6] = p.Rotation.Z; values[at + 7] = p.Rotation.W;
            values[at + 8] = p.Scale.X; values[at + 9] = p.Scale.Y; values[at + 10] = p.Scale.Z;
        }
        pose.Value = string.Join(" ", Enumerable.Range(0, values.Length / 4).Select(i =>
            "(" + string.Join(" ", values.Skip(i * 4).Take(4).Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + ")"));
        pose.SetAttributeValue("numelements", model.ReferencePose.Count);
        string changed = document.ToString();
        NativeSave.Plan plan = NativeSave.Compare(_savedXml, changed);
        if (!plan.Possible) throw new NotSupportedException(plan.Refusal);
        byte[] rebuilt = NativeSave.Apply(_savedBytes, plan);
        SaveVerifier.Verify(_savedBytes, rebuilt, plan);
        if (changed == _xml || plan.Empty && !Dirty) return;
        _undo.Push(_xml);
        _redo.Clear();
        _xml = changed;
    }

    public void Undo()
    {
        if (!_undo.TryPop(out string? previous)) return;
        _redo.Push(_xml);
        _xml = previous;
    }

    public void Redo()
    {
        if (!_redo.TryPop(out string? next)) return;
        _undo.Push(_xml);
        _xml = next;
    }

    public void Save()
    {
        if (!Dirty) return;
        var result = DocumentSaveTransaction.Commit(Path, _savedXml, _xml, _stamp);
        if (!result.Committed && !result.Unchanged) throw new IOException(result.Message);
        _savedBytes = InputFilePolicy.ReadHkx(Path);
        _stamp = DocumentSourceStamp.Capture(_savedBytes);
        _savedXml = _xml = NativeXml.From(_savedBytes);
        _undo.Clear();
        _redo.Clear();
    }

    private static IEnumerable<XElement> Objects(XDocument xml) => xml.Descendants("hkobject")
        .Where(e => (string?)e.Attribute("class") == "hkaSkeleton");

    private static bool Standalone(XDocument xml)
    {
        if (Objects(xml).Count() != 1) return false;
        foreach (XElement item in xml.Descendants("hkobject").Where(e => e.Attribute("class") != null))
        {
            string? type = (string?)item.Attribute("class");
            if (type is not ("hkaSkeleton" or "hkaAnimationContainer" or "hkRootLevelContainer")) return false;
            foreach (XElement array in item.Elements("hkparam").Where(e => e.Attribute("numelements") != null))
            {
                string? name = (string?)array.Attribute("name");
                bool owned = type == "hkaSkeleton" && name is "bones" or "parentIndices" or "referencePose" or "floatSlots" or "referenceFloats" ||
                    type == "hkaAnimationContainer" && name == "skeletons" || type == "hkRootLevelContainer" && name == "namedVariants";
                if (!owned && (string?)array.Attribute("numelements") != "0") return false;
            }
        }
        return true;
    }

    private static XElement Param(XElement element, string name) => element.Elements("hkparam")
        .Single(e => (string?)e.Attribute("name") == name);

    private static float[] Numbers(string value) => value.Split(new[] { ' ', '\r', '\n', '\t', '(', ')' },
        StringSplitOptions.RemoveEmptyEntries).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray();

    private static HkxSkeleton Read(XElement element)
    {
        var model = new HkxSkeleton { Name = Param(element, "name").Value };
        foreach (XElement bone in Param(element, "bones").Elements("hkobject"))
        {
            model.BoneNames.Add(Param(bone, "name").Value);
            model.LockTranslation.Add(bool.Parse(Param(bone, "lockTranslation").Value));
        }
        model.ParentIndices.AddRange(Param(element, "parentIndices").Value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v, CultureInfo.InvariantCulture)));
        float[] values = Numbers(Param(element, "referencePose").Value);
        if (values.Length != model.BoneNames.Count * 12) throw new InvalidDataException("Reference pose count differs from the bones.");
        for (int i = 0; i < values.Length; i += 12)
            model.ReferencePose.Add(new HkxBonePose(new Vector3(values[i], values[i + 1], values[i + 2]),
                new Quaternion(values[i + 4], values[i + 5], values[i + 6], values[i + 7]),
                new Vector3(values[i + 8], values[i + 9], values[i + 10])));
        var floats = element.Elements("hkparam").FirstOrDefault(e => (string?)e.Attribute("name") == "referenceFloats");
        if (floats != null) model.ReferenceFloats.AddRange(Numbers(floats.Value));
        var slots = element.Elements("hkparam").FirstOrDefault(e => (string?)e.Attribute("name") == "floatSlots");
        if (slots != null) model.FloatSlots.AddRange(slots.Elements("hkcstring").Select(e => e.Value));
        var errors = SkeletonValidator.Check(model).Where(f => f.Level == SkeletonValidator.Level.Error).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(errors[0].ToString());
        return model;
    }
}
