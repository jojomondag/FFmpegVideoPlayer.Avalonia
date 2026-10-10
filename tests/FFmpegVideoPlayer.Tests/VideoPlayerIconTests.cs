using Avalonia;
using Avalonia.Controls;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SkiaSharp;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace FFmpegVideoPlayer.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class VideoPlayerIconTests
{
    private static readonly string[] IconNames =
        ["FolderIcon", "PlayPauseIcon", "StopIcon", "VolumeIcon", "FullscreenIcon"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Icons_render_before_any_interaction(bool useCustomProvider) => RunUiTest(() =>
    {
        var player = new VideoPlayerControl
        {
            ShowFullscreenButton = true,
            IconProvider = useCustomProvider ? new TestIconProvider() : null
        };
        var window = new Window { Width = 800, Height = 500, Content = player };
        try
        {
            // No Source or Media: rendering the controls does not require opening a file.
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using var png = new MemoryStream();
            frame.Save(png, PngBitmapEncoderOptions.Default);
            png.Position = 0;
            using var bitmap = SKBitmap.Decode(png);

            foreach (var name in IconNames)
            {
                var icon = FindIcon(player, name);
                var origin = icon.TranslatePoint(default, window);
                Assert.NotNull(origin);
                var darkPixels = 0;
                for (var y = (int)origin.Value.Y; y < origin.Value.Y + icon.Bounds.Height; y++)
                for (var x = (int)origin.Value.X; x < origin.Value.X + icon.Bounds.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.Alpha > 128 && pixel.Red < 100 && pixel.Green < 100 && pixel.Blue < 100)
                        darkPixels++;
                }
                Assert.True(darkPixels > 0, $"{name} did not draw any icon pixels before interaction.");
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Default_icons_have_drawable_geometry_before_any_interaction() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();

        foreach (var name in IconNames)
            AssertDrawableGeometry(player, name);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Custom_provider_supplies_all_icons_immediately(bool isFullscreen) => RunUiTest(() =>
    {
        var provider = new TestIconProvider();
        var player = new VideoPlayerControl
        {
            IsFullscreen = isFullscreen,
            IconProvider = provider
        };

        AssertProviderIcons(player, provider, isFullscreen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Changing_and_clearing_provider_refreshes_all_icons(bool isFullscreen) => RunUiTest(() =>
    {
        var first = new TestIconProvider();
        var second = new TestIconProvider();
        var player = new VideoPlayerControl
        {
            IconProvider = first,
            IsFullscreen = isFullscreen
        };

        player.IconProvider = second;
        AssertProviderIcons(player, second, isFullscreen);

        player.IconProvider = null;
        foreach (var (name, expected) in ExpectedIcons(DefaultIconProvider.Instance, isFullscreen))
        {
            var actual = AssertDrawableGeometry(player, name);
            Assert.NotSame(ProviderGeometry(first, name, isFullscreen), actual);
            Assert.NotSame(ProviderGeometry(second, name, isFullscreen), actual);
            Assert.Equal(expected.Bounds, actual.Bounds);
        }
    });

    [Fact]
    public Task Fullscreen_state_selects_the_matching_custom_icon() => RunUiTest(() =>
    {
        var provider = new TestIconProvider();
        var player = new VideoPlayerControl { IconProvider = provider };
        Assert.Same(provider.Fullscreen, FindIcon(player, "FullscreenIcon").Data);

        player.IsFullscreen = true;
        Assert.Same(provider.FullscreenExit, FindIcon(player, "FullscreenIcon").Data);

        player.IsFullscreen = false;
        Assert.Same(provider.Fullscreen, FindIcon(player, "FullscreenIcon").Data);
    });

    [Fact]
    public Task Foreground_colors_every_icon_including_fullscreen() => RunUiTest(() =>
    {
        var initialBrush = new SolidColorBrush(Color.Parse("#125F9A"));
        var player = new VideoPlayerControl { ControlForeground = initialBrush };
        AssertIconFills(player, initialBrush);

        var replacementBrush = new SolidColorBrush(Color.Parse("#D45920"));
        player.ControlForeground = replacementBrush;
        AssertIconFills(player, replacementBrush);

        player.IconProvider = new TestIconProvider();
        player.IsFullscreen = true;
        AssertIconFills(player, replacementBrush);
    });

    private static async Task RunUiTest(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        // Property tests stay detached; only the first-render regression opens a headless window.
        await session.Dispatch(action, CancellationToken.None);
    }

    private static IconPath FindIcon(VideoPlayerControl player, string name) =>
        Assert.IsType<IconPath>(player.FindControl<IconPath>(name));

    private static Geometry AssertDrawableGeometry(VideoPlayerControl player, string name)
    {
        var geometry = Assert.IsAssignableFrom<Geometry>(FindIcon(player, name).Data);
        Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0,
            $"{name} has empty geometry bounds: {geometry.Bounds}.");
        return geometry;
    }

    private static void AssertProviderIcons(VideoPlayerControl player, IIconProvider provider, bool isFullscreen)
    {
        foreach (var (name, expected) in ExpectedIcons(provider, isFullscreen))
            Assert.Same(expected, FindIcon(player, name).Data);
    }

    private static void AssertIconFills(VideoPlayerControl player, IBrush expected)
    {
        foreach (var name in IconNames)
            Assert.Same(expected, FindIcon(player, name).Fill);
    }

    private static Geometry ProviderGeometry(IIconProvider provider, string name, bool isFullscreen) =>
        ExpectedIcons(provider, isFullscreen).Single(icon => icon.Name == name).Geometry;

    private static (string Name, Geometry Geometry)[] ExpectedIcons(IIconProvider provider, bool isFullscreen) =>
        [
            ("FolderIcon", provider.CreateFolderOpenIcon()),
            ("PlayPauseIcon", provider.CreatePlayIcon()),
            ("StopIcon", provider.CreateStopIcon()),
            ("VolumeIcon", provider.CreateVolumeHighIcon()),
            ("FullscreenIcon", isFullscreen ? provider.CreateFullscreenExitIcon() : provider.CreateFullscreenIcon())
        ];

    private sealed class TestIconProvider : IIconProvider
    {
        public Geometry Play { get; } = Rectangle(30, 3, 4);
        public Geometry Pause { get; } = Rectangle(40, 4, 5);
        public Geometry Stop { get; } = Rectangle(50, 5, 6);
        public Geometry Folder { get; } = Rectangle(60, 6, 7);
        public Geometry VolumeHigh { get; } = Rectangle(70, 7, 8);
        public Geometry VolumeOff { get; } = Rectangle(80, 8, 9);
        public Geometry Fullscreen { get; } = Rectangle(90, 9, 10);
        public Geometry FullscreenExit { get; } = Rectangle(100, 10, 11);

        public Geometry CreatePlayIcon() => Play;
        public Geometry CreatePauseIcon() => Pause;
        public Geometry CreateStopIcon() => Stop;
        public Geometry CreateFolderOpenIcon() => Folder;
        public Geometry CreateVolumeHighIcon() => VolumeHigh;
        public Geometry CreateVolumeOffIcon() => VolumeOff;
        public Geometry CreateFullscreenIcon() => Fullscreen;
        public Geometry CreateFullscreenExitIcon() => FullscreenExit;

        private static Geometry Rectangle(double x, double width, double height) =>
            new RectangleGeometry { Rect = new Rect(x, 1, width, height) };
    }
}

public sealed class IconTestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<IconTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
