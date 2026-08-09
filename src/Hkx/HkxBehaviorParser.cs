using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCommonwealth.Services.Hkx;

public class HkxBehaviorParser
{
    public class BehaviorNode
    {
        public int Offset { get; set; }
        public string ClassName { get; set; } = "";
        public string NodeName { get; set; } = "";
        
        // Attributes discovered during pointer scanning
        public string AnimationName { get; set; } = "";
        public List<BehaviorNode> Children { get; } = new();
        public List<string> StringAttributes { get; } = new();
    }

    private static readonly byte[] HkxMagic = new byte[] { 0x57, 0xE0, 0xE0, 0x57 };

    // Virtual-fixup order. Index i here is the same object as the i-th <hkobject name="#N"> in
    // hkxpack's XML for the same file; verified 322/322 and 4645/4645.
    public static List<BehaviorNode> LastObjects { get; private set; } = new();

    /// The object a file should be read from, or null when nothing could be read at all.
    ///
    /// Null means the bytes were refused: missing, too short, wrong magic, wrong version, no section
    /// header, or no objects. It does not mean the file holds no behaviour. A packfile with objects
    /// in it always resolves a root, because the search falls back to the first object when it finds
    /// no graph or machine to prefer. See the fallback below for why.
    ///
    /// So callers must not use `root == null` to tell an animation apart from a behaviour. That
    /// condition is never observed for a populated packfile, and code written on it is dead. Ask
    /// about the contents instead: an animation file lists no `hkbClipGenerator`, and
    /// `HkxBinaryReader.TryReadAnimation` says whether an animation decoded.
    public static BehaviorNode? ParseBehavior(string filepath)
    {
        if (!File.Exists(filepath)) return null;
        byte[] data = File.ReadAllBytes(filepath);
        if (data.Length < 64) return null;

        if (data[0] != HkxMagic[0] || data[1] != HkxMagic[1] ||
            data[2] != HkxMagic[2] || data[3] != HkxMagic[3])
            return null;

        int version = BitConverter.ToInt32(data, 0x0C);
        if (version != 11) return null;

        int SecHdrBase = -1;
        for (int probe = 0x40; probe <= 0x60 && SecHdrBase < 0; probe += 0x10)
        {
            if (probe + 14 <= data.Length && Encoding.ASCII.GetString(data, probe, 14) == "__classnames__")
                SecHdrBase = probe;
        }
        if (SecHdrBase < 0) return null;

        const int SecHdrStride = 0x40;

        int cnStart = 0;
        int dataAbs = 0;
        int vfAbs = 0;
        int expAbs = 0;
        int lfAbs = 0;
        int gfAbs = 0;

        for (int i = 0; i < 3; i++)
        {
            int b = SecHdrBase + i * SecHdrStride;
            if (b + 0x30 > data.Length) break;
            string name = ReadNullTermString(data, b, 16);
            int ds = BitConverter.ToInt32(data, b + 0x14);
            int lf = BitConverter.ToInt32(data, b + 0x18);
            int gf = BitConverter.ToInt32(data, b + 0x1C);
            int vf = BitConverter.ToInt32(data, b + 0x20);
            int exp = BitConverter.ToInt32(data, b + 0x24);

            if (name == "__classnames__") cnStart = ds;
            if (name == "__data__")
            {
                dataAbs = ds;
                lfAbs = ds + lf;
                gfAbs = ds + gf;
                vfAbs = ds + vf;
                expAbs = ds + exp;
            }
        }

        if (dataAbs == 0 || cnStart == 0) return null;

        // 1. Local Fixups
        var fixups = new Dictionary<int, int>();
        int pos = lfAbs;
        while (pos + 8 <= gfAbs && pos + 8 <= data.Length)
        {
            int src = BitConverter.ToInt32(data, pos);
            int dst = BitConverter.ToInt32(data, pos + 4);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            fixups[src] = dst;
            pos += 8;
        }

        // 2. Virtual Fixups (All objects)
        var objects = new List<BehaviorNode>();
        var objectMap = new Dictionary<int, BehaviorNode>();

        pos = vfAbs;
        while (pos + 12 <= expAbs && pos + 12 <= data.Length)
        {
            int src = BitConverter.ToInt32(data, pos);
            int nameOff = BitConverter.ToInt32(data, pos + 8);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            
            string cls = ReadNullTermString(data, cnStart + nameOff, 256);
            if (!string.IsNullOrEmpty(cls))
            {
                var node = new BehaviorNode { Offset = src, ClassName = cls };
                objects.Add(node);
                objectMap[src] = node;
            }
            pos += 12;
        }

        var globalEdges = new List<KeyValuePair<int, int>>();
        pos = gfAbs;
        while (pos + 12 <= vfAbs && pos + 12 <= data.Length)
        {
            int src = BitConverter.ToInt32(data, pos);
            int dst = BitConverter.ToInt32(data, pos + 8);
            if (src == unchecked((int)0xFFFFFFFF)) break;
            globalEdges.Add(new KeyValuePair<int, int>(src, dst));
            pos += 12;
        }

        var regionOwner = new Dictionary<int, BehaviorNode?>();
        foreach (var node in objects) regionOwner[node.Offset] = node;
        foreach (var fx in fixups) if (!regionOwner.ContainsKey(fx.Value)) regionOwner[fx.Value] = null;
        var regionStarts = regionOwner.Keys.ToList();
        regionStarts.Sort();

        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var fx in fixups)
            {
                if (regionOwner[fx.Value] != null) continue;
                var owner = OwnerOf(regionStarts, regionOwner, fx.Key);
                if (owner != null) { regionOwner[fx.Value] = owner; changed = true; }
            }
            if (!changed) break;
        }

        foreach (var edge in globalEdges)
        {
            var owner = OwnerOf(regionStarts, regionOwner, edge.Key);
            if (owner == null) continue;
            if (objectMap.TryGetValue(edge.Value, out var childNode) && childNode != owner && !owner.Children.Contains(childNode))
                owner.Children.Add(childNode);
        }

        foreach (var fx in fixups.OrderBy(f => f.Key))
        {
            var owner = OwnerOf(regionStarts, regionOwner, fx.Key);
            if (owner == null || objectMap.ContainsKey(fx.Value)) continue;
            string str = ReadNullTermString(data, dataAbs + fx.Value, 128);
            if (!IsValidAsciiString(str)) continue;
            owner.StringAttributes.Add(str);
        }

        foreach (var node in objects)
        {
            if (node.StringAttributes.Count > 0 && string.IsNullOrEmpty(node.NodeName))
                node.NodeName = node.StringAttributes[0];

            // hkbClipGenerator field order is name, then animationName.
            if (node.ClassName == "hkbClipGenerator" && node.StringAttributes.Count > 1)
                node.AnimationName = node.StringAttributes[1];
        }

        LastObjects = objects;

        // 4. Find the Root state machine or behavior graph
        var rootNode = objects.FirstOrDefault(o => o.ClassName == "hkbBehaviorGraph") ??
                       objects.FirstOrDefault(o => o.ClassName == "hkbStateMachine");

        // Plenty of files worth opening name neither a graph nor a machine: an animation, a skeleton,
        // a character, a ragdoll. Returning null for those would make "could not read this file" and
        // "this file is not a behaviour" the same answer, and the window needs to tell them apart to
        // decide what to show. So anything with objects in it resolves a root and the caller reads
        // what it actually found.
        //
        // `root == null` is therefore a test only for an unreadable file. It is never true for a
        // populated packfile and cannot identify an animation.
        if (rootNode == null && objects.Count > 0)
        {
            rootNode = objects[0];
        }

        return rootNode;
    }

    private static BehaviorNode? OwnerOf(List<int> starts, Dictionary<int, BehaviorNode?> owners, int offset)
    {
        int lo = 0, hi = starts.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (starts[mid] <= offset) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found < 0 ? null : owners[starts[found]];
    }

    private static string ReadNullTermString(byte[] data, int off, int maxLen)
    {
        if (off < 0 || off >= data.Length) return "";
        int end = off;
        int limit = Math.Min(off + maxLen, data.Length);
        while (end < limit && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, off, end - off);
    }

    private static bool IsValidAsciiString(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        bool hasAlphaNumeric = false;
        foreach (char c in s)
        {
            if (c < 32 || c > 126) return false;
            if (char.IsLetterOrDigit(c)) hasAlphaNumeric = true;
        }
        return hasAlphaNumeric;
    }
}
