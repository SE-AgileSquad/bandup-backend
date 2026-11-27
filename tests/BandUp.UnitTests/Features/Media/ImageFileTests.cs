using System.Buffers.Binary;
using BandUp.Application.Features.Media.Admin;
using BandUp.Infrastructure.Configuration;
using BandUp.Infrastructure.Media;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace BandUp.UnitTests.Features.Media;

public sealed class ImageFileTests
{
    [Fact]
    public void Png_Dimensions_And_Size_Are_Checked_Without_Trusting_A_Name_Or_Mime_Type()
    {
        byte[] image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        ImageFileInfo.Inspect(image).ShouldBe(new("png", "image/png"));
        BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(16, 4), 10001);
        ImageFileInfo.Inspect(image).ShouldBeNull();
        BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(16, 4), 5000);
        BinaryPrimitives.WriteInt32BigEndian(image.AsSpan(20, 4), 5000);
        ImageFileInfo.Inspect(image).ShouldBeNull();
        ImageFileInfo.Inspect(new byte[ImageFileInfo.MaxFileBytes + 1]).ShouldBeNull();
        ImageFileInfo.Inspect(null).ShouldBeNull();
        ImageFileInfo.Inspect("<svg onload='alert(1)'/>"u8.ToArray()).ShouldBeNull();
    }

    [Fact]
    public void Jpeg_Markers_Are_Parsed_And_Truncated_Segments_Are_Rejected()
    {
        byte[] jpeg = new byte[32];
        new byte[] { 255, 216, 255, 192, 0, 8, 8, 0, 10, 0, 20, 3 }.CopyTo(jpeg, 0);
        ImageFileInfo.Inspect(jpeg).ShouldBe(new("jpg", "image/jpeg"));
        jpeg[5] = 255;
        ImageFileInfo.Inspect(jpeg).ShouldBeNull();
    }

    [Theory]
    [InlineData("VP8X")]
    [InlineData("VP8L")]
    [InlineData("VP8 ")]
    public void Webp_Canvas_Headers_Are_Recognized(string kind)
    {
        byte[] webp = new byte[32];
        "RIFF"u8.CopyTo(webp);
        "WEBP"u8.CopyTo(webp.AsSpan(8));
        global::System.Text.Encoding.ASCII.GetBytes(kind).CopyTo(webp, 12);
        if (kind == "VP8L")
        {
            webp[20] = 0x2F;
        }
        if (kind == "VP8 ")
        {
            new byte[] { 0x9D, 1, 0x2A, 1, 0, 1, 0 }.CopyTo(webp, 23);
        }
        ImageFileInfo.Inspect(webp).ShouldBe(new("webp", "image/webp"));
    }

    [Fact]
    public void Arbitrary_Bytes_Do_Not_Throw_While_Inspecting()
    {
        Random random = new(47);
        for (int i = 0; i < 300; i++)
        {
            byte[] bytes = new byte[random.Next(0, 256)];
            random.NextBytes(bytes);
            Should.NotThrow(() => ImageFileInfo.Inspect(bytes));
        }
    }

    [Fact]
    public async Task Missing_Configuration_Fails_Only_Image_Operations_And_Hides_Provider_Details()
    {
        CloudinaryImageStorage storage = new(Options.Create(new CloudinarySettings()), Substitute.For<IHttpClientFactory>());
        using MemoryStream stream = new([1, 2, 3]);
        (await Should.ThrowAsync<ImageStorageException>(() => storage.UploadAsync("test", stream, "png", CancellationToken.None)))
            .Message.ShouldBe("Image upload failed.");
        await Should.ThrowAsync<ImageStorageException>(() => storage.DownloadAsync("test", "png", CancellationToken.None));
        await Should.ThrowAsync<ImageStorageException>(() => storage.DeleteAsync("test", CancellationToken.None));
    }
}
