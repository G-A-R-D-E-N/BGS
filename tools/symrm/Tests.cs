using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;

// Regressions worth catching without a game install. Everything here works on a graph built in
// memory, so it needs no BA2, no hkxpack and no JVM, which means it can actually be run every time
// rather than only when someone remembers to extract a corpus.
public static class Tests
{
    private static int _failed;
    private static int _ran;

    public static int Run()
    {
        _failed = 0;
        _ran = 0;

        DetachedSubtreeStaysDrawn();
        ReplacingLinkSaysWhatItDisplaced();
        BlenderChildIsWrapped();
        AnyNodeCanBeDeleted();
        StructuralObjectsAreProtected();
        PortTypesRefuseNonsense();

        Console.WriteLine($"\n{_ran} checks, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string what, object expected, object actual)
    {
        _ran++;
        bool ok = Equals(expected, actual);
        if (!ok) _failed++;
        Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {what,-58} expected {expected}, got {actual}");
    }

    private static void CheckTrue(string what, bool value)
    {
        _ran++;
        if (!value) _failed++;
        Console.WriteLine($"  {(value ? "ok  " : "FAIL")}  {what}");
    }

    // The one that bit us. Retargeting a link that already held something detaches whatever it held,
    // along with everything under it. Drawing only what the root reaches made all of that disappear
    // from the canvas and read as deletion. Layout has to walk each detached subtree from its own
    // head, so the drawn count must not follow the reachable count down.
    private static void DetachedSubtreeStaysDrawn()
    {
        Console.WriteLine("detached subtree stays drawn after a retarget");

        string xml = SmallGraph();
        var before = BehaviourGraphModel.Parse(xml);

        Check("objects in the file", 7, before.Objects.Count);
        Check("reachable from the root before", 6, Reachable(before));
        Check("drawn before", 7, GraphAuthor.Layout(before, 1000).Count);

        // #97 is the spare clip nothing points at. Dropping the graph's root generator on it is the
        // drag that caused the report.
        xml = GraphLinks.Connect(xml, "91", "rootGenerator", "97", out _);
        var after = BehaviourGraphModel.Parse(xml);

        Check("objects after, nothing was deleted", 7, after.Objects.Count);
        Check("reachable from the root after", 2, Reachable(after));
        Check("drawn after, the whole point", 7, GraphAuthor.Layout(after, 1000).Count);

        var drawn = GraphAuthor.Layout(after, 1000).Select(l => l.Node.Id).ToHashSet();
        CheckTrue("the displaced state machine #92 is still drawn", drawn.Contains("92"));
        CheckTrue("its child state #93 is still drawn", drawn.Contains("93"));
        CheckTrue("the clip under that state #94 is still drawn", drawn.Contains("94"));
    }

    private static void ReplacingLinkSaysWhatItDisplaced()
    {
        Console.WriteLine("\na replacing connection reports what it displaced");

        GraphLinks.Connect(SmallGraph(), "91", "rootGenerator", "97", out string note);
        CheckTrue("note names the displaced object", note.Contains("#92"));
        CheckTrue("note says it is detached", note.Contains("detached"));

        GraphLinks.Connect(SmallGraph(), "93", "transitions", "97", out string plain);
        CheckTrue("an empty link says nothing about replacing", !plain.Contains("replacing"));
    }

    // A blender holds weighted wrappers, not generators. Writing a bare reference into children
    // passes hkxpack and gives the engine something it cannot read, so the wrapper has to be built.
    private static void BlenderChildIsWrapped()
    {
        Console.WriteLine("\na generator dropped on a blender is wrapped");

        string xml = SmallGraph().Replace(
            "<hkobject class=\"hkbClipGenerator\" name=\"#97\"",
            "<hkobject class=\"hkbBlenderGenerator\" name=\"#98\" signature=\"0xce45c088\">\n" +
            "            <hkparam name=\"name\">Blend</hkparam>\n" +
            "            <hkparam name=\"children\" numelements=\"0\">\n</hkparam>\n" +
            "        </hkobject>\n" +
            "        <hkobject class=\"hkbClipGenerator\" name=\"#97\"");

        xml = GraphLinks.Connect(xml, "98", "children", "94", out string note);
        var model = BehaviourGraphModel.Parse(xml);
        var blender = model.Get("98")!;

        Check("the blender has one child", 1, blender.Refs("children").Count);
        string child = blender.Refs("children")[0];
        Check("the child is a wrapper, not the clip", "hkbBlenderGeneratorChild", model.Get(child)?.Class);
        Check("the wrapper points at the clip", "94", model.Get(child)?.Ref("generator"));
        CheckTrue("the note mentions the wrapper", note.Contains(child));
    }

    // A node that shipped with the game is referenced by something, always. Refusing to delete
    // while references exist therefore made vanilla nodes undeletable, which is not how a graph
    // editor behaves. Deleting breaks the links into it instead.
    private static void AnyNodeCanBeDeleted()
    {
        Console.WriteLine("\nany node can be deleted, links into it are broken first");

        string xml = SmallGraph();
        var before = BehaviourGraphModel.Parse(xml);
        CheckTrue("the clip starts out referenced", GeneratorEditor.ReferencesTo(before, "94").Count == 1);

        xml = GraphAuthor.DeleteNode(xml, "94", out string note);
        var after = BehaviourGraphModel.Parse(xml);

        Check("the object is gone", null, after.Get("94"));
        Check("nothing else was removed", 6, after.Objects.Count);
        Check("the state that held it survives", "A", after.Get("93")?.Str("name"));
        Check("its generator link was cleared, not left dangling", "null", after.Get("93")?.Str("generator"));
        CheckTrue("the note says what it cleared", note.Contains("#93"));
        CheckTrue("no dangling reference remains", GraphValidator.Check(xml)
            .All(f => !f.What.Contains("not in this file")));
    }

    private static void StructuralObjectsAreProtected()
    {
        Console.WriteLine("\nthe objects the file is built around cannot be deleted");

        foreach (string id in new[] { "91" })
        {
            string cls = BehaviourGraphModel.Parse(SmallGraph()).Get(id)!.Class;
            bool refused = false;
            try { GraphAuthor.DeleteNode(SmallGraph(), id, out _); }
            catch (InvalidOperationException) { refused = true; }
            CheckTrue($"deleting #{id} {cls} is refused", refused);
        }

        CheckTrue("a clip is not protected", GraphAuthor.CanDelete("hkbClipGenerator"));
        CheckTrue("a state machine is not protected", GraphAuthor.CanDelete("hkbStateMachine"));
    }

    // The canvas types its ports so GraphEdit refuses a drag that could not work. These are the
    // pairings behind that, checked here because the canvas itself cannot be scripted.
    private static void PortTypesRefuseNonsense()
    {
        Console.WriteLine("\nport types accept what fits and refuse what does not");

        bool Allowed(string field, string className)
        {
            int from = GraphLinks.Accepts(field), to = GraphLinks.FamilyOf(className);
            return from == to || GraphLinks.ValidPairs.Contains((from, to));
        }

        CheckTrue("a generator slot takes a clip", Allowed("generator", "hkbClipGenerator"));
        CheckTrue("a generator slot takes a state machine", Allowed("generator", "hkbStateMachine"));
        CheckTrue("a states array takes a state info", Allowed("states", "hkbStateMachineStateInfo"));
        CheckTrue("a states array takes a generator to wrap", Allowed("states", "hkbClipGenerator"));
        CheckTrue("a blender's children take a wrapper", Allowed("children", "hkbBlenderGeneratorChild"));
        CheckTrue("a modifier slot takes a modifier", Allowed("modifier", "hkbEventDrivenModifier"));

        CheckTrue("a generator slot refuses a modifier", !Allowed("generator", "hkbEventDrivenModifier"));
        CheckTrue("a generator slot refuses a transition array",
            !Allowed("generator", "hkbStateMachineTransitionInfoArray"));
        CheckTrue("a modifier slot refuses a clip", !Allowed("modifier", "hkbClipGenerator"));
        CheckTrue("a triggers slot refuses a clip", !Allowed("triggers", "hkbClipGenerator"));
        CheckTrue("a states array refuses a trigger array",
            !Allowed("states", "hkbClipTriggerArray"));
    }

    private static int Reachable(BehaviourGraphModel model)
    {
        var root = model.Objects.First(o => o.Class == "hkbBehaviorGraph");
        var seen = new HashSet<string> { root.Id };
        var queue = new Queue<HkObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var slot in GraphLinks.OutSlots(model, current))
                foreach (string target in slot.Targets)
                {
                    if (!seen.Add(target)) continue;
                    var next = model.Get(target);
                    if (next != null) queue.Enqueue(next);
                }
        }
        return seen.Count;
    }

    // Seven objects: a graph, a machine with two states, a clip under each, and one spare clip that
    // nothing points at. Small enough to reason about, shaped like the real thing.
    private static string SmallGraph() => """
        <?xml version="1.0" encoding="ascii"?>
        <hkpackfile classversion="11" contentsversion="hk_2014.1.0-r1">
            <hksection name="__data__">
                <hkobject class="hkbBehaviorGraph" name="#91" signature="0xb1218f86">
                    <hkparam name="name">Graph</hkparam>
                    <hkparam name="rootGenerator">#92</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachine" name="#92" signature="0xa5896bcf">
                    <hkparam name="name">Root</hkparam>
                    <hkparam name="startStateId">0</hkparam>
                    <hkparam name="wildcardTransitions">null</hkparam>
                    <hkparam name="states" numelements="2">
                        #93 #95
                    </hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#93" signature="0x39d76713">
                    <hkparam name="name">A</hkparam>
                    <hkparam name="stateId">0</hkparam>
                    <hkparam name="generator">#94</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#94" signature="0xd4cc9f6">
                    <hkparam name="name">ClipA</hkparam>
                    <hkparam name="animationName">a.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbStateMachineStateInfo" name="#95" signature="0x39d76713">
                    <hkparam name="name">B</hkparam>
                    <hkparam name="stateId">1</hkparam>
                    <hkparam name="generator">#96</hkparam>
                    <hkparam name="transitions">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#96" signature="0xd4cc9f6">
                    <hkparam name="name">ClipB</hkparam>
                    <hkparam name="animationName">b.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
                <hkobject class="hkbClipGenerator" name="#97" signature="0xd4cc9f6">
                    <hkparam name="name">Spare</hkparam>
                    <hkparam name="animationName">spare.hkx</hkparam>
                    <hkparam name="triggers">null</hkparam>
                </hkobject>
            </hksection>
        </hkpackfile>
        """;
}
