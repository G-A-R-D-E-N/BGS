using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class PredefinedTemplates
{
    public const int MinimumBlendChildren = 1;
    public const int MaximumBlendChildren = 16;
    public enum SlotKind
    {
        Text,
        Count,
        Choice,
        ObjectReference,
    }

    public sealed record Slot(string Key, string DisplayName, string Description, SlotKind Kind,
                              bool Required, string DefaultValue = "",
                              int Minimum = 0, int Maximum = 0,
                              IReadOnlyList<string>? Choices = null);

    public sealed record Definition(string Id, string DisplayName, string Description, string RootClass,
                                    IReadOnlyList<Slot> Slots);

    public sealed record Resolution(IReadOnlyDictionary<string, string> Values, string? Refusal)
    {
        public bool Possible => Refusal == null;
        public string Text(string key) => Values.TryGetValue(key, out var value) ? value : "";
    }

    public sealed record Result(byte[]? Bytes, int RootId, IReadOnlyList<int> CreatedIds, string Summary,
                                string? Refusal)
    {
        public bool Possible => Refusal == null;
    }

    private static readonly IReadOnlyList<Definition> Catalog = new[]
    {
        new Definition(
            "clip-generator",
            "New Clip Generator",
            "Creates a clip generator with the normal playback defaults.",
            "hkbClipGenerator",
            new[]
            {
                new Slot("name", "Name", "The generator name.", SlotKind.Text, false, "New Clip"),
                new Slot("animation", "Animation", "The animation this clip plays.", SlotKind.Text, true),
                new Slot("mode", "Playback mode", "The clip playback mode.", SlotKind.Choice, false,
                         "looping", Choices: new[] { "looping", "single-play" }),
            }),
        new Definition(
            "blend-generator",
            "Blend Generator",
            "Creates a blend generator with a requested number of child slots.",
            "hkbBlenderGenerator",
            new[]
            {
                new Slot("name", "Name", "The generator name.", SlotKind.Text, false, "New Blend"),
                new Slot("children", "Children", "How many empty blend child slots to create.", SlotKind.Count,
                         true, Minimum: MinimumBlendChildren, Maximum: MaximumBlendChildren),
            }),
        new Definition(
            "state-with-generator",
            "New State with Generator",
            "Creates a state and attaches either an existing generator or a new clip generator.",
            "hkbStateMachineStateInfo",
            new[]
            {
                new Slot("machine", "State machine", "The state machine that receives the state.",
                         SlotKind.ObjectReference, true),
                new Slot("name", "Name", "The state name.", SlotKind.Text, false, "New State"),
                new Slot("generator", "Generator", "An existing generator to attach.",
                         SlotKind.ObjectReference, false),
                new Slot("animation", "Animation", "Required when creating the attached clip generator.",
                         SlotKind.Text, false),
            }),
    };

    public static IReadOnlyList<Definition> All() => Catalog;

    public static Definition? Get(string id) => Catalog.FirstOrDefault(definition => definition.Id == id);

    public static Resolution Resolve(Definition definition, IReadOnlyDictionary<string, string> raw)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in definition.Slots)
        {
            string value = raw.TryGetValue(slot.Key, out var supplied) ? supplied.Trim() : slot.DefaultValue;
            if (slot.Required && value.Length == 0)
                return new Resolution(values, $"{slot.DisplayName} ({slot.Key}) is required.");

            if (slot.Kind == SlotKind.Count && value.Length > 0 &&
                (!int.TryParse(value, out int count) || count < slot.Minimum || count > slot.Maximum))
                return new Resolution(values, $"{slot.DisplayName} must be between {slot.Minimum} and {slot.Maximum}.");

            if (slot.Kind == SlotKind.Choice && value.Length > 0 &&
                (slot.Choices == null || !slot.Choices.Contains(value, StringComparer.Ordinal)))
                return new Resolution(values, $"{slot.DisplayName} must be one of: {string.Join(", ", slot.Choices ?? Array.Empty<string>())}.");

            values[slot.Key] = value;
        }

        return new Resolution(values, null);
    }

    public static Result Instantiate(string path, string templateId, IReadOnlyDictionary<string, string> raw)
    {
        var definition = Get(templateId);
        if (definition == null) return Failed($"Unknown predefined template '{templateId}'.");

        var resolved = Resolve(definition, raw);
        if (!resolved.Possible) return Failed(resolved.Refusal!);

        byte[] source;
        PackfileObjects objects;
        try
        {
            source = File.ReadAllBytes(path);
            objects = new PackfileObjects(PackfileImage.Read(source), HavokClasses.Shipped);
        }
        catch (Exception e) { return Failed(e.Message); }

        var before = NativeGraphModel.From(objects);
        var existingErrors = before == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : GraphValidator.Check(before, objects: objects).Where(finding => finding.Level == GraphValidator.Level.Error)
                            .Select(FindingKey).ToHashSet(StringComparer.Ordinal);

        try
        {
            var plan = new List<NativeSave.Change>();
            var created = new List<int>();
            int Add(string className)
            {
                int id = NativeGraphModel.FirstId + objects.Instances.Count + created.Count;
                int index = objects.Instances.Count(i => i.ClassName == className) +
                            plan.Count(c => c.Added && c.ClassName == className);
                plan.Add(new NativeSave.Change(className, index, "", "#" + id, Added: true));
                created.Add(id);
                return id;
            }
            int Index(string className, int id) => objects.Instances.Count(i => i.ClassName == className) +
                                                   created.TakeWhile(value => value != id)
                                                          .Count(value => plan.First(c => c.Added && c.Value == "#" + value).ClassName == className);
            void Field(string className, int id, string field, string value) =>
                plan.Add(new NativeSave.Change(className, Index(className, id), field, value));
            void Ref(string className, int id, string field, int target) =>
                plan.Add(new NativeSave.Change(className, Index(className, id), field, "#" + target, Ref: true));

            int Clip()
            {
                return AddClip(resolved.Text("name"), resolved.Text("animation"), resolved.Text("mode"));
            }

            int AddClip(string name, string animation, string mode)
            {
                int clip = Add("hkbClipGenerator");
                Field("hkbClipGenerator", clip, "name", name);
                Field("hkbClipGenerator", clip, "animationName", animation);
                Field("hkbClipGenerator", clip, "playbackSpeed", "1");
                Field("hkbClipGenerator", clip, "animationBindingIndex", "-1");
                Field("hkbClipGenerator", clip, "mode", mode == "single-play" ? "0" : "1");
                return clip;
            }

            int root;
            if (templateId == "clip-generator") root = Clip();
            else if (templateId == "blend-generator")
            {
                int count = int.Parse(resolved.Text("children"));
                root = Add("hkbBlenderGenerator");
                Field("hkbBlenderGenerator", root, "name", resolved.Text("name"));
                Field("hkbBlenderGenerator", root, "blendParameter", "1");
                Field("hkbBlenderGenerator", root, "maxCyclicBlendParameter", "1");
                Field("hkbBlenderGenerator", root, "indexOfSyncMasterChild", "-1");
                Field("hkbBlenderGenerator", root, "flags", "8");
                var children = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int child = Add("hkbBlenderGeneratorChild");
                    Field("hkbBlenderGeneratorChild", child, "weight", "1");
                    children.Add(child);
                }
                plan.Add(new NativeSave.Change("hkbBlenderGenerator", Index("hkbBlenderGenerator", root),
                                                "children", string.Join(" ", children.Select(id => "#" + id)), Array: true));
            }
            else
            {
                if (!TryObject(resolved.Text("machine"), objects, out int machine, out string machineClass) ||
                    machineClass != "hkbStateMachine") return Failed("State machine must name an hkbStateMachine in this file.");

                int generator;
                if (resolved.Text("generator").Length > 0)
                {
                    if (!TryObject(resolved.Text("generator"), objects, out generator, out string generatorClass) ||
                        !IsA(generatorClass, "hkbGenerator"))
                        return Failed("Generator must name a generator in this file.");
                }
                else
                {
                    if (resolved.Text("animation").Length == 0) return Failed("Animation is required when no generator is selected.");
                    generator = AddClip("New Clip", resolved.Text("animation"), "looping");
                }

                root = Add("hkbStateMachineStateInfo");
                int stateId = NextStateId(objects, machine);
                Field("hkbStateMachineStateInfo", root, "name", resolved.Text("name"));
                Field("hkbStateMachineStateInfo", root, "stateId", stateId.ToString());
                Field("hkbStateMachineStateInfo", root, "probability", "1");
                Field("hkbStateMachineStateInfo", root, "enable", "true");
                Ref("hkbStateMachineStateInfo", root, "generator", generator);
                var states = objects.ReadRefArray(objects.Instances[machine - NativeGraphModel.FirstId], "states")
                    ?.Select(state => state == null ? "null" : "#" + (NativeGraphModel.FirstId + objects.Instances.ToList().IndexOf(state)))
                    .Append("#" + root) ?? new[] { "#" + root };
                plan.Add(new NativeSave.Change("hkbStateMachine", objects.Instances.Take(machine - NativeGraphModel.FirstId)
                    .Count(instance => instance.ClassName == "hkbStateMachine"), "states", string.Join(" ", states), Array: true));
            }

            byte[] bytes = NativeSave.Apply(source, new NativeSave.Plan(plan, null));
            var reopened = new PackfileObjects(PackfileImage.Read(bytes), HavokClasses.Shipped);
            var model = NativeGraphModel.From(reopened) ?? throw new InvalidOperationException("The rebuilt file could not be modeled.");
            var errors = GraphValidator.Check(model, objects: reopened)
                .Where(finding => finding.Level == GraphValidator.Level.Error && !existingErrors.Contains(FindingKey(finding))).ToList();
            if (errors.Count > 0) return Failed(errors[0].What);
            return new Result(bytes, root, created, $"Created {definition.DisplayName}.", null);
        }
        catch (Exception e) { return Failed(e.Message); }
    }

    private static Result Failed(string refusal) => new(null, -1, Array.Empty<int>(), "", refusal);

    private static bool IsA(string className, string expected)
    {
        for (string? current = className; current != null; current = HavokClassTypes.Shipped[current]?.Parent)
            if (current == expected) return true;
        return false;
    }

    private static string FindingKey(GraphValidator.Finding finding) => finding.Where + "\n" + finding.What;

    private static bool TryObject(string value, PackfileObjects objects, out int id, out string className)
    {
        id = -1;
        className = "";
        if (!int.TryParse(value.TrimStart('#'), out id)) return false;
        int index = id - NativeGraphModel.FirstId;
        if (index < 0 || index >= objects.Instances.Count) return false;
        className = objects.Instances[index].ClassName;
        return true;
    }

    private static int NextStateId(PackfileObjects objects, int machine)
    {
        var used = objects.ReadRefArray(objects.Instances[machine - NativeGraphModel.FirstId], "states")
            ?.Where(state => state?.ClassName == "hkbStateMachineStateInfo")
            .Select(state => objects.ReadInt(state!, "stateId") ?? -1).ToList() ?? new List<int>();
        return used.Count == 0 ? 0 : used.Max() + 1;
    }
}
