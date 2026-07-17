using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class LoopbackMediaSessionTests
{
    [Fact]
    public async Task Serves_manifest_and_seekable_media_ranges_without_disk_files()
    {
        var media = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        const string manifest = "<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" />";
        await using var session = new LoopbackMediaSession(
            "media.mpd",
            manifest,
            [CreateMemoryResource("video.mp4", "video/mp4", media)]);
        using var client = new HttpClient();

        Assert.Equal(manifest, await client.GetStringAsync(session.PlaybackUrl));

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(session.PlaybackUrl), "video.mp4"));
        request.Headers.Range = new RangeHeaderValue(5, 11);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(new byte[] { 5, 6, 7, 8, 9, 10, 11 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(32, response.Content.Headers.ContentRange?.Length);
        Assert.Equal(5, response.Content.Headers.ContentRange?.From);
        Assert.Equal(11, response.Content.Headers.ContentRange?.To);
    }

    [Fact]
    public async Task Supports_suffix_ranges_used_by_media_probe_clients()
    {
        var media = Encoding.ASCII.GetBytes("0123456789");
        await using var session = new LoopbackMediaSession(
            "media.mpd",
            "<MPD />",
            [CreateMemoryResource("audio.m4a", "audio/mp4", media)]);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(session.PlaybackUrl), "audio.m4a"));
        request.Headers.Range = new RangeHeaderValue(null, 4);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("6789", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Keeps_streaming_while_each_upstream_read_makes_progress()
    {
        var media = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
        var idleTimeout = TimeSpan.FromMilliseconds(250);
        await using var session = new LoopbackMediaSession(
            "video.mp4",
            [new MediaResource(
                "video.mp4",
                "video/mp4",
                media.LongLength,
                _ => ValueTask.FromResult<Stream>(
                    new DelayedSeekableStream(media, TimeSpan.FromMilliseconds(35))))],
            readIdleTimeout: idleTimeout);
        using var client = new HttpClient();
        var stopwatch = Stopwatch.StartNew();

        var received = await client.GetByteArrayAsync(session.PlaybackUrl);

        Assert.Equal(media, received);
        Assert.True(stopwatch.Elapsed > idleTimeout, $"Elapsed {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Bounds_non_cooperative_upstream_open_and_disposes_a_late_stream()
    {
        var releaseOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateStreamDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new LoopbackMediaSession(
            "video.mp4",
            [new MediaResource(
                "video.mp4",
                "video/mp4",
                1,
                async _ =>
                {
                    await releaseOpen.Task;
                    return new TrackingMemoryStream([1], () => lateStreamDisposed.TrySetResult());
                })],
            upstreamOpenTimeout: TimeSpan.FromMilliseconds(100));
        using var client = new HttpClient();

        Exception? exception;
        try
        {
            exception = await Record.ExceptionAsync(() => client.GetAsync(session.PlaybackUrl));
        }
        finally
        {
            releaseOpen.TrySetResult();
        }

        Assert.NotNull(exception);
        await lateStreamDisposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Closes_connections_that_do_not_finish_request_headers()
    {
        await using var session = new LoopbackMediaSession(
            "video.mp4",
            [CreateMemoryResource("video.mp4", "video/mp4", [1])],
            headerTimeout: TimeSpan.FromMilliseconds(100));
        var uri = new Uri(session.PlaybackUrl);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, uri.Port);
        await using var network = client.GetStream();
        await network.WriteAsync(Encoding.ASCII.GetBytes(
            $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: 127.0.0.1\r\n"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var bytesRead = await network.ReadAsync(new byte[1], timeout.Token);

        Assert.Equal(0, bytesRead);
    }

    private static MediaResource CreateMemoryResource(
        string fileName,
        string contentType,
        byte[] content) =>
        new(
            fileName,
            contentType,
            content.LongLength,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(content, writable: false)));

    private sealed class DelayedSeekableStream(byte[] content, TimeSpan delayPerRead) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => content.LongLength;
        public override long Position
        {
            get => _position;
            set => _position = checked((int)value);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delayPerRead, cancellationToken);
            if (_position >= content.Length)
                return 0;
            var count = Math.Min(1, Math.Min(buffer.Length, content.Length - _position));
            content.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            return Position;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TrackingMemoryStream(byte[] content, Action onDispose)
        : MemoryStream(content, writable: false)
    {
        private int _disposed;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                onDispose();
        }
    }
}
