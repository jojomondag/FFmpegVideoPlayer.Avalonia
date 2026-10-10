using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private WriteableBitmap? _fallbackBitmap;

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
            BitmapVideoFrame.Copy(_fallbackBitmap, frameData, width, height, stride);
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
            BitmapVideoFrame.Clear(_fallbackBitmap, Height);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        if (_fallbackBitmap != null && Bounds.Width > 0 && Bounds.Height > 0 && _videoWidth > 0 && _videoHeight > 0)
        {
            var sourceRect = new Rect(0, 0, _videoWidth, _videoHeight);
            var destRect = BitmapVideoFrame.CalculateDestinationRect(sourceRect, Bounds, _stretch);
            context.DrawImage(_fallbackBitmap, sourceRect, destRect);
        }
    }

    public void Dispose()
    {
        _fallbackBitmap = null;
    }
}

