using System.Buffers.Binary;

namespace Forum.Modules.Files.Tests.Unit;

/// <summary>Hand-crafted minimal image headers (valid for header-only probing) used across the Files tests.</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);            // IHDR data length
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;   // bit depth
        bytes[25] = 6;   // color type RGBA
        return bytes;    // compression/filter/interlace + CRC stay zero — the probe never reads past the dims.
    }

    public static byte[] Gif(int width, int height)
    {
        var bytes = new byte[13];
        "GIF89a"u8.ToArray().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    public static byte[] Jpeg(int width, int height)
    {
        // SOI, an APP0 (JFIF) segment and a COM segment to exercise marker walking, then SOF0 with the dims.
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange([0xFF, 0xE0, 0x00, 0x10]); // APP0, length 16
        bytes.AddRange("JFIF\0"u8.ToArray());
        bytes.AddRange(new byte[16 - 2 - 5]);
        bytes.AddRange([0xFF, 0xFE, 0x00, 0x06]); // COM, length 6
        bytes.AddRange("test"u8.ToArray());
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]); // SOF0, length 17, precision 8
        bytes.Add((byte)(height >> 8));
        bytes.Add((byte)height);
        bytes.Add((byte)(width >> 8));
        bytes.Add((byte)width);
        bytes.Add(0x03); // 3 components
        bytes.AddRange(new byte[9]);
        return [.. bytes];
    }

    public static byte[] WebPVp8x(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.ToArray().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 22);
        "WEBP"u8.ToArray().CopyTo(bytes, 8);
        "VP8X"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 10);
        var w = width - 1;
        var h = height - 1;
        bytes[24] = (byte)w;
        bytes[25] = (byte)(w >> 8);
        bytes[26] = (byte)(w >> 16);
        bytes[27] = (byte)h;
        bytes[28] = (byte)(h >> 8);
        bytes[29] = (byte)(h >> 16);
        return bytes;
    }

    public static byte[] WebPVp8l(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.ToArray().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 22);
        "WEBP"u8.ToArray().CopyTo(bytes, 8);
        "VP8L"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 9);
        bytes[20] = 0x2F;
        var packed = (uint)((width - 1) | ((height - 1) << 14));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(21), packed);
        return bytes;
    }
}
