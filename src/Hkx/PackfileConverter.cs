using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        // For relative-array payloads: the source offset of the four-byte header inside the
        // containing struct, so the relocated payload's new distance from that struct can be
        // written back into bytes 2-3 during transcoding.
        public int HeaderAt;
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
            // Same pointer width means no relayout is needed: the bytes are not transcoded, so
            // this is a header restamp at most. It is not a corruption path, but ConvertTo(true)
            // must not vouch for a file whose classes are not the ones this schema describes, so
            // validate signatures too when the sections needed to read them exist. A bare
            // __data__ with no class-name table has nothing to check.
            if (image.Section("__classnames__") != null)
            {
                PackfileObjects sameObjects;
                try { sameObjects = new PackfileObjects(image); }
                catch (InvalidOperationException) { return false; }
                if (types.SignatureProblems(sameObjects.ClassNames()).Count > 0) return false;
            }

            // Only restamp a header that already matches the target rules; if the non-pointer
            // packing differs, the bytes were laid out under rules we did not reproduce, and
            // relabelling them would make the header lie about the file. Refuse rather than
            // silently canonicalise.
            var want = Rules(target);
            return image.LayoutRules.Length >= 4 && image.LayoutRules.AsSpan(0, 4).SequenceEqual(want);
        }

        PackfileObjects objects;
        try { objects = new PackfileObjects(image); }
        catch (InvalidOperationException) { return false; }

        // A same-named class from another Havok version can carry a different binary layout. If
        // the file's declared signatures do not match the schema, the walker would interpret it
        // with the wrong offsets and produce a successful but corrupted conversion. Refuse before
        // CollectTypes/Discover, like the native writer does before it writes.
        if (types.SignatureProblems(objects.ClassNames()).Count > 0) return false;

        int self = image.Sections.IndexOf(data);
        if (self < 0) return false;

        // The converter lays out only __data__ and then changes the file-wide layout header, so
        // every serialized structure in the file must be accounted for. Anything else the header
        // could affect — populated sections, fixups outside __data__, exports or imports, a
        // root that is not in __data__, or fixups carrying unknown section indices — is refused
        // rather than left behind in the old layout under a header that now describes the file.
        if (!SectionsAreSafe(image, self, types, source)) return false;

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in objects.Instances) CollectTypes(types, instance.ClassName, reachable);
        if (reachable.Any(c => !LayoutWalker.CanPlace(types, c))) return false;

        var aims = new Dictionary<int, int>();
        foreach (var (from, to) in data.Locals()) aims[from] = to;
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

            if (!Discover(types, objects, source, target, aims, instance.Offset, instance.ClassName,
                          blocks, placedBlocks, 0, data.Data.Length))
                return false;
        }

        // Every relocated block claims a source byte range, and the converter would transcode
        // each one independently into its own destination. Two blocks claiming the same bytes —
        // a relative-array payload overlapping an object, two payloads sharing storage, an
        // array backing store aliasing a string — would therefore produce fixups attached to
        // the wrong relocated copy. Relative-array payloads are especially important because
        // their source addresses come from the stored relative offset. Refuse any overlap,
        // including same-start aliases: exact aliasing is only safe with alias semantics that
        // are not implemented.
        var spans = new List<(long Start, long End)>();
        foreach (var block in blocks)
        {
            int len = block.Kind switch
            {
                "object" => LayoutWalker.Of(types, block.ClassName!, source).Size,
                "string" or "element string" => block.TargetLen,
                _ => block.Count * block.SourceElemWidth,
            };
            if (len <= 0) continue;
            spans.Add(((long)block.SourceAt, (long)block.SourceAt + len));
        }
        spans.Sort();
        for (int i = 1; i < spans.Count; i++)
            if (spans[i].Start < spans[i - 1].End || spans[i].Start == spans[i - 1].Start)
                return false;

        // Relative-array payloads are placed on 16-byte lines, matching the FO4-era writer
        // (hkxpack snaps deferred array payloads to 0x10 and pads each completed block to the
        // next line). The placement is still just a policy: the header's stored offset is
        // rewritten to wherever the payload actually lands.
        var targetAt = PackfileLayout.Where(blocks.Select(b => new PackfileLayout.Item(b.Kind, b.SourceAt, b.TargetLen)
        {
            Align = b.Kind == "relarray" ? 16 : 0,
        }).ToList());
        for (int i = 0; i < blocks.Count; i++) blocks[i].TargetAt = targetAt[i];

        // Where each relative-array payload landed, keyed by the source offset of its header
        // site inside the containing struct. TranscodeObject rewrites the header's second
        // uint16 (the payload's distance from that struct's start) to the new distance.
        var relSites = new Dictionary<int, int>();
        foreach (var block in blocks)
            if (block.Kind == "relarray")
                relSites[block.HeaderAt] = block.TargetAt;

        var baseMap = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            baseMap[block.SourceAt] = block.TargetAt;
            if (block.Kind == "struct array" || (block.Kind == "relarray" && block.ClassName != null))
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
            if (!Transcode(types, source, target, data.Data, written, block, siteMap, relSites))
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

    // Fail closed on every section the converter does not lay out. __classnames__ is treated as
    // opaque/raw data and __data__ is the one section being transcoded; anything else that could
    // be affected by the file-wide layout header is refused before any bytes are moved.
    private static bool SectionsAreSafe(PackfileImage image, int self, HavokClassTypes types,
                                        PointerLayout source)
    {
        int classNames = image.Sections.FindIndex(s => s.Tag == "__classnames__");

        // Exports and imports have offset semantics the converter does not remap. Refuse any in
        // any section, __data__ included.
        if (image.Sections.Any(s => s.Exports.Length > 0 || s.Imports.Length > 0)) return false;

        // __classnames__ bytes are treated as opaque/raw data, but its fixup tables are still
        // offset-bearing: the file-wide pointer layout is changing, and none of them would be
        // remapped. Data is allowed; every fixup kind in it is forbidden.
        if (classNames >= 0)
        {
            var names = image.Sections[classNames];
            if (names.Locals().Any() || names.Globals().Any() || names.Virtuals().Any())
                return false;
        }

        for (int i = 0; i < image.Sections.Count; i++)
        {
            var section = image.Sections[i];
            if (i == self || i == classNames) continue;

            // A populated section — even one without fixups — can hold serialized structures that
            // shift with the pointer width, and there is no way to prove its bytes are
            // width-independent. Refuse rather than leave it under a relabelled header.
            if (section.Data.Length > 0) return false;
            if (section.Locals().Any() || section.Globals().Any() || section.Virtuals().Any())
                return false;
        }

        // The root/contents pointer must live in the section being transcoded, or it would keep
        // pointing at bytes laid out under the old rules.
        if (image.ContentsSectionIndex != self) return false;

        // Validate every section index carried by the fixups before using or preserving it.
        foreach (var section in image.Sections)
        {
            if (section.Globals().Any(g => g.Section < 0 || g.Section >= image.Sections.Count) ||
                section.Virtuals().Any(v => v.Section < 0 || v.Section >= image.Sections.Count))
                return false;
        }

        // A data-side pointer whose source is remapped but whose target sits in another section
        // is only safe when that section is width-independent, which is only claimed for the
        // class-name table. Anything else — a populated __types__ or an arbitrary section — is
        // refused.
        foreach (var (_, section, destination) in image.Sections[self].Globals())
        {
            if (section == self) continue;
            if (classNames >= 0 && section == classNames &&
                destination >= 0 && destination < image.Sections[classNames].Data.Length)
                continue;
            return false;
        }

        // __data__ virtual fixups resolve class names: each declares the class of the object at
        // its source. The section field must name the class-name table, not __data__ or some
        // other section whose bytes happen to read as a string (PackfileObjects resolves the
        // destination against __classnames__ regardless of the section field). The destination
        // must be a genuine record start rather than a byte inside a longer name, which could
        // resolve to a known suffix and bypass the signature check that runs on full records.
        // Sources must be unique — two virtuals cannot declare one object's class twice — and
        // the object each describes must fit inside __data__ under the active layout, or
        // Discover would read past the section.
        if (classNames < 0) return false;
        var nameStarts = ClassNameNameStarts(image.Sections[classNames]);
        var seenVirtualSources = new HashSet<int>();
        var data = image.Sections[self];
        var spans = new List<(long Start, long End)>();
        foreach (var (sourceAt, section, destination) in data.Virtuals())
        {
            if (sourceAt < 0 || !seenVirtualSources.Add(sourceAt)) return false;
            if (section != classNames) return false;
            if (!nameStarts.Contains(destination)) return false;

            string? name = ClassNameAt(image.Sections[classNames], destination);
            if (name != null && types.Knows(name))
            {
                // The layout must be derivable before Of tries to derive it: a malformed parent
                // cycle would otherwise recurse forever.
                if (!LayoutWalker.CanPlace(types, name)) return false;

                int size = LayoutWalker.Of(types, name, source).Size;
                // long arithmetic: a hostile source near int.MaxValue must not overflow past
                // the bounds check.
                if (size <= 0 || (long)sourceAt + size > data.Data.Length) return false;
                spans.Add(((long)sourceAt, (long)sourceAt + size));
            }
        }

        // Unique starts are not enough: two virtuals may not describe overlapping objects, or
        // the converter would transcode the same source bytes into two destinations.
        spans.Sort();
        for (int i = 1; i < spans.Count; i++)
            if (spans[i - 1].End > spans[i].Start) return false;

        return true;
    }

    // The offsets at which class-name records genuinely begin inside the __classnames__ blob:
    // the four-byte signature followed by the name, exactly the walk PackfileObjects.ClassNames
    // uses. A virtual fixup may only name a whole record, never a byte inside one.
    private static HashSet<int> ClassNameNameStarts(PackfileSection classNames)
    {
        var starts = new HashSet<int>();
        var blob = classNames.Data;
        for (int at = 0; at + 5 < blob.Length; )
        {
            int end = Array.IndexOf(blob, (byte)0, at + 5);
            if (end < 0) break;
            starts.Add(at + 5);
            at = end + 1;
        }
        return starts;
    }

    private static string? ClassNameAt(PackfileSection classNames, int at)
    {
        if (at < 0 || at >= classNames.Data.Length) return null;
        int end = Array.IndexOf(classNames.Data, (byte)0, at);
        return end < 0 ? null : Encoding.ASCII.GetString(classNames.Data, at, end - at);
    }

    // Returns false when the object's serialized shape cannot be accounted for, so the caller
    // can refuse the whole conversion instead of relocating bytes it misread. offset is the
    // start of the struct currently being walked — the containing struct for every member
    // below it, which is exactly the base a relative array's stored offset is measured from.
    private static bool Discover(HavokClassTypes types, PackfileObjects objects, PointerLayout source,
                                 PointerLayout target, Dictionary<int, int> aims, int offset, string className,
                                 List<Block> blocks, HashSet<int> seen, int depth, int dataLength)
    {
        if (depth > 12) return true;
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
                        if (!Discover(types, objects, source, target, aims, at + e * stride, member.CType,
                                      blocks, seen, depth + 1, dataLength))
                            return false;
                }
                continue;
            }

            if (member.VType == "TYPE_RELARRAY")
            {
                // The np-era header is two little-endian uint16s: the element count plus one,
                // then the payload's offset from the member site itself (hkRelArray resolves
                // `this + m_offset` in the game runtime; real files place polytope payloads
                // accordingly). The stored offset is authoritative — the payload does not have
                // to sit immediately after the object — so it is read from the header, never
                // reconstructed. No fixup points at the header and none is emitted; the
                // payload is relocated as an ordinary block and bytes 2-3 are rewritten to its
                // new distance from the relocated member site during transcoding.
                int raw = objects.ReadIntAt(at) ?? 0;
                int sizePlusOne = raw & 0xFFFF;
                int relOff = (raw >> 16) & 0xFFFF;
                if (sizePlusOne == 0) return false;
                int count = sizePlusOne - 1;
                if (count == 0) continue;

                int srcElem = RelElementWidth(types, source, member);
                int tgtElem = RelElementWidth(types, target, member);
                if (srcElem <= 0 || tgtElem <= 0) return false;

                long payloadAt = (long)at + relOff;
                long payloadLen = (long)count * srcElem;
                if (payloadAt < 0 || payloadLen <= 0 || payloadLen > dataLength - payloadAt) return false;

                var relBlock = new Block
                {
                    SourceAt = (int)payloadAt, HeaderAt = at, Kind = "relarray", Count = count,
                    SourceElemWidth = srcElem, TargetElemWidth = tgtElem, TargetLen = count * tgtElem,
                };
                if (member.VSub == "TYPE_STRUCT" && member.CType != null && types.Knows(member.CType))
                {
                    relBlock.ClassName = member.CType;
                    blocks.Add(relBlock);

                    for (int e = 0; e < count; e++)
                        if (!Discover(types, objects, source, target, aims, (int)payloadAt + e * srcElem,
                                      member.CType, blocks, seen, depth + 1, dataLength))
                            return false;
                }
                else
                {
                    // Scalar payloads are copied verbatim, so their element width must not
                    // change between widths; a pointer-sized element would otherwise be
                    // silently truncated.
                    if (srcElem != tgtElem) return false;
                    blocks.Add(relBlock);
                }
                continue;
            }

            if (member.VType is not ("TYPE_ARRAY" or "TYPE_SIMPLEARRAY")) continue;

            // ArrayAt returns null exactly when the declared count is nonzero but its pointer
            // site has no resolvable fixup (or the count or backing span is out of range); an
            // empty array without a fixup still resolves to Elements(0, 0). Copying a nonzero
            // header anyway would emit a converted file whose array advertises elements but
            // whose backing pointer was silently dropped by Remap. Fail closed instead.
            var array = objects.ArrayAt(at);
            if (array == null) return false;
            if (array.Count == 0 || !aims.ContainsKey(at)) continue;

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
                    if (!Discover(types, objects, source, target, aims, array.At + e * srcStride, member.CType,
                                  blocks, seen, depth + 1, dataLength))
                        return false;
                continue;
            }

            // Pointer, hkUlong and variant elements are pointer-width dependent, so the active
            // layout — not the fixed shipped-table width — sizes the backing block and its
            // sites: an hkUlong array strides at four bytes per element in a four-byte file,
            // and a variant is two pointers (object, then type) at any width. RelElementWidth
            // applies the same rule the walker and relative arrays use.
            // Pointer, hkUlong and variant elements are pointer-width dependent, so the active
            // layout — not the fixed shipped-table width — sizes the backing block and its
            // sites: an hkUlong array strides at four bytes per element in a four-byte file,
            // and a variant is two pointers (object, then type) at any width. RelElementWidth
            // applies the same rule the walker and relative arrays use.
            int srcWidth = RelElementWidth(types, source, member);
            int tgtWidth = RelElementWidth(types, target, member);
            if (srcWidth <= 0 || tgtWidth <= 0) continue;

            string kind = member.VSub switch
            {
                "TYPE_POINTER" => "pointer array",
                "TYPE_STRINGPTR" or "TYPE_CSTRING" => "string array",
                "TYPE_VARIANT" => "variant array",
                "TYPE_ULONG" => "ulong array",
                _ => "value array",
            };

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
        return true;
    }

    // The element width of a relative array's payload at a given pointer width, mirroring the
    // walker: pointers, strings and hkUlong are pointer-sized, variants are two pointers, and
    // structs use their laid-out size.
    private static int RelElementWidth(HavokClassTypes types, PointerLayout layout,
                                       HavokClassTypes.Member member)
    {
        if (member.VSub is "TYPE_POINTER" or "TYPE_STRINGPTR" or "TYPE_CSTRING") return layout.PointerSize;
        if (member.VSub == "TYPE_VARIANT") return 2 * layout.PointerSize;
        if (member.VSub == "TYPE_ULONG") return layout.PointerSize;
        if (member.VSub == "TYPE_STRUCT")
            return member.CType != null && types.Knows(member.CType)
                ? LayoutWalker.Of(types, member.CType, layout).Size
                : 0;
        return HavokClassTypes.Width(member.VSub);
    }

    private static void CollectTypes(HavokClassTypes types, string className, HashSet<string> into)
    {
        if (!into.Add(className)) return;
        foreach (var member in types.Members(className))
        {
            if (member.CType == null || !types.Knows(member.CType)) continue;
            if (member.VType == "TYPE_STRUCT" ||
                (member.VType is "TYPE_ARRAY" or "TYPE_SIMPLEARRAY" or "TYPE_RELARRAY" &&
                 member.VSub == "TYPE_STRUCT"))
                CollectTypes(types, member.CType, into);
        }
    }

    private static Block StringBlock(PackfileObjects objects, int at, string kind = "string") =>
        new() { SourceAt = at, Kind = kind, TargetLen = objects.RunToNull(at) };

    private static bool Transcode(HavokClassTypes types, PointerLayout source, PointerLayout target,
                                  byte[] from, byte[] to, Block block, Dictionary<int, int> siteMap,
                                  Dictionary<int, int> relSites)
    {
        switch (block.Kind)
        {
            case "object":
                return TranscodeObject(types, source, target, from, to, block.SourceAt, block.TargetAt,
                                       block.ClassName!, siteMap, relSites);

            case "struct array":
            case "relarray":
                if (block.ClassName != null)
                {
                    for (int e = 0; e < block.Count; e++)
                        if (!TranscodeObject(types, source, target, from, to,
                                             block.SourceAt + e * block.SourceElemWidth,
                                             block.TargetAt + e * block.TargetElemWidth, block.ClassName!,
                                             siteMap, relSites))
                            return false;
                    return true;
                }

                // Scalar relative-array payload: element width is width-independent (the
                // walker refuses a payload whose width would change), so copy verbatim.
                for (int e = 0; e < block.Count; e++)
                    Array.Copy(from, block.SourceAt + e * block.SourceElemWidth,
                               to, block.TargetAt + e * block.TargetElemWidth, block.SourceElemWidth);
                return true;

            case "pointer array":
            case "string array":
                for (int e = 0; e < block.Count; e++)
                    siteMap[block.SourceAt + e * block.SourceElemWidth] = block.TargetAt + e * block.TargetElemWidth;
                return true;

            case "variant array":
                // Two pointer-sized slots per element (object pointer, then type pointer);
                // both move with the layout width and both must be remapped.
                for (int e = 0; e < block.Count; e++)
                {
                    int s = block.SourceAt + e * block.SourceElemWidth;
                    int t = block.TargetAt + e * block.TargetElemWidth;
                    siteMap[s] = t;
                    siteMap[s + block.SourceElemWidth / 2] = t + block.TargetElemWidth / 2;
                }
                return true;

            case "ulong array":
                // hkUlong values are pointer-sized: narrow with an overflow check when the
                // target is narrower, exactly like a single TYPE_ULONG member.
                for (int e = 0; e < block.Count; e++)
                {
                    int s = block.SourceAt + e * block.SourceElemWidth;
                    int t = block.TargetAt + e * block.TargetElemWidth;
                    if (block.SourceElemWidth > block.TargetElemWidth)
                        for (int k = block.TargetElemWidth; k < block.SourceElemWidth; k++)
                            if (from[s + k] != 0) return false;
                    Array.Copy(from, s, to, t, Math.Min(block.SourceElemWidth, block.TargetElemWidth));
                }
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
                                        Dictionary<int, int> siteMap, Dictionary<int, int> relSites)
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

                case "TYPE_RELARRAY":
                    // Two little-endian uint16s: count+1, then the payload's distance from
                    // this struct's start. The count is preserved as stored; the distance is
                    // rewritten to the relocated payload's new position. A payload whose new
                    // distance does not fit a uint16 cannot be represented — refuse rather
                    // than emit a header pointing somewhere wrong. An empty array registers
                    // no payload block, so its header is written with a zero distance, the
                    // same value the serializers write for an empty relative array.
                    if (relSites.TryGetValue(s, out int relPayload))
                    {
                        // The stored distance is measured from the member site, not the struct
                        // start, so it must be rewritten against the relocated member site too.
                        long rel = (long)relPayload - t;
                        if (rel < 0 || rel > ushort.MaxValue) return false;
                        Array.Copy(from, s, to, t, 2);
                        BitConverter.GetBytes((ushort)rel).CopyTo(to, t + 2);
                    }
                    else
                    {
                        // No payload block: only legitimate for an empty array (count zero,
                        // encoded as 1). A header that claims elements without a registered
                        // payload means discovery never reached it — refuse rather than emit
                        // a header pointing at nothing.
                        if ((BitConverter.ToInt32(from, s) & 0xFFFF) != 1) return false;
                        Array.Copy(from, s, to, t, 2);
                        to[t + 2] = 0;
                        to[t + 3] = 0;
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

                        // TYPE_ARRAY keeps a separate capacityAndFlags word next to the count.
                        // The backing store is relocated and sized for exactly the copied number
                        // of elements, but the source header can advertise a larger capacity; a
                        // runtime append would then write past the relocated block. Preserve the
                        // flag bits but re-advertise capacity as the count that was copied.
                        if (member.VType == "TYPE_ARRAY")
                        {
                            uint flags = BitConverter.ToUInt32(from, es + source.PointerSize + 4) & 0xC0000000u;
                            uint size = BitConverter.ToUInt32(from, es + source.PointerSize);
                            BitConverter.GetBytes(flags | size).CopyTo(to, et + target.PointerSize + 4);
                        }
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
                                                 s + e * srcStride, t + e * tgtStride, member.CType,
                                                 siteMap, relSites))
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
