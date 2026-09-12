using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

public sealed record HavokHeaderVersion(string Version, int Offset);

public static class HavokVersionProbe
{
    public const int HeaderBytes = 96;

    public static HavokHeaderVersion? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.Read | FileShare.Delete, HeaderBytes + 1,
                                          FileOptions.SequentialScan);
        // One byte past the window, so a version string running up to the boundary can be
        // told apart from one that merely ends there. See Read(ReadOnlySpan<byte>).
        Span<byte> header = stackalloc byte[HeaderBytes + 1];
        int read = 0;
        while (read < header.Length)
        {
            int got = stream.Read(header[read..]);
            if (got == 0) break;
            read += got;
        }
        return Read(header[..read]);
    }

    public static HavokHeaderVersion? Read(ReadOnlySpan<byte> bytes)
    {
        int limit = Math.Min(bytes.Length, HeaderBytes);
        for (int i = 0; i + 3 < limit; i++)
        {
            if (bytes[i] != (byte)'h' || bytes[i + 1] != (byte)'k' || bytes[i + 2] != (byte)'_')
                continue;

            int end = i + 3;
            bool hasDigit = false;
            while (end < limit && IsVersionByte(bytes[end]))
            {
                if (bytes[end] >= (byte)'0' && bytes[end] <= (byte)'9') hasDigit = true;
                end++;
            }

            if (!hasDigit || end <= i + 3) continue;

            // The token ran to the edge of the window. If more bytes are available and the
            // next one would have continued it, the string is cut short -- and a truncated
            // version reads as a different, valid-looking one, so report nothing instead.
            // With no byte to peek at, the token is taken as complete.
            if (end == limit && limit < bytes.Length && IsVersionByte(bytes[limit])) return null;

            return new HavokHeaderVersion(System.Text.Encoding.ASCII.GetString(bytes[i..end]), i);
        }
        return null;
    }

    private static bool IsVersionByte(byte value) =>
        value is >= (byte)'0' and <= (byte)'9'
        or >= (byte)'A' and <= (byte)'Z'
        or >= (byte)'a' and <= (byte)'z'
        or (byte)'_' or (byte)'.' or (byte)'-';
}
