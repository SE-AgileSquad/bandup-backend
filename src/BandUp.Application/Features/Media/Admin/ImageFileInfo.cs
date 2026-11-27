using System.Buffers.Binary;

namespace BandUp.Application.Features.Media.Admin;

public sealed record ImageFileInfo(string Format, string ContentType)
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxRequestBytes = MaxFileBytes + 128 * 1024;

    public static ImageFileInfo? Inspect(byte[]? bytes)
    {
        if (bytes is null || bytes.Length is < 24 or > MaxFileBytes)
        {
            return null;
        }
        ReadOnlySpan<byte> data = bytes;
        int width = 0, height = 0;
        string? format = null;
        if (data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && data.Slice(12, 4).SequenceEqual("IHDR"u8) && data.Length >= 33)
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));
            format = "png";
        }
        else if (data[..3].SequenceEqual(new byte[] { 255, 216, 255 }))
        {
            for (int pos = 2; pos + 4 < data.Length;)
            {
                if (data[pos++] != 255)
                {
                    break;
                }
                while (pos < data.Length && data[pos] == 255)
                {
                    pos++;
                }
                if (pos + 2 >= data.Length)
                {
                    break;
                }
                byte marker = data[pos++];
                if (marker is 0xDA or 0xD9)
                {
                    break;
                }
                if (marker is 0x01 or >= 0xD0 and <= 0xD7)
                {
                    continue;
                }
                int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos, 2));
                if (length < 2 || pos + length > data.Length)
                {
                    break;
                }
                if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC && length >= 8)
                {
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 3, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos + 5, 2));
                    format = "jpg";
                    break;
                }
                pos += length;
            }
        }
        else if (data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8) && data.Length >= 30)
        {
            if (data.Slice(12, 4).SequenceEqual("VP8X"u8))
            {
                width = 1 + data[24] + (data[25] << 8) + (data[26] << 16);
                height = 1 + data[27] + (data[28] << 8) + (data[29] << 16);
            }
            else if (data.Slice(12, 4).SequenceEqual("VP8 "u8) && data.Length >= 30
                && data.Slice(23, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
            }
            else if (data.Slice(12, 4).SequenceEqual("VP8L"u8) && data[20] == 0x2F)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
            }
            format = "webp";
        }
        return format is not null && width is > 0 and <= 10000 && height is > 0 and <= 10000
            && (long)width * height <= 24_000_000
            ? new(format, format == "jpg" ? "image/jpeg" : $"image/{format}") : null;
    }
}
