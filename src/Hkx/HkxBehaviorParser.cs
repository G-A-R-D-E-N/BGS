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
        public string AnimationName { get; set; } = "";
        public List<BehaviorNode> Children { get; } = new();
        public List<string> StringAttributes { get; } = new();
    }

    public static List<BehaviorNode> LastObjects { get; private set; } = new();

    public static BehaviorNode? ParseBehavior(string filepath)
    {
        LastObjects = new List<BehaviorNode>();
        if (!File.Exists(filepath)) return null;

        PackfileImage image;
        try
        {
            image = PackfileImage.Read(filepath);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException
                                  or ArgumentException or OverflowException)
        {
            return null;
        }

        if (image.FileVersion != 11) return null;

        var classNames = image.Section("__classnames__");
        var data = image.Section("__data__");
        if (classNames == null || data == null) return null;

        int classNamesSection = image.Sections.IndexOf(classNames);
        int dataSection = image.Sections.IndexOf(data);
        if (classNamesSection < 0 || dataSection < 0) return null;

        int pointer = image.Layout.PointerSize;

        var fixups = new Dictionary<int, int>();
        foreach (var local in data.Locals())
        {
            if (!ValidField(local.Source, data.Data.Length, pointer) ||
                !ValidOffset(local.Destination, data.Data.Length))
                continue;

            if (!fixups.TryAdd(local.Source, local.Destination)) return null;
        }

        var objects = new List<BehaviorNode>();
        var objectMap = new Dictionary<int, BehaviorNode>();

        foreach (var virtualFixup in data.Virtuals())
        {
            if (virtualFixup.Section != classNamesSection ||
                !ValidOffset(virtualFixup.Source, data.Data.Length) ||
                !ValidOffset(virtualFixup.Destination, classNames.Data.Length))
                continue;

            string cls = ReadNullTermString(classNames.Data, virtualFixup.Destination, 256);
            if (string.IsNullOrEmpty(cls)) continue;

            if (objectMap.ContainsKey(virtualFixup.Source)) return null;

            var node = new BehaviorNode { Offset = virtualFixup.Source, ClassName = cls };
            objects.Add(node);
            objectMap[virtualFixup.Source] = node;
        }

        var globalEdges = new List<KeyValuePair<int, int>>();
        var globalSources = new HashSet<int>();
        foreach (var global in data.Globals())
        {
            if (global.Section != dataSection ||
                !ValidField(global.Source, data.Data.Length, pointer) ||
                !ValidOffset(global.Destination, data.Data.Length))
                continue;

            if (!globalSources.Add(global.Source)) return null;
            globalEdges.Add(new KeyValuePair<int, int>(global.Source, global.Destination));
        }

        var regionOwner = new Dictionary<int, BehaviorNode?>();
        foreach (var node in objects) regionOwner[node.Offset] = node;
        foreach (var fx in fixups)
            if (!regionOwner.ContainsKey(fx.Value)) regionOwner[fx.Value] = null;

        var regionStarts = regionOwner.Keys.OrderBy(x => x).ToList();
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var fx in fixups)
            {
                if (regionOwner[fx.Value] != null) continue;
                var owner = OwnerOf(regionStarts, regionOwner, fx.Key);
                if (owner != null)
                {
                    regionOwner[fx.Value] = owner;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        foreach (var edge in globalEdges)
        {
            var owner = OwnerOf(regionStarts, regionOwner, edge.Key);
            if (owner == null) continue;
            if (objectMap.TryGetValue(edge.Value, out var childNode) &&
                childNode != owner && !owner.Children.Contains(childNode))
                owner.Children.Add(childNode);
        }

        foreach (var fx in fixups.OrderBy(f => f.Key))
        {
            var owner = OwnerOf(regionStarts, regionOwner, fx.Key);
            if (owner == null || objectMap.ContainsKey(fx.Value)) continue;

            string str = ReadNullTermString(data.Data, fx.Value, 128);
            if (!IsValidAsciiString(str)) continue;
            owner.StringAttributes.Add(str);
        }

        foreach (var node in objects)
        {
            if (node.StringAttributes.Count > 0 && string.IsNullOrEmpty(node.NodeName))
                node.NodeName = node.StringAttributes[0];

            if (node.ClassName == "hkbClipGenerator" && node.StringAttributes.Count > 1)
                node.AnimationName = node.StringAttributes[1];
        }

        LastObjects = objects;

        return objects.FirstOrDefault(o => o.ClassName == "hkbBehaviorGraph") ??
               objects.FirstOrDefault(o => o.ClassName == "hkbStateMachine") ??
               objects.FirstOrDefault();
    }

    private static bool ValidOffset(int offset, int length) => offset >= 0 && offset < length;

    private static bool ValidField(int offset, int length, int width) =>
        offset >= 0 && width >= 0 && offset <= length - width;

    private static BehaviorNode? OwnerOf(List<int> starts, Dictionary<int, BehaviorNode?> owners, int offset)
    {
        int lo = 0, hi = starts.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (starts[mid] <= offset)
            {
                found = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return found < 0 ? null : owners[starts[found]];
    }

    private static string ReadNullTermString(byte[] data, int off, int maxLen)
    {
        if (off < 0 || off >= data.Length || maxLen <= 0) return "";
        int end = off;
        int limit = (int)Math.Min((long)off + maxLen, data.Length);
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
