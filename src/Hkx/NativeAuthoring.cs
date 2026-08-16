using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public sealed class NativeAuthoringPlan
{
    public sealed record ObjectRef(int Id, string ClassName)
    {
        public string Reference => "#" + Id.ToString(CultureInfo.InvariantCulture);
    }

    public sealed record Result(byte[] Bytes, IReadOnlyList<GraphValidator.Finding> Findings);

    private readonly byte[] _source;
    private readonly PackfileObjects _sourceObjects;
    private readonly List<NativeSave.Change> _changes = new();
    private readonly Dictionary<int, ObjectRef> _objects = new();
    private readonly Dictionary<int, int> _classIndices = new();
    private readonly Dictionary<string, int> _classCounts = new(StringComparer.Ordinal);
    private int _nextId;

    public NativeAuthoringPlan(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source.ToArray();

        _sourceObjects = new PackfileObjects(PackfileImage.Read(_source), HavokClasses.Shipped);
        for (int i = 0; i < _sourceObjects.Instances.Count; i++)
        {
            int id = NativeGraphModel.FirstId + i;
            string className = _sourceObjects.Instances[i].ClassName;
            int classIndex = _classCounts.GetValueOrDefault(className);
            _objects[id] = new ObjectRef(id, className);
            _classIndices[id] = classIndex;
            _classCounts[className] = classIndex + 1;
        }

        _nextId = NativeGraphModel.FirstId + _sourceObjects.Instances.Count;
    }

    // The packfile parse this plan already performed over the untouched source, exposed so callers
    // can model the source without reading its bytes a second time.
    internal PackfileObjects SourceObjects => _sourceObjects;

    public bool Contains(int id) => _objects.ContainsKey(id);

    public string ClassOf(int id) => RequireObject(id).ClassName;

    public ObjectRef AddObject(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) throw new ArgumentException("class name is required", nameof(className));

        var type = HavokClassTypes.Shipped[className];
        var layout = HavokClasses.Shipped[className];
        if (type?.Size is not int size || size <= 0 || layout == null)
            throw new InvalidOperationException($"{className} has no complete native layout in this build");

        int id = _nextId++;
        int index = _classCounts.GetValueOrDefault(className);
        var added = new ObjectRef(id, className);
        _objects[id] = added;
        _classIndices[id] = index;
        _classCounts[className] = index + 1;

        _changes.Add(new NativeSave.Change(
            className, index, "", added.Reference, Added: true, Id: id));
        return added;
    }

    public void SetString(int id, string field, string value) => SetScalar(id, field, value ?? "");

    public void SetInt(int id, string field, int value) =>
        SetScalar(id, field, value.ToString(CultureInfo.InvariantCulture));

    public void SetReal(int id, string field, float value) =>
        SetScalar(id, field, value.ToString("R", CultureInfo.InvariantCulture));

    public void SetBool(int id, string field, bool value) =>
        SetScalar(id, field, value ? "true" : "false");

    public void SetEnum(int id, string field, string name)
    {
        var obj = RequireObject(id);
        var member = HavokClassTypes.Shipped.Members(obj.ClassName)
            .FirstOrDefault(m => m.Name == field)
            ?? throw new InvalidOperationException($"{obj.ClassName}.{field} is not a known field");

        if (member.VType is not ("TYPE_ENUM" or "TYPE_FLAGS") || member.EType == null)
            throw new InvalidOperationException($"{obj.ClassName}.{field} is not an enum or flags field");

        var values = HavokClassTypes.Shipped.Enum(obj.ClassName, member.EType)
            ?? throw new InvalidOperationException($"{obj.ClassName}.{field} has no enum table");
        if (!values.TryGetValue(name, out long number))
            throw new ArgumentException($"{name} is not a value of {obj.ClassName}.{field}", nameof(name));

        SetScalar(id, field, number.ToString(CultureInfo.InvariantCulture));
    }

    public void SetReference(int id, string field, int? targetId)
    {
        var obj = RequireObject(id);
        var member = RequireField(obj, field);
        if (!NativeSave.IsReference(member.Type))
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {member.Type}, not a reference");

        string value = "null";
        if (targetId is int target)
        {
            var targetObject = RequireObject(target);
            var typed = HavokClassTypes.Shipped.Members(obj.ClassName).FirstOrDefault(m => m.Name == field);
            RequireAssignable(targetObject, typed?.CType, $"{obj.ClassName}.{field}");
            value = targetObject.Reference;
        }

        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field, value, Ref: true, Id: id),
            c => SameField(c, id, field) && !c.InElement);
    }

    public void SetPointerArray(int id, string field, IEnumerable<int> targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        var obj = RequireObject(id);
        var member = RequireField(obj, field);
        if (!NativeSave.IsPointerArray(member.Type))
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {member.Type}, not an array of references");

        var typed = HavokClassTypes.Shipped.Members(obj.ClassName).FirstOrDefault(m => m.Name == field);
        var targets = targetIds.Select(target => RequireObject(target)).ToList();
        foreach (var target in targets)
            RequireAssignable(target, typed?.CType, $"{obj.ClassName}.{field}");

        string value = string.Join(" ", targets.Select(target => target.Reference));
        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field, value, Array: true, Id: id),
            c => SameField(c, id, field) && !c.InElement);
    }

    public void SetTextArray(int id, string field, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var obj = RequireObject(id);
        var member = RequireField(obj, field);
        if (!NativeSave.IsTextArray(member.Type))
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {member.Type}, not a text array");

        string value = string.Join("\0", values);
        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field, value, Text: true, Array: true, Id: id),
            c => SameField(c, id, field) && !c.InElement);
    }

    public void ResizeStructArray(int id, string field, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var obj = RequireObject(id);
        var member = RequireField(obj, field);
        if (member.Type != "array of struct")
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {member.Type}, not an array of structs");

        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field,
                count.ToString(CultureInfo.InvariantCulture), Element: 0, Grow: true, Id: id),
            c => c.Id == id && c.Field == field && c.Grow);
    }

    public void SetStructMember(int id, string field, int element, string member, string value)
    {
        if (element < 0) throw new ArgumentOutOfRangeException(nameof(element));
        if (string.IsNullOrWhiteSpace(member)) throw new ArgumentException("member is required", nameof(member));

        var obj = RequireObject(id);
        var layout = RequireField(obj, field);
        if (layout.Type is not ("array of struct" or "struct"))
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {layout.Type}, not structured data");
        if (layout.Type == "struct" && element != 0)
            throw new ArgumentOutOfRangeException(nameof(element), "inline structs only have element 0");

        var structured = RequireStructuredMember(obj, field, member);
        if (structured.VType == "TYPE_POINTER")
        {
            if (value == "null")
            {
            }
            else if (value.Length > 1 && value[0] == '#' &&
                     int.TryParse(value[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId))
            {
                var target = RequireObject(targetId);
                RequireAssignable(target, structured.CType, $"{obj.ClassName}.{field}.{member}");
            }
            else
            {
                throw new ArgumentException(
                    $"{obj.ClassName}.{field}.{member} needs an object id or null", nameof(value));
            }
        }

        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field, value,
                Element: element, Member: member, Id: id),
            c => c.Id == id && c.Field == field && c.Element == element && c.Member == member && !c.Grow);
    }

    public NativeSave.Plan ToSavePlan() => new(_changes.ToList(), null);

    public Result Apply()
    {
        var plan = ToSavePlan();
        byte[] bytes = NativeSave.Apply(_source, plan);
        SaveVerifier.Verify(_source, bytes, plan);

        var objects = new PackfileObjects(PackfileImage.Read(bytes), HavokClasses.Shipped);
        var model = NativeGraphModel.From(objects)
            ?? throw new InvalidOperationException("the authored file could not be modeled after the native write");

        var findings = GraphValidator.Check(model, objects: objects);
        var blocking = findings.Where(f => f.BlocksSave).ToList();
        if (blocking.Count > 0)
            throw new InvalidOperationException(
                "the authored graph failed validation: " + string.Join("; ", blocking.Select(f => f.ToString())));

        return new Result(bytes, findings);
    }

    internal void RequireAssignable(int id, string expectedClass, string role)
    {
        if (string.IsNullOrWhiteSpace(expectedClass))
            throw new ArgumentException("expected class is required", nameof(expectedClass));
        RequireAssignable(RequireObject(id), expectedClass, role);
    }

    private void SetScalar(int id, string field, string value)
    {
        var obj = RequireObject(id);
        var member = RequireField(obj, field);
        if (NativeSave.IsReference(member.Type) || NativeSave.IsPointerArray(member.Type) ||
            NativeSave.IsTextArray(member.Type) || member.Type.StartsWith("array of ", StringComparison.Ordinal) ||
            member.Type == "struct")
            throw new InvalidOperationException($"{obj.ClassName}.{field} is {member.Type}, not a scalar field");

        bool text = member.Type is "stringptr" or "cstring";
        Upsert(
            new NativeSave.Change(obj.ClassName, IndexOf(id), field, value, Text: text, Id: id),
            c => SameField(c, id, field) && !c.InElement);
    }

    private ObjectRef RequireObject(int id) =>
        _objects.TryGetValue(id, out var obj)
            ? obj
            : throw new ArgumentException($"#{id} is not in this authoring session", nameof(id));

    private static HavokClasses.Member RequireField(ObjectRef obj, string field) =>
        HavokClasses.Shipped.Field(obj.ClassName, field)
        ?? throw new InvalidOperationException($"{obj.ClassName}.{field} is not in the native class layout");

    private static HavokClassTypes.Member RequireStructuredMember(ObjectRef obj, string field, string memberPath)
    {
        var types = HavokClassTypes.Shipped;
        var outer = types.Members(obj.ClassName).FirstOrDefault(member => member.Name == field)
            ?? throw new InvalidOperationException($"{obj.ClassName}.{field} is not in the class metadata");
        string owner = outer.CType
            ?? throw new InvalidOperationException($"{obj.ClassName}.{field} has no structured element class");

        HavokClassTypes.Member? found = null;
        string[] parts = memberPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("member path is required", nameof(memberPath));

        for (int i = 0; i < parts.Length; i++)
        {
            found = types.Members(owner).FirstOrDefault(member => member.Name == parts[i])
                ?? throw new InvalidOperationException($"{owner}.{parts[i]} is not in the class metadata");
            if (i == parts.Length - 1) break;
            if (found.VType != "TYPE_STRUCT" || found.CType == null)
                throw new InvalidOperationException($"{owner}.{parts[i]} is not an inline struct");
            owner = found.CType;
        }

        return found!;
    }

    private static void RequireAssignable(ObjectRef target, string? expectedClass, string role)
    {
        if (string.IsNullOrWhiteSpace(expectedClass)) return;
        if (IsAssignable(target.ClassName, expectedClass)) return;
        throw new ArgumentException(
            $"{role} expects {expectedClass}, but #{target.Id} is {target.ClassName}");
    }

    private static bool IsAssignable(string actualClass, string expectedClass)
    {
        var types = HavokClassTypes.Shipped;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? current = actualClass; current != null && seen.Add(current); current = types[current]?.Parent)
            if (string.Equals(current, expectedClass, StringComparison.Ordinal)) return true;
        return false;
    }

    private int IndexOf(int id) => _classIndices[id];

    private static bool SameField(NativeSave.Change change, int id, string field) =>
        change.Id == id && change.Field == field && !change.Added && !change.Grow;

    private void Upsert(NativeSave.Change change, Func<NativeSave.Change, bool> matches)
    {
        int at = _changes.FindIndex(c => matches(c));
        if (at >= 0) _changes[at] = change;
        else _changes.Add(change);
    }
}

public sealed class BehaviourAuthoringSession
{
    public sealed record StateRef(int ObjectId, int StateId);
    public sealed record TransitionRef(int ArrayObjectId, int Index);

    private readonly NativeAuthoringPlan _plan;
    private readonly BehaviourGraphModel _model;
    private readonly Dictionary<int, List<int>> _statesByMachine = new();
    private readonly Dictionary<int, int> _stateIds = new();
    private readonly Dictionary<(int Owner, string Field), int> _transitionArrays = new();
    private readonly Dictionary<int, int> _transitionCounts = new();
    private List<string>? _events;

    public BehaviourAuthoringSession(byte[] source)
    {
        _plan = new NativeAuthoringPlan(source);
        _model = NativeGraphModel.From(_plan.SourceObjects)
            ?? throw new InvalidOperationException("the source file cannot be represented by the native graph model");
    }

    public NativeAuthoringPlan.ObjectRef AddClip(string name, string animationName, int bindingIndex = -1,
                                                  float playbackSpeed = 1.0f)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("clip name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(animationName))
            throw new ArgumentException("animation name is required", nameof(animationName));
        if (playbackSpeed <= 0 || float.IsNaN(playbackSpeed) || float.IsInfinity(playbackSpeed))
            throw new ArgumentOutOfRangeException(nameof(playbackSpeed));

        var clip = _plan.AddObject("hkbClipGenerator");
        _plan.SetString(clip.Id, "name", name);
        _plan.SetString(clip.Id, "animationBundleName", "");
        _plan.SetString(clip.Id, "animationName", animationName);
        _plan.SetInt(clip.Id, "animationBindingIndex", bindingIndex);
        _plan.SetReal(clip.Id, "playbackSpeed", playbackSpeed);
        _plan.SetEnum(clip.Id, "mode", "MODE_LOOPING");
        return clip;
    }

    public StateRef AddState(int machineId, string name, int generatorId)
    {
        var states = EnsureMachine(machineId);
        _plan.RequireAssignable(generatorId, "hkbGenerator", "state generator");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("state name is required", nameof(name));

        int stateId = states.Count == 0
            ? 0
            : states.Select(id => _stateIds[id]).Max() + 1;

        var state = _plan.AddObject("hkbStateMachineStateInfo");
        _plan.SetReference(state.Id, "generator", generatorId);
        _plan.SetString(state.Id, "name", name);
        _plan.SetInt(state.Id, "stateId", stateId);
        _plan.SetReal(state.Id, "probability", 1.0f);
        _plan.SetBool(state.Id, "enable", true);

        states.Add(state.Id);
        _stateIds[state.Id] = stateId;
        _plan.SetPointerArray(machineId, "states", states);
        return new StateRef(state.Id, stateId);
    }

    public int AddEvent(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("event name is required", nameof(name));

        var strings = _model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData")
            ?? throw new InvalidOperationException("this graph has no hkbBehaviorGraphStringData");
        var data = _model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphData")
            ?? throw new InvalidOperationException("this graph has no hkbBehaviorGraphData");

        _events ??= SymbolEditor.EventNames(_model).ToList();
        if (_events.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"event '{name}' already exists", nameof(name));

        int index = _events.Count;
        _events.Add(name);

        int stringsId = int.Parse(strings.Id, CultureInfo.InvariantCulture);
        int dataId = int.Parse(data.Id, CultureInfo.InvariantCulture);
        _plan.SetTextArray(stringsId, "eventNames", _events);
        _plan.ResizeStructArray(dataId, "eventInfos", _events.Count);
        _plan.SetStructMember(dataId, "eventInfos", index, "flags", "0");
        return index;
    }

    public TransitionRef AddTransition(int machineId, int? fromStateObjectId, int toStateObjectId,
                                       int eventId, int? effectId = null)
    {
        var states = EnsureMachine(machineId);
        if (!states.Contains(toStateObjectId))
            throw new ArgumentException($"#{toStateObjectId} is not a state of #{machineId}", nameof(toStateObjectId));
        if (fromStateObjectId is int from && !states.Contains(from))
            throw new ArgumentException($"#{from} is not a state of #{machineId}", nameof(fromStateObjectId));
        if (eventId < -1) throw new ArgumentOutOfRangeException(nameof(eventId));

        int eventCount = (_events ?? SymbolEditor.EventNames(_model)).Count;
        if (eventId >= eventCount)
            throw new ArgumentOutOfRangeException(nameof(eventId),
                $"event {eventId} is not declared; this graph currently has {eventCount} event(s)");
        if (effectId.HasValue)
            _plan.RequireAssignable(effectId.Value, "hkbTransitionEffect", "transition effect");

        int owner = fromStateObjectId ?? machineId;
        string field = fromStateObjectId.HasValue ? "transitions" : "wildcardTransitions";
        var key = (owner, field);

        if (!_transitionArrays.TryGetValue(key, out int arrayId))
        {
            string? existing = _model.Get(owner.ToString(CultureInfo.InvariantCulture))?.Ref(field);
            if (existing != null && int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                arrayId = parsed;
                // Only seed the running count the first time this underlying array is
                // seen. A second owner that shares the same transition-array object must
                // not reset a counter we have already advanced, or its transitions would
                // be planned at indices that collide with the first owner's.
                if (!_transitionCounts.ContainsKey(arrayId))
                {
                    var array = _model.Get(existing);
                    _transitionCounts[arrayId] = array != null && array.StructLists.TryGetValue("transitions", out var rows)
                        ? rows.Count
                        : 0;
                }
            }
            else
            {
                var array = _plan.AddObject("hkbStateMachineTransitionInfoArray");
                arrayId = array.Id;
                _transitionCounts[arrayId] = 0;
                _plan.SetReference(owner, field, arrayId);
            }
            _transitionArrays[key] = arrayId;
        }

        int index = _transitionCounts[arrayId];
        _transitionCounts[arrayId] = index + 1;
        _plan.ResizeStructArray(arrayId, "transitions", index + 1);

        SetInterval(arrayId, index, "triggerInterval");
        SetInterval(arrayId, index, "initiateInterval");
        _plan.SetStructMember(arrayId, "transitions", index, "transition",
            effectId.HasValue ? "#" + effectId.Value.ToString(CultureInfo.InvariantCulture) : "null");
        _plan.SetStructMember(arrayId, "transitions", index, "condition", "null");
        _plan.SetStructMember(arrayId, "transitions", index, "eventId", eventId.ToString(CultureInfo.InvariantCulture));
        _plan.SetStructMember(arrayId, "transitions", index, "toStateId",
            _stateIds[toStateObjectId].ToString(CultureInfo.InvariantCulture));
        _plan.SetStructMember(arrayId, "transitions", index, "fromNestedStateId", "0");
        _plan.SetStructMember(arrayId, "transitions", index, "toNestedStateId", "0");
        _plan.SetStructMember(arrayId, "transitions", index, "priority", "0");
        _plan.SetStructMember(arrayId, "transitions", index, "flags", "0");
        return new TransitionRef(arrayId, index);
    }

    public NativeAuthoringPlan.Result Build() => _plan.Apply();

    private List<int> EnsureMachine(int machineId)
    {
        if (_plan.ClassOf(machineId) != "hkbStateMachine")
            throw new ArgumentException($"#{machineId} is not an hkbStateMachine", nameof(machineId));

        if (_statesByMachine.TryGetValue(machineId, out var cached)) return cached;

        var machine = _model.Get(machineId.ToString(CultureInfo.InvariantCulture))
            ?? throw new ArgumentException($"#{machineId} is not in the source graph", nameof(machineId));
        var states = machine.Refs("states")
            .Select(id => int.Parse(id, CultureInfo.InvariantCulture))
            .ToList();

        foreach (int stateObjectId in states)
        {
            var state = _model.Get(stateObjectId.ToString(CultureInfo.InvariantCulture));
            if (state == null || state.Class != "hkbStateMachineStateInfo")
                throw new InvalidOperationException(
                    $"#{machineId}.states contains #{stateObjectId}, which is not an hkbStateMachineStateInfo");
            _stateIds[stateObjectId] = state.Int("stateId");
        }

        _statesByMachine[machineId] = states;
        return states;
    }

    private void SetInterval(int arrayId, int index, string interval)
    {
        _plan.SetStructMember(arrayId, "transitions", index, interval + ".enterEventId", "-1");
        _plan.SetStructMember(arrayId, "transitions", index, interval + ".exitEventId", "-1");
        _plan.SetStructMember(arrayId, "transitions", index, interval + ".enterTime", "0");
        _plan.SetStructMember(arrayId, "transitions", index, interval + ".exitTime", "0");
    }
}

public static class BatchAnimationBuilder
{
    public sealed record Entry(string Name, string AnimationName, int BindingIndex = -1, float PlaybackSpeed = 1.0f);

    public sealed record Created(Entry Entry, NativeAuthoringPlan.ObjectRef Clip,
                                 BehaviourAuthoringSession.StateRef State);

    public sealed record Result(byte[] Bytes, IReadOnlyList<Created> Created,
                                IReadOnlyList<GraphValidator.Finding> Findings);

    public static Result Build(byte[] source, int stateMachineId, IEnumerable<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var requested = entries.ToList();
        if (requested.Count == 0) throw new ArgumentException("at least one animation is required", nameof(entries));
        if (requested.Any(e => string.IsNullOrWhiteSpace(e.Name) || string.IsNullOrWhiteSpace(e.AnimationName)))
            throw new ArgumentException("every animation needs a state name and animation name", nameof(entries));

        var duplicate = requested.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
            throw new ArgumentException($"state name '{duplicate.Key}' appears more than once", nameof(entries));

        var session = new BehaviourAuthoringSession(source);
        var created = new List<Created>(requested.Count);
        foreach (var entry in requested)
        {
            var clip = session.AddClip(entry.Name, entry.AnimationName, entry.BindingIndex, entry.PlaybackSpeed);
            var state = session.AddState(stateMachineId, entry.Name, clip.Id);
            created.Add(new Created(entry, clip, state));
        }

        var result = session.Build();
        return new Result(result.Bytes, created, result.Findings);
    }
}