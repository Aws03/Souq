using System.Text;
using AwesomeAssertions;
using Souq.Application.Common.Files;

namespace Souq.Application.Tests.Common;

// النوع يُكشف من التوقيع لا من الاسم/الترويسة (ADR-0016, Phase 0 B3).
public class MediaFileInspectorTests
{
    public static TheoryData<byte[], string> KnownSignatures => new()
    {
        { new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }, ".jpg" },
        { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, ".png" },
        { Encoding.ASCII.GetBytes("GIF89a....."), ".gif" },
        { Encoding.ASCII.GetBytes("GIF87a....."), ".gif" },
        { new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 }, ".webp" },
        { new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D }, ".mp4" },
        { new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42 }, ".webm" },
    };

    [Theory]
    [MemberData(nameof(KnownSignatures))]
    public void يكشف_الأنواع_المسموحة_من_توقيعها(byte[] header, string expectedExtension)
    {
        MediaFileInspector.Detect(header)!.Extension.Should().Be(expectedExtension);
    }

    [Theory]
    [InlineData("<html><script>alert(document.cookie)</script></html>")] // HTML متنكّر كصورة
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>")] // SVG يحمل سكربت
    [InlineData("#!/bin/sh\nrm -rf /")]
    [InlineData("RIFF....WAVEfmt ")] // RIFF لكن ليس WebP
    public void يرفض_أي_محتوى_غير_مدرج(string content)
    {
        MediaFileInspector.Detect(Encoding.UTF8.GetBytes(content)).Should().BeNull();
    }

    [Fact]
    public void يرفض_الملف_الفارغ_والترويسة_المبتورة()
    {
        MediaFileInspector.Detect(ReadOnlySpan<byte>.Empty).Should().BeNull();
        MediaFileInspector.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47 }).Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_يعيد_التدفّق_لبدايته_كي_يُخزَّن_الملف_كاملاً()
    {
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 });

        var type = await MediaFileInspector.DetectAsync(stream);

        type.Should().Be(MediaFileInspector.Jpeg);
        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task DetectAsync_يرفض_تدفّقاً_لا_يدعم_الرجوع()
    {
        using var stream = new NonSeekableStream(new byte[] { 0xFF, 0xD8, 0xFF });

        var act = () => MediaFileInspector.DetectAsync(stream);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class NonSeekableStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
}
