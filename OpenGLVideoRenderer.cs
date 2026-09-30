using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FFmpegVideoPlayer.Core;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// CPU bitmap renderer retained for compatibility with the OpenGL rendering mode.
/// This implementation does not create an OpenGL context.
/// </summary>
public class OpenGLVideoRenderer : Control, IVideoRenderer
{
    private int _videoWidth;
    private int _videoHeight;
    private Stretch _stretch = Stretch.Uniform;
    private WriteableBitmap? _fallbackBitmap; // Fallback to CPU rendering if OpenGL not available

    /// <summary>
    /// Gets or sets the video width.
    /// </summary>
    public new int Width
    {
        get => _videoWidth;
        set
        {
            if (_videoWidth != value)
            {
                _videoWidth = value;
                UpdateFallbackBitmap();
            }
        }
    }

    /// <summary>
    /// Gets or sets the video height.
    /// </summary>
    public new int Height
    {
        get => _videoHeight;
        set
        {
            if (_videoHeight != value)
            {
                _videoHeight = value;
                UpdateFallbackBitmap();
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

    public OpenGLVideoRenderer()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        Debug.WriteLine("[OpenGLVideoRenderer] Using CPU bitmap rendering.");
    }

    private void UpdateFallbackBitmap()
    {
        if (_videoWidth > 0 && _videoHeight > 0)
        {
            _fallbackBitmap = new WriteableBitmap(
                new PixelSize(_videoWidth, _videoHeight),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);
            InvalidateVisual();
        }
    }


    public void RenderFrame(IntPtr frameData, int width, int height, int stride)
    {
        if (frameData == IntPtr.Zero || width <= 0 || height <= 0)
            return;

        if (_videoWidth != width || _videoHeight != height)
        {
            Width = width;
            Height = height;
        }

        if (_fallbackBitmap == null)
            return;

        try
        {
            using (var fb = _fallbackBitmap.Lock())
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
            Debug.WriteLine($"[OpenGLVideoRenderer] Failed to render frame: {ex.Message}");
        }
    }

    public void RenderFrame(byte[] frameData, int width, int height, int stride)
    {
        if (frameData == null || frameData.Length == 0 || width <= 0 || height <= 0)
            return;

        unsafe
        {
            fixed (byte* ptr = frameData)
            {
                RenderFrame((IntPtr)ptr, width, height, stride);
            }
        }
    }

    public void Clear()
    {
        if (_fallbackBitmap != null)
        {
            using (var fb = _fallbackBitmap.Lock())
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        if (_fallbackBitmap != null && Bounds.Width > 0 && Bounds.Height > 0 && _videoWidth > 0 && _videoHeight > 0)
        {
            var sourceRect = new Rect(0, 0, _videoWidth, _videoHeight);
            var destRect = CalculateDestinationRect(sourceRect, Bounds, _stretch);
            context.DrawImage(_fallbackBitmap, sourceRect, destRect);
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

    public void Dispose()
    {
        _fallbackBitmap = null;
    }
}

