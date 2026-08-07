using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BehaviourStudio.Tools;

// A picture, written without a display.
//
// The viewport can only be judged by looking at it, and looking at it has meant a person with the
// program open. That is a poor place for a question to end up when the question is whether one bone
// out of thirteen is drawn where it belongs: the answer is in the data, and the only thing missing
// was a way to see it.
//
// Eight bit greyscale, or truecolour when something needs marking out from the rest. PNG rather than
// a raw dump because it can be looked at, and PNG is little more than zlib around the rows once the
// four chunks are laid out and the checksums are right.
public sealed class Png
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _rgb;

    public Png(int width, int height, byte background = 18)
    {
        _width = width;
        _height = height;
        _rgb = new byte[width * height * 3];
        System.Array.Fill(_rgb, background);
    }

    public void Dot(int x, int y, byte r, byte g, byte b, int size = 1)
    {
        for (int dy = -size / 2; dy <= size / 2; dy++)
            for (int dx = -size / 2; dx <= size / 2; dx++)
            {
                int px = x + dx, py = y + dy;
                if (px < 0 || py < 0 || px >= _width || py >= _height) continue;

                int at = (py * _width + px) * 3;
                _rgb[at] = r;
                _rgb[at + 1] = g;
                _rgb[at + 2] = b;
            }
    }

    /// A straight line between two points, stepped one pixel at a time along whichever axis is
    /// longer so it has no gaps in it.
    public void Line(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int steps = Math.Max(dx, dy);
        if (steps == 0) { Dot(x0, y0, r, g, b); return; }

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Dot((int)MathF.Round(x0 + (x1 - x0) * t), (int)MathF.Round(y0 + (y1 - y0) * t), r, g, b);
        }
    }

    public void Save(string path)
    {
        using var file = File.Create(path);

        file.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new List<byte>();
        header.AddRange(BigEndian(_width));
        header.AddRange(BigEndian(_height));
        header.Add(8);      // bits per channel
        header.Add(2);      // truecolour
        header.Add(0); header.Add(0); header.Add(0);
        Chunk(file, "IHDR", header.ToArray());

        // Every row carries a filter byte in front of it. Zero, meaning the row is stored as it is:
        // the point here is a picture that can be opened, not a small one.
        var raw = new byte[_height * (_width * 3 + 1)];
        for (int y = 0; y < _height; y++)
        {
            raw[y * (_width * 3 + 1)] = 0;
            System.Array.Copy(_rgb, y * _width * 3, raw, y * (_width * 3 + 1) + 1, _width * 3);
        }

        using var squashed = new MemoryStream();
        using (var zlib = new ZLibStream(squashed, CompressionLevel.Optimal, true))
            zlib.Write(raw, 0, raw.Length);

        Chunk(file, "IDAT", squashed.ToArray());
        Chunk(file, "IEND", System.Array.Empty<byte>());
    }

    private static void Chunk(Stream file, string name, byte[] body)
    {
        file.Write(BigEndian(body.Length));

        var tagged = new byte[4 + body.Length];
        for (int i = 0; i < 4; i++) tagged[i] = (byte)name[i];
        body.CopyTo(tagged, 4);

        file.Write(tagged);
        file.Write(BigEndian(unchecked((int)Crc(tagged))));
    }

    private static byte[] BigEndian(int value) =>
        new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc(byte[] bytes)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in bytes) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
