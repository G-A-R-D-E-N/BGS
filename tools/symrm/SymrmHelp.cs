using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BehaviourStudio.Tools;

public sealed record SymrmCommandHelp(
    string Name,
    string Usage,
    string Summary,
    string Flags,
    string Example);

public static class SymrmHelp
{
    private static readonly SymrmCommandHelp[] Catalog =
    {
        C("corpus", "corpus <archive.ba2> <output-dir> [filter]",
          "Extract matching HKX files from a BA2 corpus.", "none",
          "symrm corpus Fallout4.ba2 out behavior"),
        C("check", "check <file-or-dir> [--data <Data folder>] [--plugins <plugins.txt>]",
          "Validate native HKX data and optionally resolve game-data references.",
          "--data <folder>; --plugins <file>", "symrm check MyBehavior.hkx --data ./Data"),
        C("states", "states <xml-dir>",
          "Summarize state generators and dangling state references in unpacked XML.", "none",
          "symrm states ./xml"),
        C("events", "events <xml-file-or-dir>",
          "Audit event declarations and their roles in unpacked behaviour XML.", "none",
          "symrm events ./xml"),
        C("frames", "frames <animation.hkx-or-dir> [track-count | --digest]",
          "Inspect decoded animation frame/track data or print a compact digest.",
          "--digest", "symrm frames Idle.hkx --digest"),
        C("scale", "scale <animation.hkx-or-dir>",
          "Inspect decoded animation scale channels.", "none",
          "symrm scale ./Animations"),
        C("skeleton", "skeleton <skeleton.hkx> [bone-filter]",
          "Print skeleton bones and reference transforms, following a matching bone chain.", "none",
          "symrm skeleton skeleton.hkx Hand"),
        C("rig", "rig <skeleton.hkx-or-dir> [output.json]",
          "Audit rig/skeleton data; a single file can also emit its JSON representation.", "none",
          "symrm rig skeleton.hkx skeleton.json"),
        C("extract", "extract <archive.ba2> <filter> <output-dir> [extension] [--tree]",
          "Extract matching archive entries, optionally preserving their directory tree.",
          "--tree", "symrm extract Fallout4.ba2 behavior ./out .hkx --tree"),
        C("ba2", "ba2 <archive.ba2> [query] [extension]",
          "Inspect a Bethesda BA2 archive and optionally filter its entries.", "none",
          "symrm ba2 Fallout4.ba2 behavior .hkx"),
        C("motion", "motion <animation.hkx-or-dir>",
          "Inspect decoded root-motion data.", "none",
          "symrm motion WalkForward.hkx"),
        C("pose", "pose <skeleton.hkx> <animation.hkx> [frame]",
          "Pose a decoded animation against a skeleton and print the selected frame.", "none",
          "symrm pose skeleton.hkx Idle.hkx 12"),
        C("channels", "channels <skeleton.hkx> <animation.hkx-or-dir>",
          "Compare animation channels with the rig they drive.", "none",
          "symrm channels skeleton.hkx ./Animations"),
        C("packfile", "packfile <hkx-file-or-dir>",
          "Inspect packfile sections, fixups, and round-trip structure.", "none",
          "symrm packfile MyBehavior.hkx"),
        C("nullsave", "nullsave <hkx-file-or-dir>",
          "Diagnostic no-op native save and byte comparison.", "none",
          "symrm nullsave ./fixtures"),
        C("layout", "layout <hkx-file-or-dir>",
          "Audit native object layout and placement.", "none",
          "symrm layout MyBehavior.hkx"),
        C("relayout", "relayout <hkx-file-or-dir>",
          "Exercise native relayout/rebuild behavior across a file or corpus.", "none",
          "symrm relayout ./fixtures"),
        C("ground", "ground <hkx-file>",
          "Inspect pointer/fixup grounding for a native packfile.", "none",
          "symrm ground MyBehavior.hkx"),
        C("offsets", "offsets <offsets.json> [output-dump.json]",
          "Inspect reflected member offsets and optionally emit a normalized dump.", "none",
          "symrm offsets offsets.json checked-offsets.json"),
        C("convert", "convert <input.hkx> <output.hkx> <4|8>",
          "Convert a packfile between 32-bit and 64-bit pointer layouts.", "none",
          "symrm convert input.hkx output.hkx 8"),
        C("compare", "compare <left.hkx> <right.hkx>",
          "Byte/section compare two native packfiles.", "none",
          "symrm compare before.hkx after.hkx"),
        C("delete", "delete <hkx-file-or-dir> [object-id]",
          "Exercise native object deletion, defaulting to the final object when no id is supplied.", "none",
          "symrm delete MyBehavior.hkx 95"),
        C("paste", "paste <hkx-file-or-dir>",
          "Exercise native object paste/copy authoring paths.", "none",
          "symrm paste MyBehavior.hkx"),
        C("template", "template <hkx-file-or-dir> [every-nth]",
          "Exercise graph-template authoring, optionally sampling every Nth candidate.", "none",
          "symrm template ./fixtures 20"),
        C("conditions", "conditions <hkx-file-or-dir>",
          "Inspect and exercise transition-condition authoring.", "none",
          "symrm conditions MyBehavior.hkx"),
        C("savedelete", "savedelete <hkx-file-or-dir> [output.hkx]",
          "Delete an eligible object, save, reload, and verify the result; a single file can be written out.", "none",
          "symrm savedelete MyBehavior.hkx deleted.hkx"),
        C("classcheck", "classcheck <class-dump>",
          "Compare a class/layout dump with the shipped Havok class table.", "none",
          "symrm classcheck classes.txt"),
        C("types", "types <hkx-file> [--members]",
          "Inspect class metadata carried by a packfile.",
          "--members", "symrm types MyBehavior.hkx --members"),
        C("chain", "chain <behaviour.hkx> [--data <Data folder>] [--plugins <plugins.txt>]",
          "Trace behaviour-to-animation resolution and report missing links.",
          "--data <folder>; --plugins <file>", "symrm chain WeaponBehavior.hkx --data ./Data"),
        C("crash", "crash <hash-or-crashlog> --data <Data folder> [--mods <MO2 mods folder>] [--profile <name>]",
          "Resolve an AnimTextData subgraph crash hash and report missing animations.",
          "--data <folder>; --mods <folder>; --profile <name>",
          "symrm crash 10448007347639226270 --data ./Data"),
        C("hash", "hash <behaviour.hkx> <sapt-prefix> [sapt-prefix ...]",
          "Compute the Fallout 4 AnimTextData subgraph id for a behaviour path and SAPT prefixes.",
          "none", "symrm hash Actors/Character/Behaviors/WeaponBehavior.hkx Animations/Weapon/Pistol"),
        C("sweep", "sweep --data <Data folder> [--mods <MO2 mods folder>] [--profile <name>]",
          "Sweep every discovered subgraph manifest for animation coverage gaps.",
          "--data <folder>; --mods <folder>; --profile <name>",
          "symrm sweep --data ./Data --mods ./mods --profile Default"),
        C("diff", "diff --data <Data folder> --mods <MO2 mods folder> [--profile <name>]",
          "Compare vanilla and modded subgraph/behaviour coverage.",
          "--data <folder>; --mods <folder>; --profile <name>",
          "symrm diff --data ./Data --mods ./mods --profile Default"),
        C("notes", "notes <hkx-file-or-dir>",
          "Inspect animation annotations/notes in native files.", "none",
          "symrm notes ./Animations"),
        C("saveevent", "saveevent <hkx-file-or-dir> [output.hkx]",
          "Exercise event edits through native save/reload verification; a single file can be written out.", "none",
          "symrm saveevent MyBehavior.hkx event-edited.hkx"),
        C("savewide", "savewide <hkx-file-or-dir>",
          "Exercise wide/vector field edits through native save/reload verification.", "none",
          "symrm savewide MyBehavior.hkx"),
        C("savenumbers", "savenumbers <hkx-file-or-dir>",
          "Exercise numeric-array edits through native save/reload verification.", "none",
          "symrm savenumbers MyBehavior.hkx"),
        C("walk", "walk <hkx-file-or-dir>",
          "Walk native objects and report reachability/reference structure.", "none",
          "symrm walk MyBehavior.hkx"),
        C("signatures", "signatures <hkx-file-or-dir>",
          "Inspect Havok class signatures used by native files.", "none",
          "symrm signatures ./fixtures"),
        C("paths", "paths <hkx-file-or-dir>",
          "Audit serialized object/reference paths.", "none",
          "symrm paths MyBehavior.hkx"),
        C("elements", "elements <hkx-file-or-dir>",
          "Inspect inline array/struct elements and their serialization.", "none",
          "symrm elements MyBehavior.hkx"),
        C("nesting", "nesting <hkx-file-or-dir>",
          "Inspect native object nesting/ownership relationships.", "none",
          "symrm nesting MyBehavior.hkx"),
        C("objects", "objects <hkx-file> [class-filter]",
          "List native objects, optionally filtering by Havok class name.", "none",
          "symrm objects MyBehavior.hkx hkbClipGenerator"),
        C("capacity", "capacity <hkx-file-or-dir>",
          "Audit Havok array capacity/count serialization.", "none",
          "symrm capacity ./fixtures"),
        C("qstransform", "qstransform <hkx-file-or-dir>",
          "Inspect hkQsTransform values and layout behavior.", "none",
          "symrm qstransform skeleton.hkx"),
        C("splinestats", "splinestats <animation.hkx-or-dir>",
          "Summarize spline-compressed animation structure and error metrics.", "none",
          "symrm splinestats ./Animations"),
        C("spline", "spline <animation.hkx-or-dir> [every-nth]",
          "Decode/re-encode spline-compressed animation data, optionally sampling every Nth file.", "none",
          "symrm spline ./Animations 10"),
        C("savespline", "savespline <animation.hkx-or-dir> [every-nth]",
          "Exercise spline-animation edits through native save/reload verification, optionally sampling.", "none",
          "symrm savespline ./Animations 10"),
        C("editframe", "editframe <animation.hkx-or-dir> [every-nth]",
          "Exercise decoded animation frame edits and verify the result, optionally sampling.", "none",
          "symrm editframe ./Animations 10"),
        C("trim", "trim <animation.hkx-or-dir> [every-nth]",
          "Exercise animation trimming and verify decoded output, optionally sampling.", "none",
          "symrm trim ./Animations 10"),
        C("retime", "retime <animation.hkx-or-dir> [every-nth]",
          "Exercise animation retiming and verify decoded output, optionally sampling.", "none",
          "symrm retime ./Animations 10"),
        C("run", "run <behaviour.hkx-or-dir> [event-or-seconds ...]",
          "Run the behaviour simulation harness and optionally send events or advance time.", "none",
          "symrm run MyBehavior.hkx Activate 0.5"),
        C("weights", "weights <behaviour.hkx-or-dir>",
          "Inspect runtime generator/blend weights.", "none",
          "symrm weights MyBehavior.hkx"),
        C("cliptime", "cliptime <behaviour.hkx-or-dir>",
          "Resolve clip timing from animation data beside a behaviour.", "none",
          "symrm cliptime MyBehavior.hkx"),
        C("cliptrim", "cliptrim <behaviour.hkx-or-dir>",
          "Audit clip crop/trim values against resolved animation duration.", "none",
          "symrm cliptrim MyBehavior.hkx"),
        C("mesh", "mesh <mesh.nif> [skeleton.hkx]",
          "Inspect NIF mesh geometry and optionally check its skin binding against a skeleton.", "none",
          "symrm mesh Character.nif skeleton.hkx"),
        C("meshpng", "meshpng <mesh.nif> <skeleton.hkx> <output.png> [bone-name ...]",
          "Render a skinned-mesh diagnostic image, optionally highlighting named bone influences.", "none",
          "symrm meshpng Character.nif skeleton.hkx mesh.png LHand RHand"),
        C("lifecycle", "lifecycle <hkx-file-or-dir>",
          "Run the native open/edit/save/reload/validate/render lifecycle gate.", "none",
          "symrm lifecycle ./fixtures"),
        C("test", "test",
          "Run the native/Havok regression harness used by BGS CI.", "none",
          "symrm test"),
        C("defaults", "defaults <Fallout4.exe.unpacked.exe> <Fallout4_163_functions.txt> [class-filter] [--write]",
          "Compare game class-registration defaults with the shipped table, optionally writing agreed additions.",
          "--write", "symrm defaults Fallout4.exe.unpacked.exe Fallout4_163_functions.txt hkbClipGenerator"),
    };

    private static readonly Dictionary<string, SymrmCommandHelp> ByName =
        Catalog.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SymrmCommandHelp> Commands => Catalog;

    public static bool IsHelpToken(string value) => value is "--help" or "-h";

    public static string Overview()
    {
        var text = new StringBuilder();
        text.AppendLine("symrm, the native verification and diagnostic CLI for Behaviour Graph Studio.");
        text.AppendLine();
        text.AppendLine("Usage: symrm <command> [arguments]");
        text.AppendLine("       symrm --help");
        text.AppendLine("       symrm <command> --help");
        text.AppendLine();
        text.AppendLine("Commands:");
        foreach (var command in Catalog)
            text.Append("  ").Append(command.Name.PadRight(13)).AppendLine(command.Summary);
        text.AppendLine();
        text.AppendLine("Run any command with --help for its arguments, flags, and an example.");
        return text.ToString().TrimEnd();
    }

    public static bool TryCommand(string name, out string text)
    {
        if (!ByName.TryGetValue(name, out var command))
        {
            text = "";
            return false;
        }
        text = Render(command);
        return true;
    }

    public static string Render(SymrmCommandHelp command)
    {
        var text = new StringBuilder();
        text.AppendLine(command.Summary);
        text.AppendLine();
        text.AppendLine("Usage:");
        text.Append("  symrm ").AppendLine(command.Usage);
        text.AppendLine();
        text.AppendLine("Flags:");
        text.Append("  ").AppendLine(command.Flags);
        text.AppendLine();
        text.AppendLine("Example:");
        text.Append("  ").AppendLine(command.Example);
        return text.ToString().TrimEnd();
    }

    private static SymrmCommandHelp C(
        string name, string usage, string summary, string flags, string example) =>
        new(name, usage, summary, flags, example);
}
