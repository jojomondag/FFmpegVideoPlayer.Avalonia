using System.Xml.Linq;
using FFmpegVideoPlayer.Core;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace FFmpegVideoPlayer.Tests;

public sealed class YouTubeMediaSourceResolverTests
{
    [Fact]
    public void Selects_highest_h264_mp4_stream_at_or_below_configured_height()
    {
        var manifest = new StreamManifest([
            Video("vp9-2160", Container.WebM, "vp09", 2160, 30, 8_000_000),
            Video("h264-2160", Container.Mp4, "avc1.640033", 2160, 30, 7_000_000),
            Video("h264-720", Container.Mp4, "avc1.64001f", 720, 60, 4_000_000),
            Video("h264-1080", Container.Mp4, "avc1.640028", 1080, 30, 5_000_000),
        ]);

        var selected = new YouTubeMediaSourceResolver(1080).SelectVideoStream(manifest);

        Assert.NotNull(selected);
        Assert.Equal("h264-1080", selected.Url);
    }

    [Fact]
    public void Dash_manifest_combines_video_and_audio_as_one_ffmpeg_source()
    {
        var video = Video("video.mp4", Container.Mp4, "avc1.640028", 1080, 30, 5_000_000);
        var audio = new AudioOnlyStreamInfo(
            "audio.m4a",
            Container.Mp4,
            new FileSize(1_000_000),
            new Bitrate(160_000),
            "mp4a.40.2",
            null,
            true);

        var xml = YouTubeMediaSourceResolver.BuildDashManifest(
            TimeSpan.FromMinutes(3),
            "video.mp4",
            video,
            new SegmentBaseRanges(740, 741, 1288),
            "audio.mp4",
            audio,
            new SegmentBaseRanges(722, 723, 1030));
        var document = XDocument.Parse(xml);
        XNamespace dash = "urn:mpeg:dash:schema:mpd:2011";

        var adaptationSets = document.Descendants(dash + "AdaptationSet").ToArray();
        Assert.Equal(2, adaptationSets.Length);
        Assert.Contains(adaptationSets, set => (string?)set.Attribute("contentType") == "video");
        Assert.Contains(adaptationSets, set => (string?)set.Attribute("contentType") == "audio");
        Assert.Equal(
            new[] { "video.mp4", "audio.mp4" },
            document.Descendants(dash + "BaseURL").Select(element => element.Value));
        Assert.Equal(
            new[] { "741-1288", "723-1030" },
            document.Descendants(dash + "SegmentBase").Select(element => (string?)element.Attribute("indexRange")));
    }

    [Fact]
    public async Task Finds_top_level_sidx_ranges_without_reading_media_payload()
    {
        var bytes = new byte[64];
        WriteBox(bytes, 0, 24, "ftyp");
        WriteBox(bytes, 24, 20, "moov");
        WriteBox(bytes, 44, 20, "sidx");
        await using var stream = new MemoryStream(bytes, writable: false);

        var ranges = await YouTubeMediaSourceResolver.FindSegmentBaseRangesAsync(stream);

        Assert.Equal(new SegmentBaseRanges(43, 44, 63), ranges);
    }

    private static VideoOnlyStreamInfo Video(
        string url,
        Container container,
        string codec,
        int height,
        int frameRate,
        long bitrate) =>
        new(
            url,
            container,
            new FileSize(10_000_000),
            new Bitrate(bitrate),
            codec,
            new VideoQuality(height, frameRate),
            new Resolution(height * 16 / 9, height));

    private static void WriteBox(byte[] destination, int offset, int size, string type)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, 4), (uint)size);
        System.Text.Encoding.ASCII.GetBytes(type, destination.AsSpan(offset + 4, 4));
    }
}
