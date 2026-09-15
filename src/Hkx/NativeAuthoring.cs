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

        // Shrinking drops the elements past the new end, so any member write already planned
        // for one of them has nowhere to land. Leaving it planned makes the whole save refuse.
        _changes.RemoveAll(c => c.Id == id && c.Field == field && c.InElement && c.Element >= count);
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
    // The source model is the file as opened, so it knows nothing about objects this session
    // has planned. Clone and remove have to see those too, or they only work on states that
    // were already on disk.
    private readonly Dictionary<int, CreatedState> _created = new();
    private readonly Dictionary<int, List<Dictionary<string, string>>> _rows = new();
    private List<string>? _events;

    private sealed record CreatedState(int GeneratorId, float Probability, bool Enable);

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
        _created[state.Id] = new CreatedState(generatorId, 1.0f, true);
        _plan.SetPointerArray(machineId, "states", states);
        return new StateRef(state.Id, stateId);
    }

    public NativeAuthoringPlan.ObjectRef AddStateMachine(string name, string firstStateName, int generatorId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("state machine name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(firstStateName))
            throw new ArgumentException("first state name is required", nameof(firstStateName));
        _plan.RequireAssignable(generatorId, "hkbGenerator", "state machine state generator");

        var machine = _plan.AddObject("hkbStateMachine");
        _plan.SetString(machine.Id, "name", name);
        _plan.SetStructMember(machine.Id, "eventToSendWhenStateOrTransitionChanges", 0, "id", "-1");
        _plan.SetStructMember(machine.Id, "eventToSendWhenStateOrTransitionChanges", 0, "payload", "null");
        _plan.SetReference(machine.Id, "startStateIdSelector", null);
        _plan.SetInt(machine.Id, "startStateId", 0);
        _plan.SetInt(machine.Id, "returnToPreviousStateEventId", -1);
        _plan.SetInt(machine.Id, "randomTransitionEventId", -1);
        _plan.SetInt(machine.Id, "transitionToNextHigherStateEventId", -1);
        _plan.SetInt(machine.Id, "transitionToNextLowerStateEventId", -1);
        _plan.SetInt(machine.Id, "syncVariableIndex", -1);
        _plan.SetBool(machine.Id, "wrapAroundStateId", true);
        _plan.SetInt(machine.Id, "maxSimultaneousTransitions", 32);
        _plan.SetEnum(machine.Id, "startStateMode", "START_STATE_MODE_DEFAULT");
        _plan.SetEnum(machine.Id, "selfTransitionMode", "SELF_TRANSITION_MODE_FORCE_TRANSITION_TO_START_STATE");
        _plan.SetPointerArray(machine.Id, "states", Array.Empty<int>());
        _plan.SetReference(machine.Id, "wildcardTransitions", null);
        _statesByMachine[machine.Id] = new List<int>();
        AddState(machine.Id, firstStateName, generatorId);
        return machine;
    }

    public void AttachGenerator(int parentId, string field, int generatorId)
    {
        if (string.IsNullOrWhiteSpace(field)) throw new ArgumentException("generator field is required", nameof(field));
        _plan.RequireAssignable(generatorId, "hkbGenerator", "generator attachment");
        _plan.SetReference(parentId, field, generatorId);
    }

    public NativeAuthoringPlan.ObjectRef AddExpressionCondition(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("condition expression is required", nameof(expression));
        var parsed = Expression.Parse(expression);
        if (!parsed.Ok || parsed.IsAssignment)
            throw new ArgumentException(parsed.Problem ?? "a transition condition must be a predicate", nameof(expression));

        var condition = _plan.AddObject("hkbExpressionCondition");
        _plan.SetString(condition.Id, "expression", expression);
        return condition;
    }

    public void SetTransitionCondition(int transitionArrayId, int index, int? conditionId)
    {
        var rows = Rows(transitionArrayId);
        if (index < 0 || index >= rows.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (conditionId is int condition)
            _plan.RequireAssignable(condition, "hkbCondition", "transition condition");

        string value = conditionId is int id
            ? "#" + id.ToString(CultureInfo.InvariantCulture)
            : "null";
        rows[index]["condition"] = value;
        _plan.SetStructMember(transitionArrayId, "transitions", index, "condition", value);
    }

    public enum NotifyPhase { Enter, Exit }

    public int AddNotifyEvent(int stateObjectId, NotifyPhase phase, int eventId)
    {
        ValidateEventId(eventId);
        int arrayId = NotifyArray(stateObjectId, phase, create: true);
        var rows = NotifyRows(arrayId);
        int index = rows.Count;
        rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = eventId.ToString(CultureInfo.InvariantCulture),
            ["payload"] = "null",
        });
        _plan.ResizeStructArray(arrayId, "events", index + 1);
        foreach (var (member, value) in rows[index])
            _plan.SetStructMember(arrayId, "events", index, member, value);
        return index;
    }

    public int RemoveNotifyEvent(int stateObjectId, NotifyPhase phase, int index)
    {
        int arrayId = NotifyArray(stateObjectId, phase, create: false);
        var rows = NotifyRows(arrayId);
        if (index < 0 || index >= rows.Count) throw new ArgumentOutOfRangeException(nameof(index));

        int removed = int.Parse(rows[index]["id"], CultureInfo.InvariantCulture);
        rows.RemoveAt(index);
        for (int i = 0; i < rows.Count; i++)
            foreach (var (member, value) in rows[i])
                _plan.SetStructMember(arrayId, "events", i, member, value);
        _plan.ResizeStructArray(arrayId, "events", rows.Count);
        return removed;
    }

    private readonly Dictionary<(int State, NotifyPhase Phase), int> _notifyArrays = new();
    private readonly Dictionary<int, List<Dictionary<string, string>>> _notifyRows = new();

    private int NotifyArray(int stateObjectId, NotifyPhase phase, bool create)
    {
        if (_plan.ClassOf(stateObjectId) != "hkbStateMachineStateInfo")
            throw new ArgumentException($"#{stateObjectId} is not a state info object", nameof(stateObjectId));

        string field = phase == NotifyPhase.Enter ? "enterNotifyEvents" : "exitNotifyEvents";
        var key = (stateObjectId, phase);
        if (_notifyArrays.TryGetValue(key, out int cached)) return cached;

        string? existing = _model.Get(stateObjectId.ToString(CultureInfo.InvariantCulture))?.Ref(field);
        if (existing != null && int.TryParse(existing, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            _notifyArrays[key] = parsed;
            if (!_notifyRows.ContainsKey(parsed)) _notifyRows[parsed] = ReadNotifyRows(parsed);
            return parsed;
        }
        if (!create) throw new InvalidOperationException($"#{stateObjectId}.{field} is null");

        var array = _plan.AddObject("hkbStateMachineEventPropertyArray");
        _plan.ResizeStructArray(array.Id, "events", 0);
        _plan.SetReference(stateObjectId, field, array.Id);
        _notifyArrays[key] = array.Id;
        _notifyRows[array.Id] = new List<Dictionary<string, string>>();
        return array.Id;
    }

    private List<Dictionary<string, string>> NotifyRows(int arrayId) =>
        _notifyRows.TryGetValue(arrayId, out var rows)
            ? rows
            : throw new InvalidOperationException($"#{arrayId} has no event-property rows");

    private List<Dictionary<string, string>> ReadNotifyRows(int arrayId)
    {
        var array = _model.Get(arrayId.ToString(CultureInfo.InvariantCulture));
        if (array == null || array.Class != "hkbStateMachineEventPropertyArray" ||
            !array.StructLists.TryGetValue("events", out var rows))
            throw new InvalidOperationException($"#{arrayId} is not a readable event-property array");

        foreach (var row in rows)
        {
            if (!row.TryGetValue("id", out var value) || !int.TryParse(value, out _))
                throw new InvalidOperationException($"#{arrayId}.events contains an unreadable event id");
            if (!row.TryGetValue("payload", out var payload))
                throw new InvalidOperationException($"#{arrayId}.events contains an unreadable payload");
            if (payload != "null")
            {
                if (payload.Length <= 1 || payload[0] != '#' ||
                    !int.TryParse(payload[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int payloadId) ||
                    !_plan.Contains(payloadId))
                    throw new InvalidOperationException($"#{arrayId}.events contains an unreadable payload");
                _plan.RequireAssignable(payloadId, "hkbEventPayload", $"#{arrayId}.events.payload");
            }
        }
        return rows.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList();
    }

    private void ValidateEventId(int eventId)
    {
        if (eventId < 0) throw new ArgumentOutOfRangeException(nameof(eventId));
        int count = (_events ?? SymbolEditor.EventNames(_model)).Count;
        if (eventId >= count)
            throw new ArgumentOutOfRangeException(nameof(eventId),
                $"event {eventId} is not declared; this graph currently has {count} event(s)");
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
                                       int eventId, int? effectId = null, int? conditionId = null)
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
        if (conditionId.HasValue)
            _plan.RequireAssignable(conditionId.Value, "hkbCondition", "transition condition");

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
        Record(arrayId, index, "transition",
            effectId.HasValue ? "#" + effectId.Value.ToString(CultureInfo.InvariantCulture) : "null");
        Record(arrayId, index, "condition", conditionId.HasValue
            ? "#" + conditionId.Value.ToString(CultureInfo.InvariantCulture)
            : "null");
        Record(arrayId, index, "eventId", eventId.ToString(CultureInfo.InvariantCulture));
        Record(arrayId, index, "toStateId", _stateIds[toStateObjectId].ToString(CultureInfo.InvariantCulture));
        Record(arrayId, index, "fromNestedStateId", "0");
        Record(arrayId, index, "toNestedStateId", "0");
        Record(arrayId, index, "priority", "0");
        Record(arrayId, index, "flags", "0");
        return new TransitionRef(arrayId, index);
    }

    // A selector picks one of several generators by index. Adding one natively means the
    // batch and editor paths can build a choice between clips without an XML round trip.
    public NativeAuthoringPlan.ObjectRef AddManualSelector(string name, IEnumerable<int> generatorIds,
                                                           int selectedIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("selector name is required", nameof(name));

        var ids = (generatorIds ?? throw new ArgumentNullException(nameof(generatorIds))).ToList();
        if (ids.Count == 0)
            throw new ArgumentException("a selector needs at least one generator", nameof(generatorIds));
        foreach (int id in ids) _plan.RequireAssignable(id, "hkbGenerator", "selector generator");
        if (selectedIndex < 0 || selectedIndex >= ids.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex),
                $"a selector over {ids.Count} generator(s) cannot start at index {selectedIndex}");

        var selector = _plan.AddObject("hkbManualSelectorGenerator");
        _plan.SetString(selector.Id, "name", name);
        _plan.SetPointerArray(selector.Id, "generators", ids);
        _plan.SetInt(selector.Id, "selectedGeneratorIndex", selectedIndex);
        _plan.SetReference(selector.Id, "indexSelector", null);
        _plan.SetBool(selector.Id, "selectedIndexCanChangeAfterActivate", false);
        _plan.SetReference(selector.Id, "generatorChangedTransitionEffect", null);
        return selector;
    }

    // Plays another behaviour graph by name. The pointer to the loaded graph is filled in by
    // the runtime, so only the name is written.
    public NativeAuthoringPlan.ObjectRef AddBehaviorReference(string name, string behaviorName)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));
        if (string.IsNullOrWhiteSpace(behaviorName))
            throw new ArgumentException("behaviour name is required", nameof(behaviorName));

        var reference = _plan.AddObject("hkbBehaviorReferenceGenerator");
        _plan.SetString(reference.Id, "name", name);
        _plan.SetString(reference.Id, "behaviorName", behaviorName);
        return reference;
    }

    // Another state on the same machine driving the same generator. The generator is shared
    // rather than copied: duplicating the subtree is a paste, not a clone.
    public StateRef CloneState(int machineId, int stateObjectId, string name)
    {
        var states = EnsureMachine(machineId);
        if (!states.Contains(stateObjectId))
            throw new ArgumentException($"#{stateObjectId} is not a state of #{machineId}", nameof(stateObjectId));

        var was = Described(stateObjectId)
            ?? throw new InvalidOperationException($"#{stateObjectId} has no generator to clone");

        var clone = AddState(machineId, name, was.GeneratorId);
        _plan.SetReal(clone.ObjectId, "probability", was.Probability);
        _plan.SetBool(clone.ObjectId, "enable", was.Enable);
        _created[clone.ObjectId] = was with { };
        return clone;
    }

    // Takes a state off its machine and clears what pointed at it. A transition naming the
    // removed state is dropped rather than left aiming at a state id nothing answers to,
    // which is the failure the graph validator would otherwise only find later.
    public sealed record Removal(int StateObjectId, int StateId, int TransitionsDropped);

    public Removal RemoveState(int machineId, int stateObjectId)
    {
        var states = EnsureMachine(machineId);
        if (!states.Contains(stateObjectId))
            throw new ArgumentException($"#{stateObjectId} is not a state of #{machineId}", nameof(stateObjectId));
        if (states.Count == 1)
            throw new InvalidOperationException(
                $"#{stateObjectId} is the only state of #{machineId}; a state machine with no states cannot start");

        int stateId = _stateIds[stateObjectId];
        states.Remove(stateObjectId);
        _plan.SetPointerArray(machineId, "states", states);

        int dropped = 0;
        foreach (int owner in states.Append(machineId))
        {
            string field = owner == machineId ? "wildcardTransitions" : "transitions";
            dropped += DropTransitionsTo(owner, field, stateId);
        }

        if (_model.Get(machineId.ToString(CultureInfo.InvariantCulture))?.Int("startStateId") == stateId)
            _plan.SetInt(machineId, "startStateId", _stateIds[states[0]]);

        return new Removal(stateObjectId, stateId, dropped);
    }

    private CreatedState? Described(int stateObjectId)
    {
        if (_created.TryGetValue(stateObjectId, out var created)) return created;

        var state = _model.Get(stateObjectId.ToString(CultureInfo.InvariantCulture));
        string? generator = state?.Ref("generator");
        if (generator == null ||
            !int.TryParse(generator, NumberStyles.Integer, CultureInfo.InvariantCulture, out int generatorId))
            return null;

        float probability = float.TryParse(state!.Str("probability"), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out float read) && read >= 0
            ? read : 1.0f;
        return new CreatedState(generatorId, probability,
                                !bool.TryParse(state.Str("enable"), out bool enable) || enable);
    }

    private int DropTransitionsTo(int owner, string field, int stateId)
    {
        int? arrayId = TransitionArray(owner, field);
        if (arrayId == null) return 0;

        var rows = Rows(arrayId.Value);
        if (rows.Count == 0) return 0;

        string target = stateId.ToString(CultureInfo.InvariantCulture);
        var kept = rows.Where(row => !row.TryGetValue("toStateId", out string? to) || to != target).ToList();
        if (kept.Count == rows.Count) return 0;

        for (int i = 0; i < kept.Count; i++)
            foreach (var (member, value) in kept[i])
                _plan.SetStructMember(arrayId.Value, "transitions", i, member, value);

        _plan.ResizeStructArray(arrayId.Value, "transitions", kept.Count);
        _transitionCounts[arrayId.Value] = kept.Count;
        _rows[arrayId.Value] = kept;
        return rows.Count - kept.Count;
    }

    private int? TransitionArray(int owner, string field)
    {
        if (_transitionArrays.TryGetValue((owner, field), out int planned)) return planned;

        string? reference = _model.Get(owner.ToString(CultureInfo.InvariantCulture))?.Ref(field);
        return reference != null &&
               int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out int existing)
            ? existing
            : null;
    }

    // What the array will hold once this session is applied: the rows it came with, with the
    // ones this session added on the end.
    private List<Dictionary<string, string>> Rows(int arrayId)
    {
        if (_rows.TryGetValue(arrayId, out var known)) return known;

        var array = _model.Get(arrayId.ToString(CultureInfo.InvariantCulture));
        var rows = array != null && array.StructLists.TryGetValue("transitions", out var existing)
            ? existing.Select(row => new Dictionary<string, string>(row, StringComparer.Ordinal)).ToList()
            : new List<Dictionary<string, string>>();
        _rows[arrayId] = rows;
        return rows;
    }

    private void Record(int arrayId, int index, string member, string value)
    {
        var rows = Rows(arrayId);
        while (rows.Count <= index) rows.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        rows[index][member] = value;
        _plan.SetStructMember(arrayId, "transitions", index, member, value);
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
        Record(arrayId, index, interval + ".enterEventId", "-1");
        Record(arrayId, index, interval + ".exitEventId", "-1");
        Record(arrayId, index, interval + ".enterTime", "0");
        Record(arrayId, index, interval + ".exitTime", "0");
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
