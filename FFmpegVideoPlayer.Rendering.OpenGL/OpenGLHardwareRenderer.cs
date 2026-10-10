using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Rendering.OpenGL;

/// <summary>
/// Legacy renderer that currently renders frames through a CPU bitmap.
/// The class name is retained for compatibility; no OpenGL context is created.
/// </summary>
public class OpenGLHardwareRenderer : Control, IVideoRenderer
{
    private int _videoWidth;
    private int _videoHeight;
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
                InvalidateVisual();
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
                InvalidateVisual();
            }
        }
    }

    public OpenGLHardwareRenderer()
    {
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
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

        if (_fallbackBitmap == null || 
            _fallbackBitmap.PixelSize.Width != width || 
            _fallbackBitmap.PixelSize.Height != height)
        {
            _fallbackBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);
        }

        try
        {
            using (var fb = _fallbackBitmap.Lock())
            {
                var destPtr = fb.Address;
                var rowLength = Math.Min(width * 4, Math.Min(stride, fb.RowBytes));
                
                for (int y = 0; y < height; y++)
                {
                    var sourceOffset = y * stride;
                    var destOffset = y * fb.RowBytes;
                    
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
            Debug.WriteLine($"[OpenGLHardwareRenderer] Failed to render frame: {ex.Message}");
        }
    }

    public void RenderFrame(byte[] frameData, int width, int height, int stride)
    {
        if (frameData == null || frameData.Length == 0 || width <= 0 || height <= 0)
            return;

        unsafe
        {
            fixed (byte* ptr = frameData)
                RenderFrame((IntPtr)ptr, width, height, stride);
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
                    var size = fb.RowBytes * Height;
                    if (size > 0)
                        new Span<byte>((void*)fb.Address, size).Clear();
                }
            }
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        
        if (_fallbackBitmap != null && Bounds.Width > 0 && Bounds.Height > 0)
        {
            context.DrawImage(_fallbackBitmap, Bounds);
        }
    }

    public void Dispose()
    {
        _fallbackBitmap = null;
    }
}

