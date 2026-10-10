using Avalonia.Controls;
using Avalonia.Media;
using FFmpegVideoPlayer.Core;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// CPU bitmap renderer retained for compatibility with the OpenGL rendering mode.
/// This implementation does not create an OpenGL context.
/// </summary>
public class OpenGLVideoRenderer : Control, IVideoRenderer
{
    private readonly BitmapVideoFrame _frame;

    public OpenGLVideoRenderer()
    {
        _frame = new BitmapVideoFrame(this);
        _frame.Initialize();
    }

    /// <summary>Gets or sets the video width.</summary>
    public new int Width { get => _frame.Width; set => _frame.Width = value; }

    /// <summary>Gets or sets the video height.</summary>
    public new int Height { get => _frame.Height; set => _frame.Height = value; }

    /// <summary>Gets or sets the stretch mode for video rendering.</summary>
    public Stretch Stretch { get => _frame.Stretch; set => _frame.Stretch = value; }

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
