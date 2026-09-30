using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FFmpegVideoPlayer.Core;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// CPU-based video renderer using WriteableBitmap.
/// This is the default renderer for backward compatibility.
/// </summary>
public class CpuVideoRenderer : Control, IVideoRenderer
{
    private WriteableBitmap? _bitmap;
    private int _width;
    private int _height;
    private Stretch _stretch = Stretch.Uniform;

    /// <summary>
    /// Gets or sets the video width.
    /// </summary>
    public new int Width
    {
        get => _width;
        set
        {
            if (_width != value)
            {
                _width = value;
                UpdateBitmap();
            }
        }
    }

    /// <summary>
    /// Gets or sets the video height.
    /// </summary>
    public new int Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                _height = value;
                UpdateBitmap();
            }
        }
    }

    /// <summary>
    /// Gets or sets the stretch mode for video rendering.
    /// </summary>
    public Stretch Stretch
    {
        get => _stretch;
        set
        {
            if (_stretch != value)
            {
                _stretch = value;
                InvalidateVisual();
            }
        }
    }

    public CpuVideoRenderer()
    {
        // Set up control to display bitmap
        ClipToBounds = true;
        // Use a high-quality filter when the native frame is scaled to the
        // control bounds; the default filter can make enlarged video look blocky.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    private void UpdateBitmap()
    {
        if (_width > 0 && _height > 0)
        {
            _bitmap = new WriteableBitmap(
                new PixelSize(_width, _height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_width <= 0 || _height <= 0)
            return default;

        // Report the video's native size so the layout system knows our desired dimensions
        var videoSize = new Size(_width, _height);

        if (_stretch == Stretch.None)
            return videoSize;

        // Scale to fit within available space while preserving aspect ratio
        var scaleX = double.IsInfinity(availableSize.Width) ? 1.0 : availableSize.Width / _width;
        var scaleY = double.IsInfinity(availableSize.Height) ? 1.0 : availableSize.Height / _height;
        var scale = Math.Min(scaleX, scaleY);

        return new Size(_width * scale, _height * scale);
    }

    public void RenderFrame(IntPtr frameData, int width, int height, int stride)
    {
        if (frameData == IntPtr.Zero || width <= 0 || height <= 0)
            return;

        if (_width != width || _height != height)
        {
            Width = width;
            Height = height;
        }

        if (_bitmap == null)
            return;

        try
        {
            using (var fb = _bitmap.Lock())
            {
                var destPtr = fb.Address;
                
                for (int y = 0; y < height; y++)
                {
                    var sourceOffset = y * stride;
                    var destOffset = y * fb.RowBytes;
                    var maxRowLength = Math.Min(stride, fb.RowBytes);
                    var rowLength = Math.Min(width * 4, maxRowLength);
                    
                    if (rowLength > 0)
                    {
                        unsafe
                        {
                            Buffer.MemoryCopy(
                                (void*)(frameData + sourceOffset),
                                (void*)(destPtr + destOffset),
                                rowLength,
                                rowLength);
                        }
                    }
                }
            }

            InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CpuVideoRenderer] Failed to render frame: {ex.Message}");
        }
    }

    public void RenderFrame(byte[] frameData, int width, int height, int stride)
    {
        if (frameData == null || frameData.Length == 0 || width <= 0 || height <= 0)
            return;

        if (_width != width || _height != height)
        {
            Width = width;
            Height = height;
        }

        if (_bitmap == null)
            return;

        try
        {
            using (var fb = _bitmap.Lock())
            {
                var destPtr = fb.Address;
                
                for (int y = 0; y < height; y++)
                {
                    var sourceOffset = y * stride;
                    var destOffset = y * fb.RowBytes;
                    var maxRowLength = Math.Min(stride, fb.RowBytes);
                    var rowLength = Math.Min(width * 4, maxRowLength);
                    
                    if (rowLength > 0 && sourceOffset + rowLength <= frameData.Length)
                    {
                        Marshal.Copy(frameData, sourceOffset, destPtr + destOffset, rowLength);
                    }
                }
            }

            InvalidateVisual();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CpuVideoRenderer] Failed to render frame: {ex.Message}");
        }
    }

    public void Clear()
    {
        if (_bitmap != null)
        {
            using (var fb = _bitmap.Lock())
            {
                unsafe
                {
                    // Clear to transparent (all zeros = transparent black)
                    var ptr = (byte*)fb.Address;
                    var size = fb.RowBytes * Height;
                    for (int i = 0; i < size; i++)
                    {
                        ptr[i] = 0;
                    }
                }
            }
            InvalidateVisual();
        }
    }

    public void Dispose()
    {
        _bitmap = null;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        if (_bitmap != null && Bounds.Width > 0 && Bounds.Height > 0 && _width > 0 && _height > 0)
        {
            var sourceRect = new Rect(0, 0, _width, _height);
            var destRect = CalculateDestinationRect(sourceRect, Bounds, _stretch);
            context.DrawImage(_bitmap, sourceRect, destRect);
        }
    }

    private static Rect CalculateDestinationRect(Rect sourceRect, Rect bounds, Stretch stretch)
    {
        var sourceAspect = sourceRect.Width / sourceRect.Height;
        var boundsAspect = bounds.Width / bounds.Height;

        return stretch switch
        {
            Stretch.None => new Rect(
                bounds.X + (bounds.Width - sourceRect.Width) / 2,
                bounds.Y + (bounds.Height - sourceRect.Height) / 2,
                sourceRect.Width,
                sourceRect.Height),

            Stretch.Fill => bounds,

            Stretch.Uniform => sourceAspect > boundsAspect
                ? new Rect(
                    bounds.X,
                    bounds.Y + (bounds.Height - bounds.Width / sourceAspect) / 2,
                    bounds.Width,
                    bounds.Width / sourceAspect)
                : new Rect(
                    bounds.X + (bounds.Width - bounds.Height * sourceAspect) / 2,
                    bounds.Y,
                    bounds.Height * sourceAspect,
                    bounds.Height),

            Stretch.UniformToFill => sourceAspect > boundsAspect
                ? new Rect(
                    bounds.X + (bounds.Width - bounds.Height * sourceAspect) / 2,
                    bounds.Y,
                    bounds.Height * sourceAspect,
                    bounds.Height)
                : new Rect(
                    bounds.X,
                    bounds.Y + (bounds.Height - bounds.Width / sourceAspect) / 2,
                    bounds.Width,
                    bounds.Width / sourceAspect),

            _ => bounds
        };
    }
}

