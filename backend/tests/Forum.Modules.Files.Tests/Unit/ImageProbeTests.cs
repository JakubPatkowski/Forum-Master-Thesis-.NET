using Forum.Modules.Files.Application.Imaging;

using Shouldly;

using Xunit;

namespace Forum.Modules.Files.Tests.Unit;

public sealed class ImageProbeTests
{
    [Fact]
    public void Identifies_png_dimensions()
    {
        ImageProbe.TryIdentify(TestImages.Png(640, 480), out var identity).ShouldBeTrue();

        identity.ContentType.ShouldBe("image/png");
        identity.Width.ShouldBe(640);
        identity.Height.ShouldBe(480);
    }

    [Fact]
    public void Identifies_gif_dimensions()
    {
        ImageProbe.TryIdentify(TestImages.Gif(320, 200), out var identity).ShouldBeTrue();

        identity.ContentType.ShouldBe("image/gif");
        identity.Width.ShouldBe(320);
        identity.Height.ShouldBe(200);
    }

    [Fact]
    public void Identifies_jpeg_dimensions_behind_app_and_comment_segments()
    {
        ImageProbe.TryIdentify(TestImages.Jpeg(1024, 768), out var identity).ShouldBeTrue();

        identity.ContentType.ShouldBe("image/jpeg");
        identity.Width.ShouldBe(1024);
        identity.Height.ShouldBe(768);
    }

    [Fact]
    public void Identifies_webp_vp8x_canvas()
    {
        ImageProbe.TryIdentify(TestImages.WebPVp8x(1920, 1080), out var identity).ShouldBeTrue();

        identity.ContentType.ShouldBe("image/webp");
        identity.Width.ShouldBe(1920);
        identity.Height.ShouldBe(1080);
    }

    [Fact]
    public void Identifies_webp_vp8l_dimensions()
    {
        ImageProbe.TryIdentify(TestImages.WebPVp8l(800, 600), out var identity).ShouldBeTrue();

        identity.ContentType.ShouldBe("image/webp");
        identity.Width.ShouldBe(800);
        identity.Height.ShouldBe(600);
    }

    [Fact]
    public void Rejects_non_image_bytes()
    {
        ImageProbe.TryIdentify("not an image at all, just text"u8, out _).ShouldBeFalse();
        ImageProbe.TryIdentify([], out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_truncated_png_header()
    {
        var truncated = TestImages.Png(640, 480).AsSpan(0, 16);

        ImageProbe.TryIdentify(truncated, out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_zero_dimensions()
    {
        ImageProbe.TryIdentify(TestImages.Png(0, 480), out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_a_jpeg_without_a_frame_header()
    {
        // SOI followed directly by EOI — a "valid" marker stream but no SOF, so no dimensions exist.
        ImageProbe.TryIdentify([0xFF, 0xD8, 0xFF, 0xD9], out _).ShouldBeFalse();
    }
}
