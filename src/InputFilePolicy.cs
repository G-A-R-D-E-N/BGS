using System;
using System.IO;

namespace OpenCommonwealth.Services;

internal static class InputFilePolicy
{
    // Fallout 4 HKX and NIF assets are expected to be far smaller than this. The deliberately
    // generous ceiling keeps unusual mod assets compatible while preventing multi-gigabyte or
    // sparse-file inputs from being allocated wholesale before their structure is validated.
    internal const long MaximumHkxBytes = 512L * 1024 * 1024;
    internal const long MaximumNifBytes = 512L * 1024 * 1024;

    internal static byte[] ReadHkx(string path) => Read(path, MaximumHkxBytes, "HKX");

    internal static byte[] ReadNif(string path) => Read(path, MaximumNifBytes, "NIF");

    internal static void EnsureHkx(long length, string name = "HKX input") =>
        Ensure(length, MaximumHkxBytes, "HKX", name);

    internal static void EnsureNif(long length, string name = "NIF input") =>
        Ensure(length, MaximumNifBytes, "NIF", name);

    private static byte[] Read(string path, long maximum, string kind)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.Read | FileShare.Delete, 81920,
                                          FileOptions.SequentialScan);
        long length = stream.Length;
        Ensure(length, maximum, kind, Path.GetFileName(path));

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)length));
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)} changed or was truncated while it was being read.");
            offset += read;
        }

        if (stream.ReadByte() != -1)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} changed or grew while it was being read.");

        return bytes;
    }

    private static void Ensure(long length, long maximum, string kind, string name)
    {
        if (length < 0)
            throw new InvalidDataException($"{name}: {kind} input has a negative length.");
        if (length > maximum)
            throw new InvalidDataException(
                $"{name}: {kind} input is {MiB(length):N1} MiB; the supported maximum is " +
                $"{MiB(maximum):N0} MiB.");
        if (length > int.MaxValue)
            throw new InvalidDataException(
                $"{name}: {kind} input is too large for an in-memory byte array.");
    }

    private static double MiB(long bytes) => bytes / (1024d * 1024d);
}
