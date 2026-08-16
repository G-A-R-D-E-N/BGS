using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class PackfileConverter
{
    private sealed class Block
    {
        public int SourceAt;
        public int TargetAt;
        public string Kind = "";
        public int TargetLen;
        public string? ClassName;
        public int Count;
        public int SourceElemWidth;
        public int TargetElemWidth;
    }

    public static bool ConvertTo(PackfileImage image, PointerLayout target, HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;

        // Only the 4- and 8-byte layouts can actually be read back (PackfileImage.Read enforces
        // the same set). A default-initialised PointerLayout has PointerSize 0, so guard here
        // rather than trust the caller to pass a representable width.
        if (target.PointerSize != 4 && target.PointerSize != 8) return false;

        var data = image.Section("__data__");
        if (data == null) return false;

        var source = image.Layout;
        if (source.PointerSize == target.PointerSize)
        {
            // Same pointer width means no relayout is needed. Only restamp a header that
            // already matches the target rules; if the non-pointer packing differs, the bytes
            // were laid out under rules we did not reproduce, and relabelling them would make
            // the header lie about the file. Refuse rather than silently canonicalise.
            var want = Rules(target);
            return image.LayoutRules.Length >= 4 && image.LayoutRules.AsSpan(0, 4).SequenceEqual(want);
        }

        PackfileObjects objects;
        try { objects = new PackfileObjects(image); }
        catch (InvalidOperationException) { return false; }

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in objects.Instances) CollectTypes(types, instance.ClassName, reachable);
        if (reachable.Any(c => !LayoutWalker.CanPlace(types, c))) return false;

        var aims = new Dictionary<int, int>();
        foreach (var (from, to) in data.Locals()) aims[from] = to;
        int self = image.Sections.IndexOf(data);
        foreach (var (from, section, to) in data.Globals()) if (section == self) aims[from] = to;

        var blocks = new List<Block>();
        var placedBlocks = new HashSet<int>();
        foreach (var instance in objects.Instances)
        {
            var layout = LayoutWalker.Of(types, instance.ClassName, target);
            blocks.Add(new Block
            {
                SourceAt = instance.Offset, Kind = "object",
                TargetLen = layout.Size, ClassName = instance.ClassName,
            });
            Discover(types, objects, source, target, aims, instance.Offset, instance.ClassName,
                     blocks, placedBlocks, 0);
        }

        var targetAt = PackfileLayout.Where(blocks.Select(b => new PackfileLayout.Item(b.Kind, b.SourceAt, b.TargetLen)).ToList());
        for (int i = 0; i < blocks.Count; i++) blocks[i].TargetAt = targetAt[i];

        var baseMap = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            baseMap[block.SourceAt] = block.TargetAt;
            if (block.Kind == "struct array")
                for (int e = 0; e < block.Count; e++)
                    baseMap[block.SourceAt + e * block.SourceElemWidth] = block.TargetAt + e * block.TargetElemWidth;
        }

        // The root object may move during relayout, so its header offset has to move with it.
        // Resolve the new offset now (failing if it does not correspond to a relocated block),
        // but only write it back once the whole conversion has committed.
        int contentsOffset = image.ContentsSectionOffset;
        if (image.ContentsSectionIndex == self &&
            !baseMap.TryGetValue(image.ContentsSectionOffset, out contentsOffset))
            return false;

        int end = blocks.Count == 0 ? 0 : blocks[^1].TargetAt + blocks[^1].TargetLen;
        var written = new byte[PackfileLayout.Align(end, NativeAppend.Alignment)];
        var siteMap = new Dictionary<int, int>();

        foreach (var block in blocks)
            if (!Transcode(types, source, target, data.Data, written, block, siteMap))
                return false;

        if (!Remap(data, image, self, siteMap, baseMap, out var locals, out var globals, out var virtuals))
            return false;

        data.Data = written;
        data.SetLocals(locals);
        data.SetGlobals(globals);
        data.SetVirtuals(virtuals);
        image.ContentsSectionOffset = contentsOffset;
        image.LayoutRules = Rules(target);
        return true;
    }

    private static void Discover(HavokClassTypes types, PackfileObjects objects, PointerLayout source,
                                 PointerLayout target, Dictionary<int, int> aims, int offset, string className,
                                 List<Block> blocks, HashSet<int> seen, int depth)
    {
        if (depth > 12) return;
        var laid = LayoutWalker.Of(types, className, source);
        var members = types.Members(className);

        for (int i = 0; i < members.Count && i < laid.Offsets.Count; i++)
        {
            var member = members[i];
            if (!member.Written) continue;
            int at = offset + laid.Offsets[i];

            if (member.VType is "TYPE_STRINGPTR" or "TYPE_CSTRING")
            {
                if (aims.TryGetValue(at, out int text) && seen.Add(text))
                    blocks.Add(StringBlock(objects, text));
                continue;
            }

            if (member.VType == "TYPE_STRUCT")
            {
                if (member.CType != null && types.Knows(member.CType))
                {
                    int stride = LayoutWalker.Of(types, member.CType, source).Size;
                    for (int e = 0; e < Math.Max(1, member.ArrSize); e++)
                        Discover(types, objects, source, target, aims, at + e * stride, member.CType,
                                 blocks, seen, depth + 1);
                }
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY")) continue;

            var array = objects.ArrayAt(at);
            if (array == null || array.Count == 0 || !aims.ContainsKey(at)) continue;

            if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
            {
                int srcStride = LayoutWalker.Of(types, member.CType, source).Size;
                int tgtStride = LayoutWalker.Of(types, member.CType, target).Size;
                if (srcStride <= 0 || tgtStride <= 0) continue;

                if (seen.Add(array.At))
                    blocks.Add(new Block
                    {
                        SourceAt = array.At, Kind = "struct array", ClassName = member.CType,
                        Count = array.Count, SourceElemWidth = srcStride, TargetElemWidth = tgtStride,
                        TargetLen = array.Count * tgtStride,
                    });

                for (int e = 0; e < array.Count; e++)
                    Discover(types, objects, source, target, aims, array.At + e * srcStride, member.CType,
                             blocks, seen, depth + 1);
                continue;
            }

            bool pointerElements = member.VSub is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING";
            int srcWidth = pointerElements ? source.PointerSize : HavokClassTypes.Width(member.VSub);
            int tgtWidth = pointerElements ? target.PointerSize : HavokClassTypes.Width(member.VSub);
            if (srcWidth <= 0) continue;

            string kind = member.VSub == "TYPE_POINTER" ? "pointer array"
                        : pointerElements ? "string array" : "value array";

            if (seen.Add(array.At))
                blocks.Add(new Block
                {
                    SourceAt = array.At, Kind = kind, Count = array.Count,
                    SourceElemWidth = srcWidth, TargetElemWidth = tgtWidth,
                    TargetLen = array.Count * tgtWidth,
                });

            if (member.VSub is "TYPE_STRINGPTR" or "TYPE_CSTRING")
                for (int e = 0; e < array.Count; e++)
                    if (aims.TryGetValue(array.At + e * srcWidth, out int text) && seen.Add(text))
                        blocks.Add(StringBlock(objects, text, "element string"));
        }
    }

    private static void CollectTypes(HavokClassTypes types, string className, HashSet<string> into)
    {
        if (!into.Add(className)) return;
        foreach (var member in types.Members(className))
        {
            if (member.CType == null || !types.Knows(member.CType)) continue;
            if (member.VType == "TYPE_STRUCT" ||
                (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" && member.VSub == "TYPE_STRUCT"))
                CollectTypes(types, member.CType, into);
        }
    }

    private static Block StringBlock(PackfileObjects objects, int at, string kind = "string") =>
        new() { SourceAt = at, Kind = kind, TargetLen = objects.RunToNull(at) };

    private static bool Transcode(HavokClassTypes types, PointerLayout source, PointerLayout target,
                                  byte[] from, byte[] to, Block block, Dictionary<int, int> siteMap)
    {
        switch (block.Kind)
        {
            case "object":
                return TranscodeObject(types, source, target, from, to, block.SourceAt, block.TargetAt,
                                       block.ClassName!, siteMap);

            case "struct array":
                for (int e = 0; e < block.Count; e++)
                    if (!TranscodeObject(types, source, target, from, to,
                                         block.SourceAt + e * block.SourceElemWidth,
                                         block.TargetAt + e * block.TargetElemWidth, block.ClassName!, siteMap))
                        return false;
                return true;

            case "pointer array":
            case "string array":
                for (int e = 0; e < block.Count; e++)
                    siteMap[block.SourceAt + e * block.SourceElemWidth] = block.TargetAt + e * block.TargetElemWidth;
                return true;

            case "value array":
                Array.Copy(from, block.SourceAt, to, block.TargetAt, block.Count * block.SourceElemWidth);
                return true;

            case "string":
            case "element string":
                Array.Copy(from, block.SourceAt, to, block.TargetAt, block.TargetLen);
                return true;
        }
        return true;
    }

    private static bool TranscodeObject(HavokClassTypes types, PointerLayout source, PointerLayout target,
                                        byte[] from, byte[] to, int sourceAt, int targetAt, string className,
                                        Dictionary<int, int> siteMap)
    {
        var src = LayoutWalker.Of(types, className, source);
        var dst = LayoutWalker.Of(types, className, target);
        var members = types.Members(className);

        for (int i = 0; i < members.Count && i < src.Offsets.Count; i++)
        {
            var member = members[i];
            int s = sourceAt + src.Offsets[i];
            int t = targetAt + dst.Offsets[i];
            int count = Math.Max(1, member.ArrSize);

            switch (member.VType)
            {
                case "TYPE_POINTER":
                case "TYPE_STRINGPTR":
                case "TYPE_CSTRING":
                    for (int e = 0; e < count; e++)
                        siteMap[s + e * source.PointerSize] = t + e * target.PointerSize;
                    break;

                case "TYPE_VARIANT":
                    // Two pointer-sized slots per element (object pointer, then type pointer).
                    // Register both as relocation sites and copy nothing: copying the source's
                    // wider bytes into the narrower destination would overrun the next field and
                    // strand the fixups.
                    for (int e = 0; e < count; e++)
                    {
                        int vs = s + e * 2 * source.PointerSize;
                        int vt = t + e * 2 * target.PointerSize;
                        siteMap[vs] = vt;
                        siteMap[vs + source.PointerSize] = vt + target.PointerSize;
                    }
                    break;

                case "TYPE_ARRAY":
                case "TYPE_SIMPLEARRAY":
                {
                    int ints = member.VType == "TYPE_ARRAY" ? 8 : 4;
                    int srcWidth = source.PointerSize + ints;
                    int tgtWidth = target.PointerSize + ints;
                    for (int e = 0; e < count; e++)
                    {
                        int es = s + e * srcWidth;
                        int et = t + e * tgtWidth;
                        siteMap[es] = et;
                        Array.Copy(from, es + source.PointerSize, to, et + target.PointerSize, ints);
                    }
                    break;
                }

                case "TYPE_ULONG":
                    for (int e = 0; e < count; e++)
                    {
                        int es = s + e * source.PointerSize;
                        int et = t + e * target.PointerSize;
                        // Narrowing must not silently drop a value that does not fit. If any high
                        // byte is set, the 64-bit value cannot be represented at 32 bits: fail the
                        // conversion instead of truncating.
                        if (source.PointerSize > target.PointerSize)
                            for (int k = target.PointerSize; k < source.PointerSize; k++)
                                if (from[es + k] != 0) return false;
                        Array.Copy(from, es, to, et, Math.Min(source.PointerSize, target.PointerSize));
                    }
                    break;

                case "TYPE_STRUCT":
                    if (member.CType != null && types.Knows(member.CType))
                    {
                        int srcStride = LayoutWalker.Of(types, member.CType, source).Size;
                        int tgtStride = LayoutWalker.Of(types, member.CType, target).Size;
                        for (int e = 0; e < count; e++)
                            if (!TranscodeObject(types, source, target, from, to,
                                                 s + e * srcStride, t + e * tgtStride, member.CType, siteMap))
                                return false;
                    }
                    break;

                default:
                    int unit = member.VType is "TYPE_ENUM" or "TYPE_FLAGS"
                        ? HavokClassTypes.Width(member.VSub)
                        : HavokClassTypes.Width(member.VType);
                    Array.Copy(from, s, to, t, unit * count);
                    break;
            }
        }
        return true;
    }

    private static bool Remap(PackfileSection data, PackfileImage image, int self,
                              Dictionary<int, int> siteMap, Dictionary<int, int> baseMap,
                              out List<(int, int)> locals,
                              out List<(int, int, int)> globals,
                              out List<(int, int, int)> virtuals)
    {
        locals = new List<(int, int)>();
        globals = new List<(int, int, int)>();
        virtuals = new List<(int, int, int)>();

        foreach (var (from, to) in data.Locals())
        {
            if (!siteMap.TryGetValue(from, out int nf) || !baseMap.TryGetValue(to, out int nt)) return false;
            locals.Add((nf, nt));
        }

        foreach (var (from, section, to) in data.Globals())
        {
            if (!siteMap.TryGetValue(from, out int nf)) return false;
            int nt = section == self ? (baseMap.TryGetValue(to, out int b) ? b : -1) : to;
            if (nt < 0) return false;
            globals.Add((nf, section, nt));
        }

        foreach (var (from, section, to) in data.Virtuals())
        {
            if (!baseMap.TryGetValue(from, out int nf)) return false;
            virtuals.Add((nf, section, to));
        }
        return true;
    }

    private static byte[] Rules(PointerLayout target) =>
        new byte[] { (byte)target.PointerSize, 1, 0, 1 };
}
