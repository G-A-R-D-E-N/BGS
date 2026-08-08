using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// How much each child of a blender actually contributes.
//
// This is the question the weapon idle work asks and the one a static picture cannot answer. A
// blender mixes several animations, and which of them you are looking at, and how much, is the whole
// point of a blend. The canvas can draw the children; it cannot say the mix.
//
// There are two kinds of blender and they read the same child `weight` field to mean opposite things,
// which is the trap here. A plain blender mixes every child at once, each in proportion to its
// weight. A parametric blender instead lines its children up along an axis, at the positions its
// weights give, and a single blend parameter slides along that axis picking out the one or two
// nearest. Treating one as the other is not a rounding error: it reports a walk cycle mixed evenly
// into an idle when only one of them is playing.
//
// The two are told apart by a flag the game sets, checked against the shipped data rather than
// assumed: bit 0x10 is set on every one of the 208 parametric blenders whose blend parameter is
// driven by a variable, and on the 74 whose parameter is a constant, and on none of the 499 plain
// ones. See `symrm weights`.
//
// What this refuses to invent: a mix driven by a variable. 208 of the 781 blenders in the corpus
// slide their parameter from a variable set outside the graph, and 26 of 2,122 children have a
// weight bound to a variable directly. None of that is in the file as a number, so it is reported as
// driven from outside and named, not guessed at.
public static class BlendWeights
{
    /// hkbBlenderGenerator::BlenderFlags::FLAG_IS_PARAMETRIC. Set means the children are laid out
    /// along an axis and picked by the blend parameter, not mixed all at once.
    public const int Parametric = 0x10;

    public enum Mode
    {
        /// Every child mixed at once, in proportion to its weight.
        Mix,
        /// Children laid along an axis, picked by a blend parameter this file carries as a number.
        Parametric,
        /// Parametric, but the parameter is set by a variable outside the graph, so the mix is not
        /// knowable from the file.
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
        /// Whether the mix is a fact of the file rather than something a variable decides at runtime.
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

    /// Works out the mix of one blender.
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

        // The blend parameter: a constant in the file, unless a binding slides it from a variable.
        var (paramDriven, paramName) = BlendParameterBinding(model, blender);
        float paramValue = Real(blender.Str("blendParameter"));

        Mode mode = !parametric ? Mode.Mix
                  : paramDriven ? Mode.ParametricDriven
                  : Mode.Parametric;

        float[] contributions = mode switch
        {
            Mode.Mix => MixContributions(raw),
            Mode.Parametric => ParametricContributions(raw, paramValue),
            _ => new float[raw.Count],   // unknowable, left at zero and reported as driven
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

    /// Every blender in the file, resolved.
    public static IEnumerable<Result> All(BehaviourGraphModel model) =>
        model.Objects.Where(o => o.Class == "hkbBlenderGenerator").Select(o => Of(model, o.Id));

    // A plain mix: each child in proportion to its weight, normalised so the shares sum to one. A
    // child weighted zero contributes nothing, which is how vanilla switches a child off without
    // removing it. If every weight is zero the blender contributes nothing and the shares are zero
    // rather than a divide by zero.
    private static float[] MixContributions(List<(string Id, float Weight, bool Driven, string Driver)> raw)
    {
        float total = raw.Where(c => !c.Driven).Sum(c => Math.Max(0, c.Weight));
        var shares = new float[raw.Count];
        if (total <= 0) return shares;
        for (int i = 0; i < raw.Count; i++)
            if (!raw[i].Driven) shares[i] = Math.Max(0, raw[i].Weight) / total;
        return shares;
    }

    // A parametric pick: the children sit at the positions their weights give, and the parameter
    // slides along that axis. The two children bracketing it share the weight by how close the
    // parameter sits to each; everything else is off. This is Havok's own parametric blend, and it is
    // why a parametric blender's weights are positions and not shares.
    private static float[] ParametricContributions(
        List<(string Id, float Weight, bool Driven, string Driver)> raw, float parameter)
    {
        var shares = new float[raw.Count];
        var points = raw.Select((c, i) => (Index: i, Pos: c.Weight)).OrderBy(p => p.Pos).ToList();
        if (points.Count == 0) return shares;

        // Below the first or above the last, the nearest end takes it all.
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

    /// Whether a member of an object is bound to a variable, and which one.
    ///
    /// A binding names its member by a path and the variable by an index. The index is turned into a
    /// name here, because a number names nothing to anyone reading the answer, and a driver reported
    /// as "variable 7" is only nominally more use than not reporting it at all.
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
