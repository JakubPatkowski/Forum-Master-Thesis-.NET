using System.Buffers.Binary;

namespace Forum.Modules.Files.Application.Imaging;

/// <summary>The sniffed identity of an uploaded object: real content type + pixel dimensions.</summary>
internal readonly record struct ImageIdentity(string ContentType, int Width, int Height);

/// <summary>
/// Identifies an image from its leading bytes: magic-byte sniffing + header-only dimension decoding for
/// PNG / JPEG / GIF / WebP. Deliberately hand-rolled: it reads a bounded prefix, never decompresses pixel
/// data, and gives commit a content-type verdict that cannot be spoofed by a declared header (ADR 0008's
/// "never trust the declared content-type"). Unknown or truncated headers simply fail identification.
/// </summary>
internal static class ImageProbe
{
    public static bool TryIdentify(ReadOnlySpan<byte> header, out ImageIdentity identity)
    {
        identity = default;
        return TryIdentifyPng(header, ref identity)
            || TryIdentifyJpeg(header, ref identity)
            || TryIdentifyGif(header, ref identity)
            || TryIdentifyWebP(header, ref identity);
    }

    // PNG: 8-byte signature, then the IHDR chunk carries width/height as big-endian u32 at offsets 16/20.
    private static bool TryIdentifyPng(ReadOnlySpan<byte> data, ref ImageIdentity identity)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 24 || !data[..8].SequenceEqual(signature) || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        return TrySet(ref identity, "image/png", width, height);
    }

    // GIF: "GIF87a"/"GIF89a", then the logical screen descriptor carries width/height as little-endian u16.
    private static bool TryIdentifyGif(ReadOnlySpan<byte> data, ref ImageIdentity identity)
    {
        if (data.Length < 10
            || !(data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8)))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        return TrySet(ref identity, "image/gif", width, height);
    }

    // JPEG: SOI, then walk the marker segments (skipping APPn/EXIF blocks by their declared length)
    // until a start-of-frame marker, whose payload is [precision u8][height u16 BE][width u16 BE].
    private static bool TryIdentifyJpeg(ReadOnlySpan<byte> data, ref ImageIdentity identity)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
            {
                return false; // Broken marker stream.
            }

            var marker = data[offset + 1];
            if (marker == 0xFF)
            {
                offset++; // Fill byte before a marker.
                continue;
            }

            // Standalone markers carry no length (TEM, RSTn); EOI/SOS mean no frame header was found.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            if (segmentLength < 2)
            {
                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (offset + 9 > data.Length)
                {
                    return false; // Truncated within the probed prefix.
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 7)..]);
                return TrySet(ref identity, "image/jpeg", width, height);
            }

            offset += 2 + segmentLength;
        }

        return false;
    }

    // SOF0–SOF15 minus DHT (C4), JPG (C8) and DAC (CC), which reuse the range but are not frame headers.
    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    // WebP: RIFF container; the first chunk is VP8 (lossy), VP8L (lossless) or VP8X (extended/canvas).
    private static bool TryIdentifyWebP(ReadOnlySpan<byte> data, ref ImageIdentity identity)
    {
        if (data.Length < 30 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var chunk = data.Slice(12, 4);
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Frame tag (3 bytes) + start code 9D 01 2A, then 14-bit width/height as little-endian u16.
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
            {
                return false;
            }

            var width = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF;
            var height = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF;
            return TrySet(ref identity, "image/webp", (uint)width, (uint)height);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // Signature byte 2F, then width-1 and height-1 packed as two 14-bit fields.
            if (data[20] != 0x2F)
            {
                return false;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            var width = (bits & 0x3FFF) + 1;
            var height = ((bits >> 14) & 0x3FFF) + 1;
            return TrySet(ref identity, "image/webp", width, height);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Flags (1) + reserved (3), then canvas width-1 and height-1 as 24-bit little-endian values.
            var width = (uint)(data[24] | (data[25] << 8) | (data[26] << 16)) + 1;
            var height = (uint)(data[27] | (data[28] << 8) | (data[29] << 16)) + 1;
            return TrySet(ref identity, "image/webp", width, height);
        }

        return false;
    }

    private static bool TrySet(ref ImageIdentity identity, string contentType, uint width, uint height)
    {
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return false;
        }

        identity = new ImageIdentity(contentType, (int)width, (int)height);
        return true;
    }
}
