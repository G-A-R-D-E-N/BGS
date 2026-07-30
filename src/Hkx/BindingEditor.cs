using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// Structural edits for graph variables and their bindings. Everything here works on hkxpack's text
// form, so a repack is still what writes the .hkx.
public static class BindingEditor
{
    // From a vanilla file that already contains one; hkxpack rejects a wrong signature.
    private const string BindingSetSignature = "0xe942f339";

    public sealed class Binding
    {
        public string SetId = "";
        public int Index;
        public string MemberPath = "";
        public int VariableIndex = -1;
        public string BindingType = "";
    }

    public static List<string> VariableNames(BehaviourGraphModel model) =>
        model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")?.Strings("variableNames")
        ?? new List<string>();

    public static List<Binding> BindingsOf(BehaviourGraphModel model, HkObject owner)
    {
        var result = new List<Binding>();
        var set = model.Follow(owner, "variableBindingSet");
        if (set == null || !set.StructLists.TryGetValue("bindings", out var rows)) return result;

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].TryGetValue("memberPath", out var path);
            rows[i].TryGetValue("variableIndex", out var raw);
            rows[i].TryGetValue("bindingType", out var kind);
            result.Add(new Binding
            {
                SetId = set.Id,
                Index = i,
                MemberPath = path ?? "",
                VariableIndex = int.TryParse(raw, out int v) ? v : -1,
                BindingType = kind ?? "",
            });
        }
        return result;
    }

    private static string BindingElement(string memberPath, int variableIndex) =>
        "                <hkobject>\n" +
        $"                    <hkparam name=\"memberPath\">{memberPath}</hkparam>\n" +
        $"                    <hkparam name=\"variableIndex\">{variableIndex}</hkparam>\n" +
        "                    <hkparam name=\"bitIndex\">-1</hkparam>\n" +
        "                    <hkparam name=\"bindingType\">BINDING_TYPE_VARIABLE</hkparam>\n" +
        "                </hkobject>";

    // Adds a binding to the owner, creating the owner's binding set if it has none.
    public static string AddBinding(string xml, string ownerId, string memberPath, int variableIndex)
    {
        if (string.IsNullOrWhiteSpace(memberPath)) throw new ArgumentException("member path is empty");
        if (variableIndex < 0) throw new ArgumentException("no variable selected");

        string existing = OwnerBindingSetId(xml, ownerId);
        if (existing.Length > 0)
            return HkxTextEdit.ArrayAppend(xml, existing, "bindings", BindingElement(memberPath, variableIndex));

        string inner =
            "            <hkparam name=\"bindings\" numelements=\"1\">\n" +
            BindingElement(memberPath, variableIndex) + "\n" +
            "            </hkparam>\n" +
            "            <hkparam name=\"indexOfBindingToEnable\">-1</hkparam>";

        xml = HkxTextEdit.AddObject(xml, "hkbVariableBindingSet", BindingSetSignature, inner, out string newId);
        return HkxTextEdit.SetParam(xml, ownerId, "variableBindingSet", "#" + newId);
    }

    public static string RemoveBinding(string xml, string setId, int index)
    {
        xml = HkxTextEdit.ArrayRemoveAt(xml, setId, "bindings", index);

        // A set with no bindings left is dead weight; unhook whoever points at it rather than
        // leaving the engine to walk an empty set.
        if (CountBindings(xml, setId) == 0)
            foreach (string owner in OwnersOf(xml, setId))
                xml = HkxTextEdit.SetParam(xml, owner, "variableBindingSet", "null");

        return xml;
    }

    // Declares a new float variable and returns its index. Delegates, because a variable has to be
    // written into three parallel arrays and an earlier version of this silently skipped the type
    // declaration: it tested Lists for variableInfos, which is a struct list, so the test was always
    // false and the variable came out with no declared type.
    public static string AddVariable(string xml, string name, out int index) =>
        SymbolEditor.AddVariable(xml, name, SymbolEditor.VariableType.Real, out index);

    private static string OwnerBindingSetId(string xml, string ownerId)
    {
        foreach (var p in HkxTextEdit.ReadParams(xml, ownerId))
            if (p.Name == "variableBindingSet" && p.Value.StartsWith('#'))
                return p.Value[1..];
        return "";
    }

    private static int CountBindings(string xml, string setId)
    {
        var set = BehaviourGraphModel.Parse(xml).Get(setId);
        return set != null && set.StructLists.TryGetValue("bindings", out var rows) ? rows.Count : 0;
    }

    private static List<string> OwnersOf(string xml, string setId)
    {
        var owners = new List<string>();
        foreach (var obj in BehaviourGraphModel.Parse(xml).Objects)
            if (obj.Str("variableBindingSet") == "#" + setId)
                owners.Add(obj.Id);
        return owners;
    }
}
