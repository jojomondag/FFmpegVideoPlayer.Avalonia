using System.Xml.Linq;
using FFmpegVideoPlayer.Core;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
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
    public void Prefers_bitrate_over_frame_rate_at_the_same_resolution()
    {
        var manifest = new StreamManifest([
            Video("h264-1080-60", Container.Mp4, "avc1.640028", 1080, 60, 5_000_000),
            Video("h264-1080-high-bitrate", Container.Mp4, "avc1.640028", 1080, 30, 8_000_000),
        ]);

        var selected = new YouTubeMediaSourceResolver(1080).SelectVideoStream(manifest);

        Assert.NotNull(selected);
        Assert.Equal("h264-1080-high-bitrate", selected.Url);
    }

    [Fact]
    public void Dash_manifest_combines_video_and_audio_as_one_ffmpeg_source()
    {
        const string videoUrl = "https://cdn.example/video.mp4?sig=a&range=0-10";
        const string audioUrl = "https://cdn.example/audio.mp4?sig=b&range=0-10";
        var video = Video(videoUrl, Container.Mp4, "avc1.640028", 1080, 30, 5_000_000);
        var audio = new AudioOnlyStreamInfo(
            audioUrl,
            Container.Mp4,
            new FileSize(1_000_000),
            new Bitrate(160_000),
            "mp4a.40.2",
            null,
            true);

        var xml = YouTubeMediaSourceResolver.BuildDashManifest(
            TimeSpan.FromMinutes(3),
            videoUrl,
            video,
            new SegmentBaseRanges(740, 741, 1288),
            audioUrl,
            audio,
            new SegmentBaseRanges(722, 723, 1030));
        var document = XDocument.Parse(xml);
        XNamespace dash = "urn:mpeg:dash:schema:mpd:2011";

        var adaptationSets = document.Descendants(dash + "AdaptationSet").ToArray();
        Assert.Equal(2, adaptationSets.Length);
        Assert.Contains(adaptationSets, set => (string?)set.Attribute("contentType") == "video");
        Assert.Contains(adaptationSets, set => (string?)set.Attribute("contentType") == "audio");
        Assert.Equal(
            new[] { videoUrl, audioUrl },
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

    [Fact]
    public async Task Preload_coalesces_concurrent_resolution_and_reuses_the_cached_result()
    {
        var resolution = new TaskCompletionSource<ResolvedYouTubeMedia>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (_, _) =>
            {
                Interlocked.Increment(ref resolutionCalls);
                return resolution.Task;
            },
            cacheDuration: TimeSpan.FromMinutes(1));

        var first = resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");
        var second = resolver.PreloadAsync("https://www.youtube.com/watch?v=Ppejf4-YmSM");

        Assert.Equal(1, Volatile.Read(ref resolutionCalls));
        resolution.SetResult(Resolution());
        await Task.WhenAll(first, second);

        await resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");

        Assert.Equal(1, Volatile.Read(ref resolutionCalls));
    }

    [Fact]
    public async Task Failed_preload_is_evicted_so_the_next_attempt_can_retry()
    {
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (_, _) => Interlocked.Increment(ref resolutionCalls) == 1
                ? Task.FromException<ResolvedYouTubeMedia>(new InvalidOperationException("transient"))
                : Task.FromResult(Resolution()),
            cacheDuration: TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM"));

        await resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");

        Assert.Equal(2, Volatile.Read(ref resolutionCalls));
    }

    [Fact]
    public async Task A_new_lookup_prunes_expired_results_for_other_video_ids()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (_, _) =>
            {
                Interlocked.Increment(ref resolutionCalls);
                return Task.FromResult(Resolution());
            },
            cacheDuration: TimeSpan.FromMinutes(1),
            utcNow: () => now,
            resolutionTimeout: TimeSpan.FromSeconds(1),
            resolutionCacheCapacity: 8);

        await resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");
        Assert.Equal(1, resolver.CachedResolutionCount);

        now = now.AddMinutes(2);
        await resolver.PreloadAsync("https://youtu.be/dQw4w9WgXcQ");

        Assert.Equal(2, Volatile.Read(ref resolutionCalls));
        Assert.Equal(1, resolver.CachedResolutionCount);
    }

    [Fact]
    public async Task Cache_capacity_evicts_the_oldest_completed_resolution()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (_, _) =>
            {
                Interlocked.Increment(ref resolutionCalls);
                return Task.FromResult(Resolution());
            },
            cacheDuration: TimeSpan.FromMinutes(30),
            utcNow: () => now,
            resolutionTimeout: TimeSpan.FromSeconds(1),
            resolutionCacheCapacity: 2);

        await resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");
        now = now.AddSeconds(1);
        await resolver.PreloadAsync("https://youtu.be/dQw4w9WgXcQ");
        now = now.AddSeconds(1);
        await resolver.PreloadAsync("https://youtu.be/abcdefghijk");

        Assert.Equal(2, resolver.CachedResolutionCount);
        Assert.Equal(3, Volatile.Read(ref resolutionCalls));

        await resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");

        Assert.Equal(4, Volatile.Read(ref resolutionCalls));
        Assert.Equal(2, resolver.CachedResolutionCount);
    }

    [Fact]
    public async Task Cache_capacity_never_evicts_in_flight_work_or_duplicates_its_request()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var firstResolution = new TaskCompletionSource<ResolvedYouTubeMedia>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResolution = new TaskCompletionSource<ResolvedYouTubeMedia>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (videoId, _) =>
            {
                Interlocked.Increment(ref resolutionCalls);
                return videoId.ToString() == "Ppejf4-YmSM"
                    ? firstResolution.Task
                    : secondResolution.Task;
            },
            cacheDuration: TimeSpan.FromMinutes(30),
            utcNow: () => now,
            resolutionTimeout: TimeSpan.FromSeconds(5),
            resolutionCacheCapacity: 1);

        var first = resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");
        now = now.AddSeconds(1);
        var second = resolver.PreloadAsync("https://youtu.be/dQw4w9WgXcQ");
        var coalescedFirst = resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM");

        Assert.Equal(2, Volatile.Read(ref resolutionCalls));
        Assert.Equal(2, resolver.CachedResolutionCount);

        firstResolution.SetResult(Resolution());
        secondResolution.SetResult(Resolution());
        await Task.WhenAll(first, second, coalescedFirst);

        Assert.True(SpinWait.SpinUntil(
            () => resolver.CachedResolutionCount == 1,
            TimeSpan.FromSeconds(1)));
        Assert.Equal(2, Volatile.Read(ref resolutionCalls));
    }

    [Fact]
    public async Task Shared_resolution_has_a_cache_owned_timeout_and_is_evicted_after_timeout()
    {
        var timeoutTokenCanceled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resolutionCalls = 0;
        var resolver = new YouTubeMediaSourceResolver(
            new YoutubeClient(),
            preferredMaximumHeight: 1080,
            (_, cancellationToken) =>
            {
                Interlocked.Increment(ref resolutionCalls);
                cancellationToken.Register(() => timeoutTokenCanceled.TrySetResult());
                return new TaskCompletionSource<ResolvedYouTubeMedia>(
                    TaskCreationOptions.RunContinuationsAsynchronously).Task;
            },
            cacheDuration: TimeSpan.FromMinutes(1),
            utcNow: null,
            resolutionTimeout: TimeSpan.FromMilliseconds(50),
            resolutionCacheCapacity: 8);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            resolver.PreloadAsync("https://youtu.be/Ppejf4-YmSM"));
        await timeoutTokenCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, Volatile.Read(ref resolutionCalls));
        Assert.Equal(0, resolver.CachedResolutionCount);
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

    private static ResolvedYouTubeMedia Resolution() =>
        new(
            TimeSpan.FromMinutes(3),
            Video("video.mp4", Container.Mp4, "avc1.640028", 1080, 30, 5_000_000),
            new SegmentBaseRanges(740, 741, 1288),
            new AudioOnlyStreamInfo(
                "audio.m4a",
                Container.Mp4,
                new FileSize(1_000_000),
                new Bitrate(160_000),
                "mp4a.40.2",
                null,
                true),
            new SegmentBaseRanges(722, 723, 1030));

    private static void WriteBox(byte[] destination, int offset, int size, string type)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(destination.AsSpan(offset, 4), (uint)size);
        System.Text.Encoding.ASCII.GetBytes(type, destination.AsSpan(offset + 4, 4));
    }
}
