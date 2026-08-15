using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenCommonwealth.Services;

namespace OpenCommonwealth.Services.Nif;

public sealed class NifFile
{
    private const int MaxHeaderLength = 1024;
    private const int MaxBlockCount = 100_000;
    private const int MaxBlockTypeCount = 4096;
    private const int MaxStringCount = 100_000;
    private const int MaxGroupCount = 100_000;
    private const int MaxStringLength = 1024 * 1024;
    private const int MaxBlockSize = 512 * 1024 * 1024;

    public uint Version;
    public uint UserVersion;
    public uint BsVersion;
    public readonly List<string> BlockTypes = new();
    public readonly List<string> Strings = new();

    public readonly List<string> BlockType = new();
    public readonly List<int> BlockStart = new();
    public readonly List<int> BlockSize = new();

    public byte[] Data = Array.Empty<byte>();

    public int BlockCount => BlockType.Count;

    public static NifFile Read(string path) =>
        Parse(InputFilePolicy.ReadNif(path), Path.GetFileName(path));

    public static NifFile Parse(byte[] data, string name)
    {
        InputFilePolicy.EnsureNif(data.LongLength, name);
        try
        {
            return ParseCore(data, name);
        }
        catch (InvalidDataException ex)
            when (!ex.Message.StartsWith(name + ":", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException($"{name}: malformed NIF data", ex);
        }
    }

    private static NifFile ParseCore(byte[] data, string name)
    {
        var nif = new NifFile { Data = data };

        int end = Array.IndexOf(data, (byte)'\n');
        if (end < 0) throw Invalid(name, "has no NIF header line");
        if (end > MaxHeaderLength) throw Invalid(name, "has an implausibly long NIF header line");

        string banner = Encoding.ASCII.GetString(data, 0, end);
        if (!banner.StartsWith("Gamebryo File Format", StringComparison.Ordinal))
            throw Invalid(name, $"is not a Gamebryo file: '{banner}'");

        var reader = new CheckedReader(data, end + 1);
        nif.Version = reader.U32("version");
        byte endian = reader.Byte("endianness");
        if (endian != 1) throw Invalid(name, "is big endian, which is not read here");
        nif.UserVersion = reader.U32("user version");
        int blocks = reader.CountU32("block count", MaxBlockCount);
        nif.BsVersion = reader.U32("BS version");

        if (nif.BsVersion < 130)
            throw Invalid(
                name,
                $"is BSVersion {nif.BsVersion}; only Fallout 4's 130 and above are read here");

        for (int i = 0; i < 4; i++)
        {
            int length = reader.Byte($"metadata string {i + 1} length");
            reader.Skip(length, $"metadata string {i + 1}");
        }

        int types = reader.U16("block type count");
        if (types > MaxBlockTypeCount)
            throw Invalid(name, $"declares an implausible block type count of {types}");
        for (int i = 0; i < types; i++)
            nif.BlockTypes.Add(reader.SizedString($"block type {i}", MaxStringLength));

        var indexes = new int[blocks];
        for (int i = 0; i < blocks; i++)
        {
            indexes[i] = reader.U16($"block type index {i}");
            if (indexes[i] >= types)
                throw Invalid(name, $"block {i} has an out-of-range type index {indexes[i]}");
        }

        var sizes = new int[blocks];
        for (int i = 0; i < blocks; i++)
            sizes[i] = reader.SizeU32($"block size {i}", MaxBlockSize);

        int strings = reader.CountU32("string count", MaxStringCount);
        int declaredMaxStringLength = reader.SizeU32("maximum string length", MaxStringLength);
        for (int i = 0; i < strings; i++)
        {
            string value = reader.SizedString($"string {i}", MaxStringLength);
            if (declaredMaxStringLength != 0 && value.Length > declaredMaxStringLength)
                throw Invalid(name, $"string {i} exceeds the declared maximum string length");
            nif.Strings.Add(value);
        }

        int groups = reader.CountU32("group count", MaxGroupCount);
        reader.Skip(checked(groups * 4), "group table");

        for (int i = 0; i < blocks; i++)
        {
            nif.BlockType.Add(nif.BlockTypes[indexes[i]]);
            nif.BlockStart.Add(reader.Position);
            nif.BlockSize.Add(sizes[i]);
            reader.Skip(sizes[i], $"block {i}");
        }

        if (reader.Remaining < 4)
            throw Invalid(name, $"the block table runs {4 - reader.Remaining} bytes past the file footer");

        return nif;
    }

    public string NameOf(int block)
    {
        if (block < 0 || block >= BlockCount || BlockSize[block] < 4) return "";
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

    internal static uint U32(byte[] data, ref int at)
    {
        EnsureAvailable(data, at, 4);
        uint value = BitConverter.ToUInt32(data, at);
        at += 4;
        return value;
    }

    internal static int I32(byte[] data, ref int at)
    {
        EnsureAvailable(data, at, 4);
        int value = BitConverter.ToInt32(data, at);
        at += 4;
        return value;
    }

    internal static int U16(byte[] data, ref int at)
    {
        EnsureAvailable(data, at, 2);
        int value = BitConverter.ToUInt16(data, at);
        at += 2;
        return value;
    }

    internal static ulong U64(byte[] data, ref int at)
    {
        EnsureAvailable(data, at, 8);
        ulong value = BitConverter.ToUInt64(data, at);
        at += 8;
        return value;
    }

    internal static float F32(byte[] data, ref int at)
    {
        EnsureAvailable(data, at, 4);
        float value = BitConverter.ToSingle(data, at);
        at += 4;
        return value;
    }

    internal static float Half(byte[] data, int at)
    {
        EnsureAvailable(data, at, 2);
        return (float)BitConverter.ToHalf(data, at);
    }

    private static void EnsureAvailable(byte[] data, int at, int length)
    {
        if (at < 0 || length < 0 || at > data.Length - length)
            throw new InvalidDataException($"NIF data is truncated at offset {at}");
    }

    private static InvalidDataException Invalid(string name, string message) =>
        new($"{name}: {message}");

    private sealed class CheckedReader
    {
        private readonly byte[] _data;

        public CheckedReader(byte[] data, int position)
        {
            _data = data;
            Position = position;
        }

        public int Position { get; private set; }
        public int Remaining => _data.Length - Position;

        public byte Byte(string field)
        {
            Require(1, field);
            return _data[Position++];
        }

        public int U16(string field)
        {
            Require(2, field);
            int value = BitConverter.ToUInt16(_data, Position);
            Position += 2;
            return value;
        }

        public uint U32(string field)
        {
            Require(4, field);
            uint value = BitConverter.ToUInt32(_data, Position);
            Position += 4;
            return value;
        }

        public int CountU32(string field, int maximum)
        {
            uint value = U32(field);
            if (value > maximum)
                throw new InvalidDataException(
                    $"{field} {value} exceeds the supported maximum of {maximum}");
            return (int)value;
        }

        public int SizeU32(string field, int maximum)
        {
            uint value = U32(field);
            if (value > maximum)
                throw new InvalidDataException(
                    $"{field} {value} exceeds the supported maximum of {maximum}");
            return (int)value;
        }

        public string SizedString(string field, int maximumLength)
        {
            int length = SizeU32($"{field} length", maximumLength);
            Require(length, field);
            string value = Encoding.ASCII.GetString(_data, Position, length);
            Position += length;
            return value;
        }

        public void Skip(int length, string field)
        {
            Require(length, field);
            Position += length;
        }

        private void Require(int length, string field)
        {
            if (length < 0 || Position < 0 || Position > _data.Length - length)
                throw new InvalidDataException(
                    $"{field} runs past the end of the file at offset {Position}");
        }
    }
}
