using System.Buffers.Binary;
using System.Text;
using Marketplace.Application.Catalog;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Catalog;

// T-085: the type comes from the bytes, and metadata is removed without touching image data.
public sealed class CoverImageTests
{
    [Fact]
    public void Sniff_RecognisesTheThreeAllowedTypesOnly()
    {
        CoverImageRules.Sniff(CoverSamples.Jpeg()).ShouldBe("image/jpeg");
        CoverImageRules.Sniff(CoverSamples.Png()).ShouldBe("image/png");
        CoverImageRules.Sniff(CoverSamples.WebP()).ShouldBe("image/webp");
        CoverImageRules.Sniff("GIF89a......"u8.ToArray()).ShouldBeNull();
        CoverImageRules.Sniff("%PDF-1.7"u8.ToArray()).ShouldBeNull();
        CoverImageRules.DeclaredType("cover.JPEG").ShouldBe("image/jpeg");
        CoverImageRules.DeclaredType("cover.gif").ShouldBeNull();
    }

    [Fact]
    public void Jpeg_LosesExifXmpAndCommentsButKeepsEverythingElse()
    {
        var original = CoverSamples.Jpeg();

        var stripped = ImageMetadata.Strip(original, "image/jpeg").ShouldNotBeNull();

        Contains(stripped, "Exif").ShouldBeFalse();
        Contains(stripped, "GPS 41.38N").ShouldBeFalse();
        Contains(stripped, "http://ns.adobe.com/xap").ShouldBeFalse();
        Contains(stripped, "camera serial").ShouldBeFalse();
        Contains(stripped, "JFIF").ShouldBeTrue();
        stripped.AsSpan().EndsWith(CoverSamples.JpegTail).ShouldBeTrue("quantisation tables, scan data and EOI are unchanged");
    }

    [Fact]
    public void Png_LosesTextExifAndTimeChunks()
    {
        var stripped = ImageMetadata.Strip(CoverSamples.Png(), "image/png").ShouldNotBeNull();

        ChunkTypes(stripped).ShouldBe(["IHDR", "IDAT", "IEND"]);
        Contains(stripped, "GPS 41.38N").ShouldBeFalse();
    }

    [Fact]
    public void WebP_LosesExifAndXmpAndClearsTheirFlags()
    {
        var stripped = ImageMetadata.Strip(CoverSamples.WebP(), "image/webp").ShouldNotBeNull();

        Encoding.ASCII.GetString(stripped, 0, 4).ShouldBe("RIFF");
        BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(4)).ShouldBe((uint)(stripped.Length - 8));
        Contains(stripped, "EXIF").ShouldBeFalse();
        Contains(stripped, "XMP ").ShouldBeFalse();
        Contains(stripped, "GPS 41.38N").ShouldBeFalse();
        (stripped[20] & 0x0C).ShouldBe(0, "the EXIF and XMP flags are cleared");
        (stripped[20] & 0x10).ShouldBe(0x10, "other flags are kept");
        Contains(stripped, "VP8 ").ShouldBeTrue();
    }

    [Fact]
    public void Malformed_IsRefusedRatherThanStoredAsIs()
    {
        var truncated = CoverSamples.Jpeg()[..30];

        ImageMetadata.Strip(truncated, "image/jpeg").ShouldBeNull();
        ImageMetadata.Strip(CoverSamples.Png()[..20], "image/png").ShouldBeNull();
        ImageMetadata.Strip(CoverSamples.WebP()[..25], "image/webp").ShouldBeNull();
    }

    private static bool Contains(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static List<string> ChunkTypes(byte[] png)
    {
        var types = new List<string>();
        for (var position = 8; position < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            types.Add(Encoding.ASCII.GetString(png, position + 4, 4));
            position += 12 + length;
        }

        return types;
    }
}

// Minimal, structurally valid samples with metadata planted in every place a real file carries it.
public static class CoverSamples
{
    public static readonly byte[] JpegTail =
        [0xFF, 0xDB, 0x00, 0x05, 0x00, 0x01, 0x02, 0xFF, 0xDA, 0x00, 0x04, 0x01, 0x02, 0x11, 0x22, 0x33, 0xFF, 0x00, 0x44, 0xFF, 0xD9];

    public static byte[] Jpeg()
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(Segment(0xE0, "JFIF\0\u0001\u0001\0\0\u0001\0\u0001\0\0"u8.ToArray()));
        bytes.AddRange(Segment(0xE1, [.. "Exif\0\0"u8.ToArray(), .. "GPS 41.38N 2.17E camera serial 123"u8.ToArray()]));
        bytes.AddRange(Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:xmpmeta/>"u8.ToArray()));
        bytes.AddRange(Segment(0xFE, "camera serial 123"u8.ToArray()));
        bytes.AddRange(JpegTail);
        return [.. bytes];
    }

    public static byte[] Png()
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]));
        bytes.AddRange(Chunk("tEXt", "Comment\0GPS 41.38N"u8.ToArray()));
        bytes.AddRange(Chunk("eXIf", "MM\0*GPS 41.38N"u8.ToArray()));
        bytes.AddRange(Chunk("tIME", [7, 234, 11, 2, 9, 0, 0]));
        bytes.AddRange(Chunk("IDAT", [0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01]));
        bytes.AddRange(Chunk("IEND", []));
        return [.. bytes];
    }

    public static byte[] WebP()
    {
        var body = new List<byte>();
        body.AddRange(RiffChunk("VP8X", [0x1C, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        body.AddRange(RiffChunk("VP8 ", [0x30, 0x01, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00, 0x02]));
        body.AddRange(RiffChunk("EXIF", "MM\0*GPS 41.38N"u8.ToArray()));
        body.AddRange(RiffChunk("XMP ", "<x:xmpmeta/>"u8.ToArray()));
        var bytes = new List<byte>();
        bytes.AddRange("RIFF"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes((uint)(4 + body.Count)));
        bytes.AddRange("WEBP"u8.ToArray());
        bytes.AddRange(body);
        return [.. bytes];
    }

    private static byte[] Segment(byte marker, byte[] payload) =>
        [0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2), .. payload];

    private static byte[] Chunk(string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        return [.. length, .. Encoding.ASCII.GetBytes(type), .. data, 0, 0, 0, 0];
    }

    private static byte[] RiffChunk(string type, byte[] data)
    {
        var padded = data.Length % 2 == 1 ? [.. data, (byte)0] : data;
        return [.. Encoding.ASCII.GetBytes(type), .. BitConverter.GetBytes((uint)data.Length), .. padded];
    }
}
