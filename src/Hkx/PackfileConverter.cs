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

        var data = image.Section("__data__");
        if (data == null) return false;

        var source = image.Layout;
        if (source.PointerSize == target.PointerSize)
        {
            image.LayoutRules = Rules(target);
            return true;
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

        int end = blocks.Count == 0 ? 0 : blocks[^1].TargetAt + blocks[^1].TargetLen;
        var written = new byte[PackfileLayout.Align(end, NativeAppend.Alignment)];
        var siteMap = new Dictionary<int, int>();

        foreach (var block in blocks)
            Transcode(types, source, target, data.Data, written, block, siteMap);

        if (!Remap(data, image, self, siteMap, baseMap, out var locals, out var globals, out var virtuals))
            return false;

        data.Data = written;
        data.SetLocals(locals);
        data.SetGlobals(globals);
        data.SetVirtuals(virtuals);
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
                    Discover(types, objects, source, target, aims, at, member.CType, blocks, seen, depth + 1);
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

    private static void Transcode(HavokClassTypes types, PointerLayout source, PointerLayout target,
                                  byte[] from, byte[] to, Block block, Dictionary<int, int> siteMap)
    {
        switch (block.Kind)
        {
            case "object":
                TranscodeObject(types, source, target, from, to, block.SourceAt, block.TargetAt,
                                block.ClassName!, siteMap);
                break;

            case "struct array":
                for (int e = 0; e < block.Count; e++)
                    TranscodeObject(types, source, target, from, to,
                                    block.SourceAt + e * block.SourceElemWidth,
                                    block.TargetAt + e * block.TargetElemWidth, block.ClassName!, siteMap);
                break;

            case "pointer array":
            case "string array":
                for (int e = 0; e < block.Count; e++)
                    siteMap[block.SourceAt + e * block.SourceElemWidth] = block.TargetAt + e * block.TargetElemWidth;
                break;

            case "value array":
                Array.Copy(from, block.SourceAt, to, block.TargetAt, block.Count * block.SourceElemWidth);
                break;

            case "string":
            case "element string":
                Array.Copy(from, block.SourceAt, to, block.TargetAt, block.TargetLen);
                break;
        }
    }

    private static void TranscodeObject(HavokClassTypes types, PointerLayout source, PointerLayout target,
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

            switch (member.VType)
            {
                case "TYPE_POINTER":
                case "TYPE_STRINGPTR":
                case "TYPE_CSTRING":
                    siteMap[s] = t;
                    break;

                case "TYPE_ARRAY":
                case "TYPE_SIMPLEARRAY":
                    siteMap[s] = t;
                    int ints = member.VType == "TYPE_ARRAY" ? 8 : 4;
                    Array.Copy(from, s + source.PointerSize, to, t + target.PointerSize, ints);
                    break;

                case "TYPE_ULONG":
                    Array.Copy(from, s, to, t, Math.Min(source.PointerSize, target.PointerSize));
                    break;

                case "TYPE_STRUCT":
                    if (member.CType != null && types.Knows(member.CType))
                        TranscodeObject(types, source, target, from, to, s, t, member.CType, siteMap);
                    break;

                default:
                    int unit = member.VType is "TYPE_ENUM" or "TYPE_FLAGS"
                        ? HavokClassTypes.Width(member.VSub)
                        : HavokClassTypes.Width(member.VType);
                    Array.Copy(from, s, to, t, unit * Math.Max(1, member.ArrSize));
                    break;
            }
        }
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
