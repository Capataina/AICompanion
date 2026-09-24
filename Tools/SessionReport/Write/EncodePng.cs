#nullable enable

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace AICompanion.Tools.SessionReport;

/// <summary>
/// A PNG writer with no dependency beyond the base library: an 8-bit RGB image, one IDAT chunk, filter
/// type 0 on every scanline, compressed by <see cref="ZLibStream"/> (which writes the zlib header and the
/// Adler-32 trailer the format requires), and a CRC-32 computed here, because the framework's own CRC
/// lives in a NuGet package this reader will not take. It exists so a tick's picture can be written
/// without a graphics device, which is the only kind of picture an agent on the owner's machine may make.
///
/// <para>The format facts it rests on (PNG specification, second edition): the eight-byte signature
/// <c>89 50 4E 47 0D 0A 1A 0A</c>; each chunk is a big-endian length, a four-letter type, the data and a
/// CRC-32 over the type and the data but not the length; IHDR is width, height, bit depth 8, colour type 2
/// (truecolour), compression 0, filter 0, interlace 0; each scanline in the compressed stream is preceded by
/// its filter-type byte. A reader that got any of those wrong would still write a file, which is why the
/// self-test decodes what this writes rather than trusting that it wrote something.</para>
/// </summary>
public static class EncodePng
{
    public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>The PNG bytes of an image whose pixels are <paramref name="rgb"/>, three bytes a pixel, row by row from the top.</summary>
    public static byte[] Encode(int width, int height, byte[] rgb)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"a PNG needs a positive size; got {width}×{height}");
        if (rgb.Length != width * height * 3)
            throw new ArgumentException($"expected {width * height * 3} bytes of RGB for {width}×{height}, got {rgb.Length}", nameof(rgb));

        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: truecolour
        header[10] = 0;  // compression: deflate
        header[11] = 0;  // filter method 0
        header[12] = 0;  // no interlace
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            int stride = width * 3;
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0); // filter type None
                zlib.Write(rgb, y * stride, stride);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        output.Write(four);
        byte[] typeAndData = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, typeAndData, 4, data.Length);
        output.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(four, Crc32(typeAndData));
        output.Write(four);
    }

    /// <summary>The table for the reflected CRC-32 polynomial 0xEDB88320 the PNG format names.</summary>
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>CRC-32 as PNG defines it: initial value all ones, reflected table, final complement.</summary>
    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in bytes)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
