using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class MediaSourceTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=Ppejf4-YmSM")]
    [InlineData("https://youtu.be/Ppejf4-YmSM")]
    public void FromLocation_detects_supported_youtube_urls(string url)
    {
        var source = MediaSource.FromLocation(url);

        Assert.Equal(MediaSourceKind.YouTube, source.Kind);
    }

    [Fact]
    public void FromLocation_uses_local_path_for_file_uris()
    {
        var path = Path.Combine(Path.GetTempPath(), "player file.mp4");
        var source = MediaSource.FromLocation(new Uri(path).AbsoluteUri);

        Assert.Equal(MediaSourceKind.File, source.Kind);
        Assert.Equal("player file.mp4", source.DisplayName);
    }

    [Fact]
    public async Task Missing_local_file_returns_a_structured_source_not_found_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.mp4");
        using var player = new FFmpegMediaPlayer();

        var result = await player.OpenAsync(MediaSource.FromPath(path));

        Assert.False(result.Succeeded);
        Assert.Equal(MediaErrorCode.SourceNotFound, result.Error?.Code);
    }

    [Fact]
    public void Rejects_newlines_in_http_headers_and_resource_content_types()
    {
        Assert.Throws<ArgumentException>(() => MediaSource.FromUri(
            new Uri("https://example.test/video.mp4"),
            new Dictionary<string, string> { ["Authorization"] = "secret\r\nInjected: true" }));

        Assert.Throws<ArgumentException>(() => new MediaResource(
            "video.mp4",
            "video/mp4\r\nInjected: true",
            1,
            _ => ValueTask.FromResult<Stream>(new MemoryStream([0]))));
    }
}
