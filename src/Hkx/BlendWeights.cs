using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class BlendWeights
{

    public const int Parametric = 0x10;

    public enum Mode
    {

        Mix,

        Parametric,

        ParametricDriven,
    }

    public sealed record Child(string GeneratorId, string GeneratorName, string GeneratorClass,
                               float Weight, bool WeightDriven, string WeightDriver, float Contribution)
    {
        public override string ToString()
        {
            string who = GeneratorName.Length > 0 ? GeneratorName : "#" + GeneratorId;
            string share = WeightDriven ? $"driven by {WeightDriver}" : $"{Contribution * 100:F0}%";
            return $"{who}  {share}";
        }
    }

    public sealed record Result(string BlenderId, string BlenderName, Mode Mode, string Parameter,
                                float ParameterValue, IReadOnlyList<Child> Children)
    {

        public bool Resolved => Mode != Mode.ParametricDriven && Children.All(c => !c.WeightDriven);

        public override string ToString()
        {
            string head = Mode switch
            {
                Mode.Mix => $"mixes {Children.Count} child(ren)",
                Mode.Parametric => $"parametric on {Parameter} = {ParameterValue:F3}",
                _ => $"parametric, driven by {Parameter}",
            };
            return $"#{BlenderId} '{BlenderName}' {head}";
        }
    }

    public static Result Of(BehaviourGraphModel model, string blenderId)
    {
        var blender = model.Get(blenderId)
            ?? throw new InvalidOperationException($"#{blenderId} is not in this file");
        if (blender.Class != "hkbBlenderGenerator")
            throw new InvalidOperationException($"#{blenderId} is a {blender.Class}, not a blender");

        int flags = blender.Int("flags", 0);
        bool parametric = (flags & Parametric) != 0;

        var raw = new List<(string Id, float Weight, bool Driven, string Driver)>();
        foreach (string childId in blender.Refs("children"))
        {
            var child = model.Get(childId);
            if (child == null || child.Class != "hkbBlenderGeneratorChild") continue;

            float weight = Real(child.Str("weight"));
            var (driven, driver) = WeightBinding(model, child);
            string generator = child.Ref("generator") ?? "";
            raw.Add((generator, weight, driven, driver));
        }

        var (paramDriven, paramName) = BlendParameterBinding(model, blender);
        float paramValue = Real(blender.Str("blendParameter"));

        Mode mode = !parametric ? Mode.Mix
                  : paramDriven ? Mode.ParametricDriven
                  : Mode.Parametric;

        float[] contributions = mode switch
        {
            Mode.Mix => MixContributions(raw),
            Mode.Parametric => ParametricContributions(raw, paramValue),
            _ => new float[raw.Count],
        };

        var children = new List<Child>();
        for (int i = 0; i < raw.Count; i++)
        {
            var g = model.Get(raw[i].Id);
            children.Add(new Child(raw[i].Id, g?.Str("name") ?? "", g?.Class ?? "",
                raw[i].Weight, raw[i].Driven, raw[i].Driver, contributions[i]));
        }

        return new Result(blenderId, blender.Str("name"), mode,
            paramDriven ? paramName : "blendParameter", paramValue, children);
    }

    public static IEnumerable<Result> All(BehaviourGraphModel model) =>
        model.Objects.Where(o => o.Class == "hkbBlenderGenerator").Select(o => Of(model, o.Id));

    private static float[] MixContributions(List<(string Id, float Weight, bool Driven, string Driver)> raw)
    {
        float total = raw.Where(c => !c.Driven).Sum(c => Math.Max(0, c.Weight));
        var shares = new float[raw.Count];
        if (total <= 0) return shares;
        for (int i = 0; i < raw.Count; i++)
            if (!raw[i].Driven) shares[i] = Math.Max(0, raw[i].Weight) / total;
        return shares;
    }

    private static float[] ParametricContributions(
        List<(string Id, float Weight, bool Driven, string Driver)> raw, float parameter)
    {
        var shares = new float[raw.Count];
        var points = raw.Select((c, i) => (Index: i, Pos: c.Weight)).OrderBy(p => p.Pos).ToList();
        if (points.Count == 0) return shares;

        if (parameter <= points[0].Pos) { shares[points[0].Index] = 1; return shares; }
        if (parameter >= points[^1].Pos) { shares[points[^1].Index] = 1; return shares; }

        for (int i = 0; i < points.Count - 1; i++)
        {
            float lo = points[i].Pos, hi = points[i + 1].Pos;
            if (parameter < lo || parameter > hi) continue;

            float span = hi - lo;
            float t = span > 1e-9f ? (parameter - lo) / span : 0;
            shares[points[i].Index] = 1 - t;
            shares[points[i + 1].Index] = t;
            break;
        }
        return shares;
    }

    private static (bool Driven, string Driver) WeightBinding(BehaviourGraphModel model, HkObject child)
        => Binding(model, child.Ref("variableBindingSet") ?? "", "weight");

    private static (bool Driven, string Driver) BlendParameterBinding(BehaviourGraphModel model, HkObject blender)
        => Binding(model, blender.Ref("variableBindingSet") ?? "", "blendParameter");

    private static (bool Driven, string Driver) Binding(BehaviourGraphModel model, string bindingSetId, string member)
    {
        if (bindingSetId.Length == 0) return (false, "");
        var set = model.Get(bindingSetId);
        if (set == null || !set.StructLists.TryGetValue("bindings", out var rows)) return (false, "");

        var names = SymbolEditor.VariableNames(model);
        foreach (var row in rows)
        {
            if (!row.TryGetValue("memberPath", out var path) ||
                !path.EndsWith(member, StringComparison.Ordinal)) continue;

            string driver = member;
            if (row.TryGetValue("variableIndex", out var vi) && int.TryParse(vi, out int index)
                && index >= 0 && index < names.Count)
                driver = names[index];

            return (true, driver);
        }
        return (false, "");
    }

    private static float Real(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;
}
