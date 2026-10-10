using Avalonia;
using Avalonia.Controls;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FFmpegVideoPlayer.Core;
using SkiaSharp;

namespace FFmpegVideoPlayer.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class BitmapVideoRendererTests
{
    [Fact]
    public Task Derived_controls_can_read_frame_properties_during_initialization() => RunUiTest(() =>
    {
        using var cpu = new InspectingCpuRenderer();
        using var openGl = new InspectingOpenGlRenderer();
        Assert.True(cpu.ClipToBounds);
        Assert.True(openGl.ClipToBounds);
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Frames_preserve_row_stride_resize_and_clear(bool legacyOpenGl, bool nativeBuffer) => RunUiTest(() =>
    {
        using IVideoRenderer renderer = legacyOpenGl ? new OpenGLVideoRenderer() : new CpuVideoRenderer();
        byte[] pixels =
        [
            0, 0, 255, 255, 0, 0, 255, 255, 99, 99, 99, 99,
            255, 0, 0, 255, 255, 0, 0, 255, 99, 99, 99, 99
        ];
        RenderFrame(renderer, pixels, 2, 2, 12, nativeBuffer);
        Assert.Equal(2, renderer.Width);
        Assert.Equal(2, renderer.Height);
        using (var image = Capture(renderer))
        {
            Assert.Equal(SKColors.Red, image.GetPixel(0, 0));
            Assert.Equal(SKColors.Blue, image.GetPixel(1, 1));
        }

        RenderFrame(renderer, [0, 255, 0, 255, 0, 255, 0, 255, 0, 255, 0, 255], 3, 1, 12, nativeBuffer);
        Assert.Equal(3, renderer.Width);
        Assert.Equal(1, renderer.Height);
        using (var image = Capture(renderer))
            Assert.Equal(SKColors.Lime, image.GetPixel(2, 0));

        renderer.Clear();
        using (var image = Capture(renderer))
            Assert.Equal((byte)0, image.GetPixel(1, 0).Alpha);

        renderer.Dispose();
        using (var image = Capture(renderer))
            Assert.Equal((byte)0, image.GetPixel(1, 0).Alpha);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Truncated_managed_frame_preserves_rows_not_present_in_buffer(bool legacyOpenGl) => RunUiTest(() =>
    {
        using IVideoRenderer renderer = legacyOpenGl ? new OpenGLVideoRenderer() : new CpuVideoRenderer();
        renderer.RenderFrame([255, 0, 0, 255, 255, 0, 0, 255], 1, 2, 4);
        renderer.RenderFrame([0, 0, 255, 255], 1, 2, 4);

        using var image = Capture(renderer);
        Assert.Equal(SKColors.Red, image.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, image.GetPixel(0, 1));
    });

    [Fact]
    public Task Cpu_measurement_preserves_native_aspect_ratio() => RunUiTest(() =>
    {
        using var renderer = new CpuVideoRenderer { Width = 200, Height = 100 };
        renderer.Measure(new Size(80, 80));
        Assert.Equal(new Size(80, 40), renderer.DesiredSize);

        renderer.Stretch = Stretch.None;
        renderer.InvalidateMeasure();
        renderer.Measure(new Size(300, 300));
        Assert.Equal(new Size(200, 100), renderer.DesiredSize);
    });

    [Theory]
    [InlineData(false, Stretch.None)]
    [InlineData(false, Stretch.Fill)]
    [InlineData(false, Stretch.Uniform)]
    [InlineData(false, Stretch.UniformToFill)]
    [InlineData(true, Stretch.None)]
    [InlineData(true, Stretch.Fill)]
    [InlineData(true, Stretch.Uniform)]
    [InlineData(true, Stretch.UniformToFill)]
    public Task Stretch_modes_preserve_centering_letterboxing_and_cropping(bool legacyOpenGl, Stretch stretch) => RunUiTest(() =>
    {
        using IVideoRenderer renderer = legacyOpenGl
            ? new OpenGLVideoRenderer { Stretch = stretch }
            : new CpuVideoRenderer { Stretch = stretch };
        byte[] row = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
        renderer.RenderFrame([.. row, .. row], 4, 2, 16);

        using var image = Capture(renderer, new PixelSize(8, 8));
        Assert.Equal((byte)255, image.GetPixel(4, 4).Alpha);
        Assert.Equal(stretch is Stretch.Fill or Stretch.UniformToFill ? (byte)255 : (byte)0,
            image.GetPixel(4, 0).Alpha);
        var left = image.GetPixel(0, 4);
        Assert.Equal(stretch == Stretch.None ? (byte)0 : (byte)255, left.Alpha);
        if (stretch == Stretch.UniformToFill)
            Assert.True(left.Green > left.Red, "Cropping should remove the red stripe at the left edge.");
        else if (stretch is Stretch.Fill or Stretch.Uniform)
            Assert.True(left.Red > left.Green, "Fitting should preserve the red stripe at the left edge.");
    });

    private static unsafe void RenderFrame(IVideoRenderer renderer, byte[] pixels, int width, int height, int stride, bool nativeBuffer)
    {
        if (!nativeBuffer)
            renderer.RenderFrame(pixels, width, height, stride);
        else
            fixed (byte* data = pixels)
                renderer.RenderFrame((IntPtr)data, width, height, stride);
    }

    private static SKBitmap Capture(IVideoRenderer renderer, PixelSize? targetSize = null)
    {
        var control = (Control)renderer;
        var pixelSize = targetSize ?? new PixelSize(renderer.Width, renderer.Height);
        var size = new Size(pixelSize.Width, pixelSize.Height);
        control.Measure(size);
        control.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(pixelSize);
        bitmap.Render(control);
        using var png = new MemoryStream();
        bitmap.Save(png, PngBitmapEncoderOptions.Default);
        png.Position = 0;
        return SKBitmap.Decode(png);
    }

    private static async Task RunUiTest(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(action, CancellationToken.None);
    }

    private sealed class InspectingCpuRenderer : CpuVideoRenderer
    {
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ClipToBoundsProperty) return;
            Assert.Equal(0, Width);
            Assert.Equal(0, Height);
            Assert.Equal(Stretch.Uniform, Stretch);
        }
    }

    private sealed class InspectingOpenGlRenderer : OpenGLVideoRenderer
    {
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != ClipToBoundsProperty) return;
            Assert.Equal(0, Width);
            Assert.Equal(0, Height);
            Assert.Equal(Stretch.Uniform, Stretch);
        }
    }
}
