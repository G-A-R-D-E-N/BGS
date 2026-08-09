using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// How long a clip plays for, and when the events it carries go out.
//
// This is the half of running the graph that the behaviour file cannot answer on its own. A state
// leaves on an event, and one of the things that raises an event is the clip the state is playing
// reaching a point in itself. The point is in the behaviour; the length it is measured against is in
// the animation file, which is a different file, found through the project root rather than beside
// the behaviour.
//
// The mechanism is worth stating plainly because it is not the obvious one. Nothing in a state
// machine says "leave when the clip ends". A clip carries a trigger array, and a trigger marked
// `relativeToEndOfClip` fires at a point measured back from the end. Its event then moves a state the
// same way an event sent by hand does. So clip length does not add a new kind of transition; it adds
// a new sender of ordinary events.
//
// Counted over the 531 vanilla behaviours: 3,740 clips, 1,891 of them carrying a trigger array, 2,886
// triggers of which 2,148 are relative to the end. 1,326 of the events those triggers raise are
// listened for by a transition in the same file, 1,191 of them at the end of a clip. Only 40 clips
// set `enforcedDuration`, so for almost all of them the length genuinely has to be read off disk.
public static class ClipTiming
{
    /// One trigger, with the time it goes out resolved against the clip's length.
    ///
    /// `At` is in clip local seconds from the start. A trigger relative to the end is stored with a
    /// localTime measured back from the end, so resolving it is the only thing the length is needed
    /// for, and it is the reason a clip with no length has no usable triggers rather than merely
    /// approximate ones.
    public sealed record Trigger(float At, string Event, bool RelativeToEnd, bool Acyclic,
                                float LocalTime = 0)
    {
        public override string ToString() =>
            $"'{Event}' at {At:F3}s" + (RelativeToEnd ? " (from the end)" : "");
    }

    /// A clip's timing, or the reason there is none.
    ///
    /// `Seconds` is the length after cropping and playback speed, which is what a clock has to count
    /// against rather than the raw animation length. Zero with a `Why` is the honest unknown: the
    /// stepper records it as a stop rather than assuming a length and inventing the events that would
    /// follow from it.
    public sealed record Clip(string ClipId, string Name, string Animation, float Seconds,
                              IReadOnlyList<Trigger> Triggers, string Mode = "", string Why = "")
    {
        public bool Known => Seconds > 0;

        /// Whether reaching the end sends the clip back to its start.
        ///
        /// Ping pong also carries on rather than stopping, and for the purpose of a clock that only
        /// asks "does this keep going", turning round at the end and starting again are the same
        /// answer. Where they differ is which frame is showing, and nothing here draws one.
        public bool Looping => Mode is "MODE_LOOPING" or "MODE_PING_PONG";

        public override string ToString() =>
            Known ? $"'{Name}' lasts {Seconds:F3}s with {Triggers.Count} trigger(s)"
                  : $"'{Name}' has no length: {Why}";
    }

    /// Where an animation named by a clip is read from.
    ///
    /// A delegate rather than a path, because the two callers want different things from it: the
    /// window has a file open and a project root around it, and the checks want to answer from a table
    /// without touching a disk. Returning zero means the length is not available, which is a different
    /// answer from a length of zero.
    public delegate float LengthOf(string animationName);

    /// Every clip in the graph, timed.
    ///
    /// Keyed by object id so a stepper can ask about whichever clip the state it is in resolves to.
    public static Dictionary<string, Clip> All(PackfileObjects objects, IReadOnlyList<string> events,
                                               LengthOf lengthOf)
    {
        var timed = new Dictionary<string, Clip>(StringComparer.Ordinal);

        foreach (var clip in objects.OfClass("hkbClipGenerator"))
        {
            var one = Of(objects, clip, events, lengthOf);
            if (one != null) timed[one.ClipId] = one;
        }

        return timed;
    }

    /// One clip, timed. Null when the object is not a clip generator.
    public static Clip? Of(PackfileObjects objects, PackfileObjects.Instance clip,
                           IReadOnlyList<string> events, LengthOf lengthOf)
    {
        if (clip.ClassName != "hkbClipGenerator") return null;

        // The same id the model gives the object, so a stepper holding a model can look a clip up by
        // the id it already has. Numbering them from scratch here would produce a table keyed by
        // something nothing else in the tool uses.
        string id = (NativeGraphModel.FirstId + objects.IndexOf(clip)).ToString();
        string name = objects.ReadString(clip, "name") ?? "";
        string animation = objects.ReadString(clip, "animationName") ?? "";

        float seconds = Seconds(objects, clip, animation, lengthOf, out string why);
        var triggers = Triggers(objects, clip, events, seconds);

        // Named from the class table's own enum rather than compared against a number written here,
        // so a mode nobody has thought about reads as its name instead of as "not looping".
        int mode = (objects.ReadIntAt(clip.Offset + 190) ?? 0) & 0xff;
        string named = HavokClassTypes.Shipped.Enum("hkbClipGenerator", "PlaybackMode")
                                              ?.FirstOrDefault(v => v.Value == mode).Key ?? "";

        return new Clip(id, name, animation, seconds, triggers, named, why);
    }

    /// How long the clip plays for, after everything the behaviour does to the animation's own length.
    ///
    /// `enforcedDuration` wins outright when it is set, which is the one case needing no animation at
    /// all, and it is set on 40 of the corpus's 3,740 clips. Otherwise the animation's length is
    /// cropped at both ends and then divided by the playback speed, because speed changes how long the
    /// clip occupies rather than how much of the animation is used.
    ///
    /// A speed of zero is a clip parked on a frame rather than one of zero length, so it has no
    /// finish and is reported as having no length rather than as ending immediately. Reading it the
    /// other way would fire every one of its end triggers on the first step.
    public static float Seconds(PackfileObjects objects, PackfileObjects.Instance clip,
                                string animation, LengthOf lengthOf, out string why) =>
        Span(objects.ReadFloat(clip, "enforcedDuration") ?? 0,
             animation.Length == 0 ? -1 : lengthOf(animation),
             objects.ReadFloat(clip, "cropStartAmountLocalTime") ?? 0,
             objects.ReadFloat(clip, "cropEndAmountLocalTime") ?? 0,
             objects.ReadFloat(clip, "playbackSpeed") ?? 1,
             animation, out why);

    /// The arithmetic on its own, with nothing read out of a file.
    ///
    /// Separate from `Seconds` so it can be checked against answers worked out by hand. The corpus
    /// cannot check this: 199 of its 3,740 clips crop and 200 play at a speed other than one, so the
    /// combinations it never ships, a crop and a speed together among them, are exactly the ones a
    /// sweep would call correct without ever evaluating.
    ///
    /// A negative `animation` means the clip named none, which is a different answer from an
    /// animation that was looked for and not found.
    public static float Span(float enforced, float animation, float cropStart, float cropEnd,
                             float speed, string named, out string why)
    {
        why = "";

        // An enforced duration wins outright and needs no animation at all, which is the one case
        // where a missing file costs nothing. 40 of the corpus's clips set one.
        if (enforced > 0) return enforced;

        if (animation < 0)
        {
            why = "the clip names no animation";
            return 0;
        }

        if (animation <= 0)
        {
            why = $"the animation '{named}' was not found, so its length is unknown";
            return 0;
        }

        float cropped = animation - cropStart - cropEnd;
        if (cropped <= 0)
        {
            why = "the crop at each end removes the whole animation";
            return 0;
        }

        // Speed changes how long the clip occupies rather than how much of the animation is used, so
        // it divides. A negative speed plays the same span backwards, so its size is what counts.
        //
        // Zero is a clip parked on a frame rather than one of zero length, so it has no finish and is
        // reported as having no length. Reading it the other way would fire every end trigger it
        // carries on the first step, which is the opposite of what a parked clip does.
        if (speed == 0)
        {
            why = "the clip plays at zero speed, so it is parked on a frame and never finishes";
            return 0;
        }

        return cropped / Math.Abs(speed);
    }

    /// The clip's triggers with their times resolved, in the order they go out.
    ///
    /// Empty when the length is unknown, and that is the point rather than a shortcut: a trigger
    /// relative to the end has no time without one, and half a trigger list would fire the absolute
    /// ones and silently drop every "when this finishes" the clip carries, which is the majority of
    /// them.
    public static List<Trigger> Triggers(PackfileObjects objects, PackfileObjects.Instance clip,
                                         IReadOnlyList<string> events, float seconds)
    {
        var found = new List<Trigger>();
        if (seconds <= 0) return found;

        var array = objects.ReadRef(clip, "triggers", out _);
        if (array == null) return found;

        var elements = objects.ReadArray(array, "triggers");
        if (elements == null || elements.Count == 0) return found;

        int stride = HavokClassTypes.Shipped["hkbClipTrigger"]?.Size ?? 32;

        for (int i = 0; i < elements.Count; i++)
        {
            int at = elements.At + i * stride;

            float local = objects.ReadFloatAt(at) ?? 0;
            int id = objects.ReadIntAt(at + 8) ?? -1;
            bool end = Flag(objects, at + 24);
            bool acyclic = Flag(objects, at + 25);
            bool annotation = Flag(objects, at + 26);

            // An annotation is a marker for tools rather than something the graph listens to, so it
            // is read and dropped rather than raised. None of the corpus's 2,886 triggers is one,
            // which is why this costs nothing and is still worth keeping: a file that had one would
            // otherwise send an event the game does not.
            if (annotation) continue;

            string name = id >= 0 && id < events.Count ? events[id] : "";
            if (name.Length == 0) continue;

            float when = TriggerAt(local, end, seconds);
            if (when < 0 || when > seconds) continue;

            found.Add(new Trigger(when, name, end, acyclic, local));
        }

        return found.OrderBy(t => t.At).ToList();
    }

    /// When a trigger goes out, in clip local seconds from the start.
    ///
    /// Relative to the end means measured back from it, so a localTime of zero is the clip finishing
    /// and a localTime of half a second is half a second before it does. 2,148 of the corpus's 2,886
    /// triggers are written this way and 1,500 of those sit away from the end, so this is the common
    /// case rather than a corner of it.
    ///
    /// Pulled out of the byte reading so it can be checked against times worked out by hand. Reading
    /// an end relative trigger as an absolute one is a fault the corpus cannot catch: every trigger
    /// still lands inside its clip, every gate still passes, and the events simply come out at the
    /// wrong moments.
    public static float TriggerAt(float localTime, bool relativeToEnd, float seconds) =>
        relativeToEnd ? seconds - localTime : localTime;

    /// A one byte flag read out of the middle of a struct, where the reader only offers whole words.
    private static bool Flag(PackfileObjects objects, int at) => ((objects.ReadIntAt(at) ?? 0) & 0xff) != 0;

    /// Lengths read from the animation files a project's clips name.
    ///
    /// Built once per behaviour and handed to `All`, because a character's clips name the same
    /// animation many times over and reading one is the expensive part. A name that resolves to
    /// nothing is remembered as zero so a missing file is looked for once rather than once per clip.
    public static LengthOf FromDisk(string behaviourPath)
    {
        string root;
        try { root = ProjectChain.Resolve(behaviourPath).Root; }
        catch (Exception) { root = ""; }

        var cache = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        var reader = new HkxBinaryReader();

        return animation =>
        {
            if (root.Length == 0) return 0;
            if (cache.TryGetValue(animation, out float known)) return known;

            float seconds = 0;
            string path = ProjectChain.ResolvePath(root, animation);
            if (File.Exists(path) && reader.TryReadAnimation(path, out var data)) seconds = data.Duration;

            cache[animation] = seconds;
            return seconds;
        };
    }
}
