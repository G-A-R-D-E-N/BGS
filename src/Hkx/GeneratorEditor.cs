using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Creating and deleting generator nodes: clips, blenders, modifier wrappers and selectors.
// Signatures and field sets are copied out of vanilla files rather than guessed, because hkxpack
// validates both and a wrong signature is rejected on repack.
public static class GeneratorEditor
{
    public sealed class Kind
    {
        public string Class = "";
        public string Signature = "";
        public string Body = "";
    }

    // hkbClipGenerator            from PipboyBehavior.hkx #98
    // hkbBlenderGenerator         from PipboyBehavior.hkx #96
    // hkbBlenderGeneratorChild    from PipboyBehavior.hkx
    // hkbModifierGenerator        from WeaponBehavior.hkx #159
    // hkbManualSelectorGenerator  from WeaponBehavior.hkx #2407
    public static readonly Dictionary<string, Kind> Kinds = new()
    {
        ["clip"] = new Kind
        {
            Class = "hkbClipGenerator",
            Signature = "0xd4cc9f6",
            Body =
                "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
                "            <hkparam name=\"userData\">0</hkparam>\n" +
                "            <hkparam name=\"name\">{name}</hkparam>\n" +
                "            <hkparam name=\"animationBundleName\"/>\n" +
                "            <hkparam name=\"animationName\">{animation}</hkparam>\n" +
                "            <hkparam name=\"triggers\">null</hkparam>\n" +
                "            <hkparam name=\"userPartitionMask\">0</hkparam>\n" +
                "            <hkparam name=\"cropStartAmountLocalTime\">0.0</hkparam>\n" +
                "            <hkparam name=\"cropEndAmountLocalTime\">0.0</hkparam>\n" +
                "            <hkparam name=\"startTime\">0.0</hkparam>\n" +
                "            <hkparam name=\"playbackSpeed\">1.0</hkparam>\n" +
                "            <hkparam name=\"enforcedDuration\">0.0</hkparam>\n" +
                "            <hkparam name=\"userControlledTimeFraction\">0.0</hkparam>\n" +
                "            <hkparam name=\"animationBindingIndex\">65535</hkparam>\n" +
                "            <hkparam name=\"mode\">MODE_LOOPING</hkparam>\n" +
                "            <hkparam name=\"flags\">0</hkparam>",
        },
        ["blender"] = new Kind
        {
            Class = "hkbBlenderGenerator",
            Signature = "0xce45c088",
            Body =
                "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
                "            <hkparam name=\"userData\">0</hkparam>\n" +
                "            <hkparam name=\"name\">{name}</hkparam>\n" +
                "            <hkparam name=\"referencePoseWeightThreshold\">0.0</hkparam>\n" +
                "            <hkparam name=\"blendParameter\">1.0</hkparam>\n" +
                "            <hkparam name=\"minCyclicBlendParameter\">0.0</hkparam>\n" +
                "            <hkparam name=\"maxCyclicBlendParameter\">1.0</hkparam>\n" +
                "            <hkparam name=\"indexOfSyncMasterChild\">65535</hkparam>\n" +
                "            <hkparam name=\"flags\">8</hkparam>\n" +
                "            <hkparam name=\"subtractLastChild\">false</hkparam>\n" +
                "            <hkparam name=\"children\" numelements=\"0\">\n</hkparam>",
        },
        ["modifier"] = new Kind
        {
            Class = "hkbModifierGenerator",
            Signature = "0xc499fc9e",
            Body =
                "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
                "            <hkparam name=\"userData\">0</hkparam>\n" +
                "            <hkparam name=\"name\">{name}</hkparam>\n" +
                "            <hkparam name=\"modifier\">null</hkparam>\n" +
                "            <hkparam name=\"generator\">{child}</hkparam>",
        },
        // Bethesda's own generator: it plays a NiControllerSequence out of the NIF rather than a
        // Havok animation, which is what every animated door, lift and switch is built from.
        // pSequence is the sequence name in the mesh, and it is not always the node's own name:
        // the garage door's "Closeing" state plays a sequence called "Closing".
        ["sequence"] = new Kind
        {
            Class = "BGSGamebryoSequenceGenerator",
            Signature = "0x4e708fb6",
            Body =
                "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
                "            <hkparam name=\"userData\">0</hkparam>\n" +
                "            <hkparam name=\"name\">{name}</hkparam>\n" +
                "            <hkparam name=\"pSequence\">{animation}</hkparam>\n" +
                "            <hkparam name=\"eBlendModeFunction\">BMF_NONE</hkparam>\n" +
                "            <hkparam name=\"fPercent\">1.0</hkparam>\n" +
                "            <hkparam name=\"eUseTimePercentage\">NOT_USING_TIME_PERCENTAGE</hkparam>\n" +
                "            <hkparam name=\"fTimePercent\">0.0</hkparam>",
        },
        ["selector"] = new Kind
        {
            Class = "hkbManualSelectorGenerator",
            Signature = "0xeed8d5cd",
            Body =
                "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
                "            <hkparam name=\"userData\">0</hkparam>\n" +
                "            <hkparam name=\"name\">{name}</hkparam>\n" +
                "            <hkparam name=\"selectedGeneratorIndex\">0</hkparam>\n" +
                "            <hkparam name=\"indexSelector\">null</hkparam>\n" +
                "            <hkparam name=\"selectedIndexCanChangeAfterActivate\">false</hkparam>\n" +
                "            <hkparam name=\"generatorChangedTransitionEffect\">null</hkparam>\n" +
                "            <hkparam name=\"generators\" numelements=\"0\">\n</hkparam>",
        },
    };

    private const string BlenderChildClass = "hkbBlenderGeneratorChild";
    private const string BlenderChildSignature = "0xb35bbfd3";

    public static string Add(string xml, string kind, string name, string animation,
                             string childRef, out string newId)
    {
        if (!Kinds.TryGetValue(kind, out var spec))
            throw new ArgumentException($"unknown generator kind '{kind}'; try {string.Join(", ", Kinds.Keys)}");

        string body = spec.Body
            .Replace("{name}", name)
            .Replace("{animation}", animation)
            .Replace("{child}", string.IsNullOrEmpty(childRef) ? "null" : childRef);

        return HkxTextEdit.AddObject(xml, spec.Class, spec.Signature, body, out newId);
    }

    // A blender does not hold generators directly, it holds hkbBlenderGeneratorChild wrappers that
    // carry the weight. Adding a raw generator reference to children produces a file the engine
    // cannot read even though hkxpack accepts it.
    public static string AddBlenderChild(string xml, string blenderId, string generatorRef, float weight,
                                         out string childId)
    {
        var model = BehaviourGraphModel.Parse(xml);
        var blender = model.Get(blenderId) ?? throw new ArgumentException($"#{blenderId} is not in this file");
        if (blender.Class != "hkbBlenderGenerator")
            throw new ArgumentException($"#{blenderId} is a {blender.Class}, not a blender");

        string body =
            "            <hkparam name=\"variableBindingSet\">null</hkparam>\n" +
            $"            <hkparam name=\"generator\">{generatorRef}</hkparam>\n" +
            "            <hkparam name=\"boneWeights\">null</hkparam>\n" +
            $"            <hkparam name=\"weight\">{weight.ToString("0.0#####", System.Globalization.CultureInfo.InvariantCulture)}</hkparam>\n" +
            "            <hkparam name=\"worldFromModelWeight\">0.0</hkparam>";

        xml = HkxTextEdit.AddObject(xml, BlenderChildClass, BlenderChildSignature, body, out childId);
        return HkxTextEdit.ArrayAppend(xml, blenderId, "children", $"                #{childId}");
    }

    public static string AttachToSelector(string xml, string selectorId, string generatorRef) =>
        HkxTextEdit.ArrayAppend(xml, selectorId, "generators", $"                {generatorRef}");

    // Deleting a node means nothing else may still point at it, otherwise the graph has a dangling
    // reference and the engine reads a null generator.
    public static List<string> ReferencesTo(BehaviourGraphModel model, string id)
    {
        // One holder per object however many times it names the target, which is what the callers
        // want: a list of things to go and clear, not a count of links. Object order is kept because
        // the delete note names the first few and a shuffled list would reword it.
        var holders = new List<string>();
        foreach (var obj in model.Objects)
            if (HkReferences.In(obj).Any(site => site.Target == id))
                holders.Add(obj.Id);
        return holders;
    }

    public static string Remove(string xml, string id, bool force, out List<string> blockers)
    {
        var model = BehaviourGraphModel.Parse(xml);
        if (model.Get(id) is null) throw new ArgumentException($"#{id} is not in this file");

        blockers = ReferencesTo(model, id);
        if (blockers.Count > 0 && !force)
            return xml;

        var (start, length) = HkxTextEdit.ObjectBlock(xml, id);
        if (start < 0) throw new ArgumentException($"#{id} has no block");
        return xml.Remove(start, length);
    }
}
