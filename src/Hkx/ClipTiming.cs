using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class ClipTiming
{

    public sealed record Trigger(float At, string Event, bool RelativeToEnd, bool Acyclic,
                                float LocalTime = 0)
    {
        public override string ToString() =>
            $"'{Event}' at {At:F3}s" + (RelativeToEnd ? " (from the end)" : "");
    }

    public sealed record Clip(string ClipId, string Name, string Animation, float Seconds,
                              IReadOnlyList<Trigger> Triggers, string Mode = "", string Why = "")
    {
        public bool Known => Seconds > 0;

        public bool Looping => Mode is "MODE_LOOPING" or "MODE_PING_PONG";

        public override string ToString() =>
            Known ? $"'{Name}' lasts {Seconds:F3}s with {Triggers.Count} trigger(s)"
                  : $"'{Name}' has no length: {Why}";
    }

    public delegate float LengthOf(string animationName);

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

    public static Clip? Of(PackfileObjects objects, PackfileObjects.Instance clip,
                           IReadOnlyList<string> events, LengthOf lengthOf)
    {
        if (clip.ClassName != "hkbClipGenerator") return null;

        string id = (NativeGraphModel.FirstId + objects.IndexOf(clip)).ToString();
        string name = objects.ReadString(clip, "name") ?? "";
        string animation = objects.ReadString(clip, "animationName") ?? "";

        float seconds = Seconds(objects, clip, animation, lengthOf, out string why);
        var triggers = Triggers(objects, clip, events, seconds);

        int mode = (objects.ReadIntAt(clip.Offset + 190) ?? 0) & 0xff;
        string named = HavokClassTypes.Shipped.Enum("hkbClipGenerator", "PlaybackMode")
                                              ?.FirstOrDefault(v => v.Value == mode).Key ?? "";

        return new Clip(id, name, animation, seconds, triggers, named, why);
    }

    public static float Seconds(PackfileObjects objects, PackfileObjects.Instance clip,
                                string animation, LengthOf lengthOf, out string why) =>
        Span(objects.ReadFloat(clip, "enforcedDuration") ?? 0,
             animation.Length == 0 ? -1 : lengthOf(animation),
             objects.ReadFloat(clip, "cropStartAmountLocalTime") ?? 0,
             objects.ReadFloat(clip, "cropEndAmountLocalTime") ?? 0,
             objects.ReadFloat(clip, "playbackSpeed") ?? 1,
             animation, out why);

    public static float Span(float enforced, float animation, float cropStart, float cropEnd,
                             float speed, string named, out string why)
    {
        why = "";

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

        if (speed == 0)
        {
            why = "the clip plays at zero speed, so it is parked on a frame and never finishes";
            return 0;
        }

        return cropped / Math.Abs(speed);
    }

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

            if (annotation) continue;

            string name = id >= 0 && id < events.Count ? events[id] : "";
            if (name.Length == 0) continue;

            float when = TriggerAt(local, end, seconds);
            if (when < 0 || when > seconds) continue;

            found.Add(new Trigger(when, name, end, acyclic, local));
        }

        return found.OrderBy(t => t.At).ToList();
    }

    public static float TriggerAt(float localTime, bool relativeToEnd, float seconds) =>
        relativeToEnd ? seconds - localTime : localTime;

    private static bool Flag(PackfileObjects objects, int at) => ((objects.ReadIntAt(at) ?? 0) & 0xff) != 0;

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
