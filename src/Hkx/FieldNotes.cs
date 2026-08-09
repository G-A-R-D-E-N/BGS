using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What to say about a field when somebody hovers over its name.
//
// Two different kinds of thing, kept apart on purpose, because one of them is a fact about the file
// and the other is a claim about the game.
//
// **What a field is** comes out of the class table and is true by construction: its type, what an
// array holds, what class an inline struct is, how many values an enum declares. Nothing is invented
// and every field gets one.
//
// **What a field means** is a sentence, and no installed reference establishes that meaning for most
// fields. The alternative would be writing plausible sentences from the field names. A plausible sentence
// about a field nobody has checked is worse than silence: it reads with the same authority as the
// ones that were established, and there is no way for a reader to tell which is which.
//
// So the sentences here are only the ones this project has established itself, each with where it
// came from. That is a short list and it is meant to grow one finding at a time, not in a sweep.
public static class FieldNotes
{
    /// A sentence about what a field means, and where that was established. Null for the great
    /// majority of fields, which is the honest answer.
    public sealed record Note(string Says, string From)
    {
        public override string ToString() => $"{Says}  ({From})";
    }

    /// Keyed by the class that declares the field and the field's name, because two classes can both
    /// declare a `flags` and mean different things by it.
    private static readonly Dictionary<string, Note> Known = new(StringComparer.Ordinal)
    {
        ["hkbClipGenerator.mode"] = new(
            "How the clip is played. MODE_USER_CONTROLLED does not play it at all: the clip is " +
            "sampled at whatever point userControlledTimeFraction names, which is how a gauge " +
            "needle or a dial is driven.",
            "the Pip-Boy's rad meter and radio tuner, wiki: Pip-Boy Variables"),

        ["hkbClipGenerator.userControlledTimeFraction"] = new(
            "Where in the clip to sit, from 0 at the start to 1 at the end. Only used when mode is " +
            "MODE_USER_CONTROLLED. Bind it to a float variable and the variable drives the pose.",
            "the Pip-Boy's rad meter and radio tuner, wiki: Pip-Boy Variables"),

        ["hkbClipGenerator.animationName"] = new(
            "The .hkt this clip plays, as a path relative to the character's folder. The Chain tab " +
            "resolves it and says whether the file is actually there.",
            "ProjectChain, checked against every clip in the 531 vanilla behaviours"),

        ["hkbStateMachineTransitionInfo.eventId"] = new(
            "The event that takes this transition. It is an index into the file's own event list, " +
            "not a name, so it means nothing outside this file.",
            "StateRoutes, 6,394 transitions across the vanilla corpus, none dangling"),

        ["hkbStateMachineTransitionInfo.toStateId"] = new(
            "The stateId of the state this transition enters, which is not an object id and not a " +
            "position in the states array.",
            "StateRoutes, every transition in the corpus resolves to a real state of its own machine"),

        ["hkbStateMachineTransitionInfo.flags"] = new(
            "A combination of bits rather than one value, which is why it shows as a number when no " +
            "single declared name covers it. FLAG_IS_GLOBAL_WILDCARD and FLAG_IS_LOCAL_WILDCARD mark " +
            "a transition that can be taken from any state, which the canvas draws on the state it " +
            "enters rather than as a line from everywhere.",
            "measured on the vanilla corpus, and drawn that way since MR !48"),

        ["hkbStateMachineStateInfo.stateId"] = new(
            "What transitions call this state. Unrelated to the object's own id and to its position " +
            "in the machine's states array.",
            "StateRoutes"),

        ["hkbStateMachine.startStateId"] = new(
            "The stateId the machine begins in. Check graph reports an error when it names no state " +
            "of this machine.",
            "GraphValidator"),

        ["hkbBehaviorReferenceGenerator.behaviorName"] = new(
            "Another behaviour file, loaded in place of a generator. Anything walking the graph stops " +
            "here unless it opens that file too.",
            "noted while drawing the graph, issue #37"),

        ["hkbVariableBounds.min"] = new(
            "The low end of a variable's allowed range. The struct is eight bytes with min at offset " +
            "0 and max at offset 4, read out of the game's own class registration rather than guessed.",
            "_dynamic_initializer_for__hkbVariableBoundsClass__, issue #17"),

        ["hkbVariableBounds.max"] = new(
            "The high end of a variable's allowed range, at offset 4 of an eight byte struct.",
            "_dynamic_initializer_for__hkbVariableBoundsClass__, issue #17"),
    };

    /// What a field means, if this project has established it. Null otherwise, and null is the
    /// common answer.
    public static Note? Meaning(string owningClass, string field) =>
        Known.TryGetValue($"{owningClass}.{field}", out var note) ? note : null;

    /// What a field is, from the class table. Always answers when the class is one we describe.
    ///
    /// This is deliberately dull. It says the shape of the thing and nothing about its purpose,
    /// because the shape is a fact and the purpose is not.
    public static string? Structure(string owningClass, string field, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        var members = types.Members(owningClass);
        var member = members.FirstOrDefault(m => m.Name == field);

        // A fixed length C array is one member and eight fields. `hkbFootIkControlData` holds
        // `enabled` as eight bools and the file writes them as `enabled1` to `enabled8`, so looking
        // the shown name up in the member list finds nothing and the field gets no description at
        // all. 88 fields in the corpus were in that state.
        int place = 0;
        if (member == null)
        {
            string bare = field.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (bare.Length == field.Length || bare.Length == 0) return null;

            member = members.FirstOrDefault(m => m.Name == bare && m.ArrSize > 1);
            if (member == null) return null;

            int.TryParse(field[bare.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out place);
            field = bare;
        }

        string what = place > 0
            ? $"number {place} of {member.ArrSize} written out side by side as {member.Name}1 to " +
              $"{member.Name}{member.ArrSize}"
            : Shape(member, types, owningClass);

        string declared = Declares(owningClass, field, types);

        return declared.Length > 0 && declared != owningClass
            ? $"{what}, declared by {declared}"
            : what;
    }

    /// Which class in the chain actually declares the field, since most of what an object holds is
    /// inherited and knowing where a field comes from is half of knowing what it is for.
    private static string Declares(string owningClass, string field, HavokClassTypes types)
    {
        for (string? at = owningClass; at != null; at = types[at]?.Parent)
            if (types[at]?.Declared.Any(m => m.Name == field) == true)
                return at;

        return "";
    }

    private static string Shape(HavokClassTypes.Member member, HavokClassTypes types, string owner)
    {
        if (member.ArrSize > 1) return $"one of {member.ArrSize} numbers written out side by side";

        switch (member.VType)
        {
            case "TYPE_POINTER":
                return member.CType is { Length: > 0 } to
                    ? $"a pointer to a {to}"
                    : "a pointer to another object";

            case "TYPE_STRINGPTR":
            case "TYPE_CSTRING":
                return "a name, held as text";

            case "TYPE_STRUCT":
                return member.CType is { Length: > 0 } inline
                    ? $"a {inline} written inside this object"
                    : "a struct written inside this object";

            case "TYPE_ENUM":
            case "TYPE_FLAGS":
            {
                var values = member.EType == null ? null : types.Enum(owner, member.EType);
                if (values == null || values.Count == 0) return "a named value";

                return member.VType == "TYPE_FLAGS"
                    ? $"a combination of {values.Count} declared flags"
                    : $"one of {values.Count} declared values";
            }

            case "TYPE_ARRAY":
            case "TYPE_SIMPLEARRAY":
            case "TYPE_RELARRAY":
                return "an array of " + Holds(member, types);

            case "TYPE_BOOL": return "true or false";
            case "TYPE_REAL": return "a number with a decimal point";
            case "TYPE_VECTOR4": return "four numbers, written in brackets";
            case "TYPE_QUATERNION": return "a rotation, four numbers in brackets";
            case "TYPE_QSTRANSFORM": return "a position, rotation and scale, twelve numbers in three brackets";

            case "TYPE_INT8":
            case "TYPE_UINT8":
            case "TYPE_INT16":
            case "TYPE_UINT16":
            case "TYPE_INT32":
            case "TYPE_UINT32":
            case "TYPE_INT64":
            case "TYPE_UINT64":
            case "TYPE_ULONG":
            case "TYPE_CHAR":
                return "a whole number";

            default:
                return "a " + member.VType.Replace("TYPE_", "").ToLower(CultureInfo.InvariantCulture);
        }
    }

    private static string Holds(HavokClassTypes.Member member, HavokClassTypes types) => member.VSub switch
    {
        "TYPE_POINTER" => member.CType is { Length: > 0 } to ? $"pointers to {to} objects" : "pointers",
        "TYPE_STRINGPTR" or "TYPE_CSTRING" => "names",
        "TYPE_STRUCT" => member.CType is { Length: > 0 } of ? $"{of} elements" : "structs",
        "TYPE_REAL" => "numbers with decimal points",
        "TYPE_VECTOR4" => "vectors",
        "" or "TYPE_VOID" => "values",
        _ => "whole numbers",
    };
}
