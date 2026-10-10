using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FFmpegVideoPlayer.Core;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>The default CPU video renderer using WriteableBitmap.</summary>
public class CpuVideoRenderer : Control, IVideoRenderer
{
    private readonly BitmapVideoFrame _frame;

    public CpuVideoRenderer()
    {
        _frame = new BitmapVideoFrame(this, measureToVideo: true);
        _frame.Initialize();
    }

    /// <summary>Gets or sets the video width.</summary>
    public new int Width { get => _frame.Width; set => _frame.Width = value; }

    /// <summary>Gets or sets the video height.</summary>
    public new int Height { get => _frame.Height; set => _frame.Height = value; }

    /// <summary>Gets or sets the stretch mode for video rendering.</summary>
    public Stretch Stretch { get => _frame.Stretch; set => _frame.Stretch = value; }

    protected override Size MeasureOverride(Size availableSize) => _frame.Measure(availableSize);

    public void RenderFrame(IntPtr frameData, int width, int height, int stride) =>
        _frame.RenderFrame(frameData, width, height, stride);

    public void RenderFrame(byte[] frameData, int width, int height, int stride) =>
        _frame.RenderFrame(frameData, width, height, stride);

    public void Clear() => _frame.Clear();

    public void Dispose() => _frame.Dispose();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _frame.Render(context);
    }
}

// Both controls use bitmap rendering, but retain their existing layout behavior.
internal sealed class BitmapVideoFrame
{
    private readonly Control _owner;
    private readonly bool _measureToVideo;
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;
    private Stretch _stretch = Stretch.Uniform;

    internal BitmapVideoFrame(Control owner, bool measureToVideo = false)
    {
        _owner = owner;
        _measureToVideo = measureToVideo;
    }

    internal void Initialize()
    {
        _owner.ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(_owner, BitmapInterpolationMode.HighQuality);
    }

    internal int Width { get => _width; set => SetSize(value, _height); }
    internal int Height { get => _height; set => SetSize(_width, value); }

    internal Stretch Stretch
    {
        get => _stretch;
        set
        {
            if (_stretch == value) return;
            _stretch = value;
            _owner.InvalidateVisual();
        }
    }

    private void SetSize(int width, int height)
    {
        if (_width == width && _height == height) return;
        _width = width;
        _height = height;
        if (width <= 0 || height <= 0) return;

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            Platform.PixelFormat.Bgra8888, Platform.AlphaFormat.Premul);
        _bitmap?.Dispose();
        _bitmap = bitmap;
        if (_measureToVideo) _owner.InvalidateMeasure();
        _owner.InvalidateVisual();
    }

    internal Size Measure(Size availableSize)
    {
        if (_width <= 0 || _height <= 0) return default;
        if (_stretch == Stretch.None) return new Size(_width, _height);

        var scaleX = double.IsInfinity(availableSize.Width) ? 1.0 : availableSize.Width / _width;
        var scaleY = double.IsInfinity(availableSize.Height) ? 1.0 : availableSize.Height / _height;
        var scale = Math.Min(scaleX, scaleY);
        return new Size(_width * scale, _height * scale);
    }

    internal unsafe void RenderFrame(byte[] data, int width, int height, int stride)
    {
        if (data is null || data.Length == 0) return;
        fixed (byte* source = data)
            RenderFrame((IntPtr)source, width, height, stride, data.Length);
    }

    internal unsafe void RenderFrame(IntPtr data, int width, int height, int stride, long length = long.MaxValue)
    {
        if (data == IntPtr.Zero || width <= 0 || height <= 0) return;
        SetSize(width, height);
        if (_bitmap is null) return;

        try
        {
            using (var frame = _bitmap.Lock())
            {
                var rowLength = Math.Min(width * 4, Math.Min(stride, frame.RowBytes));
                if (rowLength > 0)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var sourceOffset = (long)y * stride;
                        if (sourceOffset + rowLength <= length)
                            Buffer.MemoryCopy((byte*)data + sourceOffset,
                                (byte*)frame.Address + (long)y * frame.RowBytes, rowLength, rowLength);
                    }
                }
            }
            _owner.InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{_owner.GetType().Name}] Failed to render frame: {ex.Message}");
        }
    }

    internal unsafe void Clear()
    {
        if (_bitmap is null) return;
        using (var frame = _bitmap.Lock())
        {
            var size = frame.RowBytes * _bitmap.PixelSize.Height;
            if (size > 0) new Span<byte>((void*)frame.Address, size).Clear();
        }
        _owner.InvalidateVisual();
    }

    internal void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    internal void Render(DrawingContext context)
    {
        if (_bitmap is null || _owner.Bounds.Width <= 0 || _owner.Bounds.Height <= 0 || _width <= 0 || _height <= 0)
            return;

        var sourceRect = new Rect(0, 0, _width, _height);
        context.DrawImage(_bitmap, sourceRect, CalculateDestinationRect(sourceRect, _owner.Bounds, _stretch));
    }

    private static Rect CalculateDestinationRect(Rect sourceRect, Rect bounds, Stretch stretch)
    {
        if (stretch == Stretch.None)
            return new Rect(
                bounds.X + (bounds.Width - sourceRect.Width) / 2,
                bounds.Y + (bounds.Height - sourceRect.Height) / 2,
                sourceRect.Width,
                sourceRect.Height);

        if (stretch is not (Stretch.Uniform or Stretch.UniformToFill))
            return bounds;

        var sourceAspect = sourceRect.Width / sourceRect.Height;
        var boundsAspect = bounds.Width / bounds.Height;
        var fitWidth = sourceAspect > boundsAspect;
        if (stretch == Stretch.UniformToFill) fitWidth = !fitWidth;

        return fitWidth
            ? new Rect(
                bounds.X,
                bounds.Y + (bounds.Height - bounds.Width / sourceAspect) / 2,
                bounds.Width,
                bounds.Width / sourceAspect)
            : new Rect(
                bounds.X + (bounds.Width - bounds.Height * sourceAspect) / 2,
                bounds.Y,
                bounds.Height * sourceAspect,
                bounds.Height);
    }
}
