using Avalonia.Controls;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Headless;
using Avalonia.Media;
using IconPath = Avalonia.Controls.Shapes.Path;

namespace FFmpegVideoPlayer.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MaterialVideoIconProviderTests
{
    [Fact]
    public Task Every_material_icon_has_drawable_geometry_before_rendering() => RunUiTest(() =>
    {
        var provider = MaterialVideoIconProvider.Instance;
        Geometry[] geometries =
        [
            provider.CreatePlayIcon(), provider.CreatePauseIcon(),
            provider.CreateStopIcon(), provider.CreateFolderOpenIcon(),
            provider.CreateVolumeHighIcon(), provider.CreateVolumeOffIcon(),
            provider.CreateFullscreenIcon(), provider.CreateFullscreenExitIcon()
        ];

        Assert.All(geometries, geometry =>
            Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0,
                $"The Material icon has empty bounds: {geometry.Bounds}."));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Selecting_material_provider_populates_controls_before_first_render(bool isFullscreen) => RunUiTest(() =>
    {
        var provider = MaterialVideoIconProvider.Instance;
        var player = new VideoPlayerControl
        {
            IconProvider = provider,
            IsFullscreen = isFullscreen
        };

        (string Name, Geometry Expected)[] icons =
        [
            ("FolderIcon", provider.CreateFolderOpenIcon()),
            ("PlayPauseIcon", provider.CreatePlayIcon()),
            ("StopIcon", provider.CreateStopIcon()),
            ("VolumeIcon", provider.CreateVolumeHighIcon()),
            ("FullscreenIcon", isFullscreen ? provider.CreateFullscreenExitIcon() : provider.CreateFullscreenIcon())
        ];
        foreach (var (name, expected) in icons)
        {
            var path = Assert.IsType<IconPath>(player.FindControl<IconPath>(name));
            var actual = Assert.IsAssignableFrom<Geometry>(path.Data);
            Assert.True(actual.Bounds.Width > 0 && actual.Bounds.Height > 0, name);
            Assert.Equal(expected.Bounds, actual.Bounds);
        }
    });

    private static async Task RunUiTest(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(action, CancellationToken.None);
    }
}
