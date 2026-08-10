using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.Tools;



















public static class GameDefaults
{
    public sealed record Found(string ClassName, int ObjectSize, int Members, int Version,
                               Dictionary<string, string> Defaults)
    {
        public override string ToString() =>
            $"{ClassName}: {Defaults.Count} default(s) of {Members} member(s)";
    }

    public sealed record Refusal(string ClassName, string Why)
    {
        public override string ToString() => $"{ClassName}: {Why}";
    }


    private sealed class Image
    {
        public byte[] Bytes = Array.Empty<byte>();
        public ulong Base;
        public List<(string Name, uint Va, uint VSize, uint Raw, uint RSize)> Sections = new();

        public int? At(ulong va)
        {
            if (va < Base) return null;
            ulong rva = va - Base;
            foreach (var s in Sections)
                if (rva >= s.Va && rva < s.Va + Math.Max(s.VSize, s.RSize))
                {
                    long at = s.Raw + (long)(rva - s.Va);
                    return at >= 0 && at < Bytes.Length ? (int)at : null;
                }
            return null;
        }

        public static Image Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(bytes, 0x3c);
            if (bytes[pe] != 'P' || bytes[pe + 1] != 'E')
                throw new InvalidOperationException($"{Path.GetFileName(path)} is not a PE image.");

            int sections = BitConverter.ToUInt16(bytes, pe + 6);
            int optional = BitConverter.ToUInt16(bytes, pe + 20);
            int magic = BitConverter.ToUInt16(bytes, pe + 24);
            if (magic != 0x20b)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} is not 64 bit, so it is not the runtime this reads.");

            var image = new Image
            {
                Bytes = bytes,
                Base = BitConverter.ToUInt64(bytes, pe + 24 + 24),
            };

            int table = pe + 24 + optional;
            for (int i = 0; i < sections; i++)
            {
                int at = table + i * 40;
                string name = System.Text.Encoding.ASCII.GetString(bytes, at, 8).TrimEnd('\0');
                image.Sections.Add((name,
                    BitConverter.ToUInt32(bytes, at + 12),
                    BitConverter.ToUInt32(bytes, at + 8),
                    BitConverter.ToUInt32(bytes, at + 20),
                    BitConverter.ToUInt32(bytes, at + 16)));
            }
            return image;
        }
    }






    public static (List<Found> Read, List<Refusal> Refused) Of(string exePath, string symbolDump,
                                                               HavokClassTypes? types = null)
    {
        types ??= HavokClassTypes.Shipped;
        var image = Image.Read(exePath);

        var read = new List<Found>();
        var refused = new List<Refusal>();

        foreach (var (className, rva, size) in Registrations(symbolDump))
        {
            if (!types.Knows(className))
            {


                continue;
            }

            int? at = image.At(image.Base + rva);
            if (at == null) { refused.Add(new Refusal(className, "its registration is outside the image")); continue; }

            var call = ReadCall(image, at.Value, size, image.Base + rva);
            if (call == null) { refused.Add(new Refusal(className, "its registration is not the shape this reads")); continue; }

            var layout = types[className]!;
            var declared = layout.Declared;

            if (call.Members != declared.Count)
            {
                refused.Add(new Refusal(className,
                    $"the game declares {call.Members} member(s) and the table has {declared.Count}"));
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (call.Defaults != 0)
            {
                int? blob = image.At(call.Defaults);
                if (blob == null) { refused.Add(new Refusal(className, "its defaults sit outside the image")); continue; }

                for (int i = 0; i < declared.Count; i++)
                {
                    int where = BitConverter.ToInt32(image.Bytes, blob.Value + i * 4);
                    if (where < 0) continue;

                    string? spelt = Spell(image, blob.Value + where, declared[i], types, className);
                    if (spelt != null) values[declared[i].Name] = spelt;
                }
            }

            read.Add(new Found(className, call.ObjectSize, call.Members, call.Version, values));
        }

        return (read, refused);
    }


    private static string? Spell(Image image, int at, HavokClassTypes.Member member,
                                 HavokClassTypes types, string className)
    {
        if (at < 0 || at >= image.Bytes.Length) return null;

        switch (member.VType)
        {
            case "TYPE_BOOL":
                return image.Bytes[at] != 0 ? "true" : "false";

            case "TYPE_INT8":
                return ((sbyte)image.Bytes[at]).ToString(CultureInfo.InvariantCulture);
            case "TYPE_UINT8":
                return image.Bytes[at].ToString(CultureInfo.InvariantCulture);
            case "TYPE_INT16":
                return BitConverter.ToInt16(image.Bytes, at).ToString(CultureInfo.InvariantCulture);
            case "TYPE_UINT16":
                return BitConverter.ToUInt16(image.Bytes, at).ToString(CultureInfo.InvariantCulture);
            case "TYPE_INT32":
                return BitConverter.ToInt32(image.Bytes, at).ToString(CultureInfo.InvariantCulture);
            case "TYPE_UINT32":
                return BitConverter.ToUInt32(image.Bytes, at).ToString(CultureInfo.InvariantCulture);
            case "TYPE_INT64":
                return BitConverter.ToInt64(image.Bytes, at).ToString(CultureInfo.InvariantCulture);
            case "TYPE_UINT64":
                return BitConverter.ToUInt64(image.Bytes, at).ToString(CultureInfo.InvariantCulture);

            case "TYPE_REAL":



                return BitConverter.ToSingle(image.Bytes, at).ToString("G9", CultureInfo.InvariantCulture);

            case "TYPE_VECTOR4":
            case "TYPE_QUATERNION":
                return "(" + string.Join(" ", Enumerable.Range(0, 4)
                    .Select(i => BitConverter.ToSingle(image.Bytes, at + i * 4)
                                             .ToString("G9", CultureInfo.InvariantCulture))) + ")";



            case "TYPE_ENUM":
            case "TYPE_FLAGS":
            {
                int width = Math.Max(1, HavokClassTypes.Width(member.VSub));
                long value = 0;
                for (int b = 0; b < width && at + b < image.Bytes.Length; b++)
                    value |= (long)image.Bytes[at + b] << (8 * b);

                string? name = types.NameOf(className, member, value);
                return name ?? value.ToString(CultureInfo.InvariantCulture);
            }

            default:
                return null;
        }
    }

    private sealed record Call(ulong Name, ulong Parent, int ObjectSize, ulong Defaults,
                               int Members, int Version);







    private static Call? ReadCall(Image image, int at, int length, ulong va)
    {
        var reg = new Dictionary<int, ulong>();
        var stack = new Dictionary<int, ulong>();
        var bytes = image.Bytes;

        int i = 0;
        while (i < length)
        {
            int p = at + i;
            ulong here = va + (ulong)i;


            if (bytes[p] == 0x48 && bytes[p + 1] == 0x83 && (bytes[p + 2] == 0xEC || bytes[p + 2] == 0xC4))
            { i += 4; continue; }


            if (bytes[p] == 0xC7 && bytes[p + 1] == 0x44 && bytes[p + 2] == 0x24)
            { stack[bytes[p + 3]] = BitConverter.ToUInt32(bytes, p + 4); i += 8; continue; }


            if (bytes[p] == 0x33 && bytes[p + 1] == 0xC9) { reg[1] = 0; i += 2; continue; }
            if (bytes[p] == 0x33 && bytes[p + 1] == 0xD2) { reg[2] = 0; i += 2; continue; }
            if (bytes[p] == 0x33 && bytes[p + 1] == 0xC0) { reg[0] = 0; i += 2; continue; }
            if (bytes[p] == 0x45 && bytes[p + 1] == 0x33 && bytes[p + 2] == 0xC0) { reg[8] = 0; i += 3; continue; }
            if (bytes[p] == 0x45 && bytes[p + 1] == 0x33 && bytes[p + 2] == 0xC9) { reg[9] = 0; i += 3; continue; }





            if (bytes[p] == 0x8D || ((bytes[p] is 0x48 or 0x4C or 0x44) && bytes[p + 1] == 0x8D))
            {
                bool rex = bytes[p] != 0x8D;
                int op = rex ? p + 1 : p;
                bool wideDest = rex && (bytes[p] & 0x04) != 0;

                byte modrm = bytes[op + 1];
                int mod = modrm >> 6, field = (modrm >> 3) & 7, rm = modrm & 7;
                int dest = field + (wideDest ? 8 : 0);

                if (mod == 0 && rm == 5)
                {
                    reg[dest] = (ulong)((long)here + (op - p) + 6 + BitConverter.ToInt32(bytes, op + 2));
                    i += (op - p) + 6;
                    continue;
                }



                if (rm == 4) return null;

                int disp = mod switch
                {
                    0 => 0,
                    1 => (sbyte)bytes[op + 2],
                    2 => BitConverter.ToInt32(bytes, op + 2),
                    _ => int.MinValue,
                };
                if (disp == int.MinValue) return null;

                bool wideBase = rex && (bytes[p] & 0x01) != 0;
                if (!reg.TryGetValue(rm + (wideBase ? 8 : 0), out ulong baseValue)) return null;

                reg[dest] = baseValue + (ulong)(long)disp;
                i += (op - p) + 2 + (mod == 1 ? 1 : mod == 2 ? 4 : 0);
                continue;
            }


            if (bytes[p] == 0x48 && bytes[p + 1] == 0x89 && bytes[p + 3] == 0x24)
            {
                int which = bytes[p + 2] switch { 0x44 => 0, 0x4C => 1, 0x54 => 2, _ => -1 };
                if (which < 0 || !reg.TryGetValue(which, out ulong value)) return null;
                stack[bytes[p + 4]] = value;
                i += 5;
                continue;
            }


            if (bytes[p] == 0x89 && bytes[p + 2] == 0x24)
            {
                int which = bytes[p + 1] switch { 0x44 => 0, 0x4C => 1, 0x54 => 2, _ => -1 };
                if (which < 0 || !reg.TryGetValue(which, out ulong value)) return null;
                stack[bytes[p + 3]] = value;
                i += 4;
                continue;
            }


            if (bytes[p] == 0x41 && (bytes[p + 1] == 0xB8 || bytes[p + 1] == 0xB9))
            { reg[bytes[p + 1] == 0xB8 ? 8 : 9] = BitConverter.ToUInt32(bytes, p + 2); i += 6; continue; }
            if (bytes[p] is 0xB9 or 0xBA)
            { reg[bytes[p] == 0xB9 ? 1 : 2] = BitConverter.ToUInt32(bytes, p + 1); i += 5; continue; }


            if (bytes[p] == 0xE8)
            {
                if (!reg.TryGetValue(2, out ulong name) || !reg.TryGetValue(9, out ulong size))
                    return null;
                reg.TryGetValue(8, out ulong parent);

                stack.TryGetValue(0x40, out ulong _);
                if (!stack.TryGetValue(0x48, out ulong members)) return null;
                if (!stack.TryGetValue(0x50, out ulong defaults)) return null;
                stack.TryGetValue(0x68, out ulong version);

                return new Call(name, parent, (int)size, defaults, (int)members, (int)version);
            }

            return null;
        }
        return null;
    }


    private static IEnumerable<(string Name, ulong Rva, int Size)> Registrations(string dump)
    {
        const string mark = "_dynamic_initializer_for__";

        foreach (string line in File.ReadLines(dump))
        {
            int start = line.IndexOf(mark, StringComparison.Ordinal);
            if (start < 0) continue;

            int end = line.IndexOf("Class__", start, StringComparison.Ordinal);
            if (end < 0) continue;

            string name = line.Substring(start + mark.Length, end - start - mark.Length);
            if (name.Length == 0 || name.Contains("::", StringComparison.Ordinal)) continue;

            int open = line.IndexOf("[0x", StringComparison.Ordinal);
            int bar = line.IndexOf("| sizeof=", StringComparison.Ordinal);
            int close = line.IndexOf(']', bar < 0 ? 0 : bar);
            if (open < 0 || bar < 0 || close < 0) continue;

            string rvaText = line.Substring(open + 3, bar - open - 4).Trim();
            string sizeText = line.Substring(bar + 9, close - bar - 9).Trim();

            if (!ulong.TryParse(rvaText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong rva)) continue;
            if (!int.TryParse(sizeText, out int size) || size <= 0) continue;

            yield return (name, rva, size);
        }
    }
}
