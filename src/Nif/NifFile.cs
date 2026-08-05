using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OpenCommonwealth.Services.Nif;

// The Gamebryo packfile Fallout 4 keeps meshes in. Enough of it to draw a skinned shape and no more:
// geometry, skin weights, and the bone names a shape is weighted to.
//
// Every offset below was read off the game's own files rather than recalled, and the arithmetic
// closes on itself, which is what makes that checkable. On Dogmeat.nif the block table's own sizes
// sum to eight bytes short of the file, and those eight are the footer's root count and root
// reference; each shape's declared dataSize equals numVertices * stride + numTriangles * 6 exactly;
// and BSSkin::BoneData's block size is exactly four bytes of count plus 68 per bone, 68 being a
// bounding sphere and a transform. None of that lines up by accident.
public sealed class NifFile
{
    public uint Version;
    public uint UserVersion;
    public uint BsVersion;
    public readonly List<string> BlockTypes = new();
    public readonly List<string> Strings = new();

    /// The type of each block, and where its bytes start. Sizes come from the header rather than
    /// being worked out while reading, so a block this does not understand costs nothing to skip.
    public readonly List<string> BlockType = new();
    public readonly List<int> BlockStart = new();
    public readonly List<int> BlockSize = new();

    public byte[] Data = Array.Empty<byte>();

    public int BlockCount => BlockType.Count;

    public static NifFile Read(string path) => Parse(File.ReadAllBytes(path), Path.GetFileName(path));

    public static NifFile Parse(byte[] data, string name)
    {
        var nif = new NifFile { Data = data };

        int end = Array.IndexOf(data, (byte)'\n');
        if (end < 0) throw new InvalidDataException($"{name} has no NIF header line");
        string banner = Encoding.ASCII.GetString(data, 0, end);
        if (!banner.StartsWith("Gamebryo File Format", StringComparison.Ordinal))
            throw new InvalidDataException($"{name} is not a Gamebryo file: '{banner}'");

        int at = end + 1;
        nif.Version = U32(data, ref at);
        byte endian = data[at++];
        if (endian != 1) throw new InvalidDataException($"{name} is big endian, which is not read here");
        nif.UserVersion = U32(data, ref at);
        int blocks = (int)U32(data, ref at);
        nif.BsVersion = U32(data, ref at);

        if (nif.BsVersion < 130)
            throw new InvalidDataException(
                $"{name} is BSVersion {nif.BsVersion}; only Fallout 4's 130 and above are read here");

        // Author, process script and export script, then one more for 130 and up. Each is a byte of
        // length and that length counts the trailing NUL.
        for (int i = 0; i < 4; i++)
        {
            int len = data[at++];
            at += len;
        }

        int types = U16(data, ref at);
        for (int i = 0; i < types; i++) nif.BlockTypes.Add(SizedString(data, ref at));

        var index = new int[blocks];
        for (int i = 0; i < blocks; i++) index[i] = U16(data, ref at);

        var sizes = new int[blocks];
        for (int i = 0; i < blocks; i++) sizes[i] = (int)U32(data, ref at);

        int strings = (int)U32(data, ref at);
        U32(data, ref at);
        for (int i = 0; i < strings; i++) nif.Strings.Add(SizedString(data, ref at));

        int groups = (int)U32(data, ref at);
        at += 4 * groups;

        for (int i = 0; i < blocks; i++)
        {
            nif.BlockType.Add(index[i] < nif.BlockTypes.Count ? nif.BlockTypes[index[i]] : "");
            nif.BlockStart.Add(at);
            nif.BlockSize.Add(sizes[i]);
            at += sizes[i];
        }

        // The footer is a root count and one reference per root. Anything else means the block sizes
        // and the file disagree, which would put every block start after the first bad one wrong.
        int trailing = data.Length - at;
        if (trailing < 4)
            throw new InvalidDataException(
                $"{name}: the block table runs {(-trailing)} bytes past the end of the file");

        return nif;
    }

    /// The name of a block that carries one, or empty. Every NiObjectNET starts with its name as an
    /// index into the string table, which is how a bone reference becomes a bone name.
    public string NameOf(int block)
    {
        if (block < 0 || block >= BlockCount) return "";
        int at = BlockStart[block];
        int index = (int)U32(Data, ref at);
        return index >= 0 && index < Strings.Count ? Strings[index] : "";
    }

    public IEnumerable<int> BlocksOfType(params string[] wanted)
    {
        for (int i = 0; i < BlockCount; i++)
            if (Array.IndexOf(wanted, BlockType[i]) >= 0)
                yield return i;
    }

    internal static uint U32(byte[] d, ref int at) { uint v = BitConverter.ToUInt32(d, at); at += 4; return v; }
    internal static int I32(byte[] d, ref int at) { int v = BitConverter.ToInt32(d, at); at += 4; return v; }
    internal static int U16(byte[] d, ref int at) { int v = BitConverter.ToUInt16(d, at); at += 2; return v; }
    internal static ulong U64(byte[] d, ref int at) { ulong v = BitConverter.ToUInt64(d, at); at += 8; return v; }
    internal static float F32(byte[] d, ref int at) { float v = BitConverter.ToSingle(d, at); at += 4; return v; }
    internal static float Half(byte[] d, int at) => (float)BitConverter.ToHalf(d, at);

    private static string SizedString(byte[] d, ref int at)
    {
        int len = (int)U32(d, ref at);
        string s = Encoding.ASCII.GetString(d, at, len);
        at += len;
        return s;
    }
}
