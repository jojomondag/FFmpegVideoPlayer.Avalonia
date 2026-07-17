using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Resolves a YouTube watch URL into a local DASH manifest consumed by the existing
/// FFmpeg player. Audio and video remain separate upstream streams and are relayed
/// in memory over loopback, so no tutorial video is stored on disk.
/// </summary>
public sealed class YouTubeMediaSourceResolver
{
    private readonly int _preferredMaximumHeight;
    private readonly YoutubeClient _youtube;

    public static YouTubeMediaSourceResolver Instance { get; } = new();

    public YouTubeMediaSourceResolver(int preferredMaximumHeight = 1080)
        : this(new YoutubeClient(), preferredMaximumHeight)
    {
    }

    internal YouTubeMediaSourceResolver(YoutubeClient youtube, int preferredMaximumHeight = 1080)
    {
        _youtube = youtube ?? throw new ArgumentNullException(nameof(youtube));
        if (preferredMaximumHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(preferredMaximumHeight));
        _preferredMaximumHeight = preferredMaximumHeight;
    }

    public static bool IsSupportedUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
    }

    public MediaSource CreateSource(string youtubeUrl)
    {
        var videoId = VideoId.Parse(youtubeUrl);
        return MediaSource.FromSessionFactory(
            $"YouTube {videoId}",
            MediaSourceKind.YouTube,
            token => ResolveSessionAsync(youtubeUrl, token));
    }

    private async ValueTask<MediaSourceSession> ResolveSessionAsync(
        string youtubeUrl,
        CancellationToken cancellationToken = default)
    {
        var videoId = VideoId.Parse(youtubeUrl);
        var videoTask = _youtube.Videos.GetAsync(videoId, cancellationToken).AsTask();
        var streamsTask = _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken).AsTask();
        await Task.WhenAll(videoTask, streamsTask).ConfigureAwait(false);

        var video = await videoTask.ConfigureAwait(false);
        var streamManifest = await streamsTask.ConfigureAwait(false);
        var duration = video.Duration;
        if (duration is null || duration <= TimeSpan.Zero)
            throw new InvalidOperationException($"YouTube video '{videoId}' did not report a duration.");

        var videoStream = SelectVideoStream(streamManifest)
            ?? throw new InvalidOperationException($"YouTube video '{videoId}' has no compatible video stream.");
        var audioStream = SelectAudioStream(streamManifest)
            ?? throw new InvalidOperationException($"YouTube video '{videoId}' has no compatible audio stream.");

        var videoRangesTask = ResolveSegmentBaseRangesAsync(videoStream, cancellationToken).AsTask();
        var audioRangesTask = ResolveSegmentBaseRangesAsync(audioStream, cancellationToken).AsTask();
        await Task.WhenAll(videoRangesTask, audioRangesTask).ConfigureAwait(false);
        var videoRanges = await videoRangesTask.ConfigureAwait(false);
        var audioRanges = await audioRangesTask.ConfigureAwait(false);

        const string manifestFileName = "youtube.mpd";
        const string videoFileName = "video.mp4";
        const string audioFileName = "audio.mp4";
        var dashManifest = BuildDashManifest(
            duration.Value,
            videoFileName,
            videoStream,
            videoRanges,
            audioFileName,
            audioStream,
            audioRanges);

        var loopback = new LoopbackMediaSession(
            manifestFileName,
            dashManifest,
            [
                new MediaResource(
                    videoFileName,
                    "video/mp4",
                    videoStream.Size.Bytes,
                    token => _youtube.Videos.Streams.GetAsync(videoStream, token)),
                new MediaResource(
                    audioFileName,
                    "audio/mp4",
                    audioStream.Size.Bytes,
                    token => _youtube.Videos.Streams.GetAsync(audioStream, token)),
            ]);
        return new MediaSourceSession(loopback.PlaybackUrl, owner: loopback);
    }

    internal IVideoStreamInfo? SelectVideoStream(StreamManifest manifest)
    {
        var compatible = manifest
            .GetVideoOnlyStreams()
            .Where(stream => stream.Container == Container.Mp4)
            .Where(stream =>
                stream.VideoCodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase)
                || stream.VideoCodec.StartsWith("h264", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (compatible.Length == 0)
            compatible = manifest.GetVideoOnlyStreams().Where(stream => stream.Container == Container.Mp4).ToArray();

        var preferred = compatible
            .Where(stream => stream.VideoResolution.Height <= _preferredMaximumHeight)
            .ToArray();
        var candidates = preferred.Length > 0 ? preferred : compatible;

        return candidates
            .OrderByDescending(stream => stream.VideoResolution.Area)
            .ThenByDescending(stream => stream.VideoQuality.Framerate)
            .ThenByDescending(stream => stream.Bitrate.BitsPerSecond)
            .FirstOrDefault();
    }

    internal static IAudioStreamInfo? SelectAudioStream(StreamManifest manifest) =>
        manifest
            .GetAudioOnlyStreams()
            .Where(stream => stream.Container == Container.Mp4)
            .OrderByDescending(stream => stream.IsAudioLanguageDefault == true)
            .ThenByDescending(stream => stream.Bitrate.BitsPerSecond)
            .FirstOrDefault();

    internal static string BuildDashManifest(
        TimeSpan duration,
        string videoFileName,
        IVideoStreamInfo video,
        SegmentBaseRanges videoRanges,
        string audioFileName,
        IAudioStreamInfo audio,
        SegmentBaseRanges audioRanges)
    {
        var builder = new StringBuilder();
        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent = true,
            OmitXmlDeclaration = true,
        });

        const string dashNamespace = "urn:mpeg:dash:schema:mpd:2011";
        writer.WriteStartElement("MPD", dashNamespace);
        writer.WriteAttributeString("type", "static");
        writer.WriteAttributeString("mediaPresentationDuration", XmlConvert.ToString(duration));
        writer.WriteAttributeString("minBufferTime", "PT1.5S");
        writer.WriteAttributeString("profiles", "urn:mpeg:dash:profile:isoff-on-demand:2011");

        writer.WriteStartElement("Period", dashNamespace);
        writer.WriteAttributeString("start", "PT0S");

        writer.WriteStartElement("AdaptationSet", dashNamespace);
        writer.WriteAttributeString("contentType", "video");
        writer.WriteAttributeString("segmentAlignment", "true");

        writer.WriteStartElement("Representation", dashNamespace);
        writer.WriteAttributeString("id", "v");
        writer.WriteAttributeString("bandwidth", video.Bitrate.BitsPerSecond.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("mimeType", "video/mp4");
        writer.WriteAttributeString("codecs", video.VideoCodec);
        writer.WriteAttributeString("width", video.VideoResolution.Width.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("height", video.VideoResolution.Height.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("frameRate", video.VideoQuality.Framerate.ToString(CultureInfo.InvariantCulture));
        WriteSegmentBase(writer, dashNamespace, videoFileName, videoRanges);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("AdaptationSet", dashNamespace);
        writer.WriteAttributeString("contentType", "audio");
        writer.WriteAttributeString("segmentAlignment", "true");

        writer.WriteStartElement("Representation", dashNamespace);
        writer.WriteAttributeString("id", "a");
        writer.WriteAttributeString("bandwidth", audio.Bitrate.BitsPerSecond.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("mimeType", "audio/mp4");
        writer.WriteAttributeString("codecs", audio.AudioCodec);
        WriteSegmentBase(writer, dashNamespace, audioFileName, audioRanges);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();
        return builder.ToString();
    }

    private async ValueTask<SegmentBaseRanges> ResolveSegmentBaseRangesAsync(
        IStreamInfo streamInfo,
        CancellationToken cancellationToken)
    {
        await using var stream = await _youtube.Videos.Streams
            .GetAsync(streamInfo, cancellationToken)
            .ConfigureAwait(false);
        return await FindSegmentBaseRangesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<SegmentBaseRanges> FindSegmentBaseRangesAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[16];
        long position = 0;
        for (var boxCount = 0; boxCount < 128 && position < 16 * 1024 * 1024; boxCount++)
        {
            stream.Position = position;
            await stream.ReadExactlyAsync(header.AsMemory(0, 8), cancellationToken).ConfigureAwait(false);
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8L;

            if (size == 1)
            {
                await stream.ReadExactlyAsync(header.AsMemory(8, 8), cancellationToken).ConfigureAwait(false);
                size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8)));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = stream.Length - position;
            }

            if (size < headerSize || position + size > stream.Length)
                throw new InvalidDataException($"Invalid MP4 box '{type}' at offset {position}.");

            if (type == "sidx")
            {
                if (position == 0)
                    throw new InvalidDataException("MP4 stream has no initialization data before its sidx box.");

                return new SegmentBaseRanges(position - 1, position, position + size - 1);
            }

            if (type is "moof" or "mdat")
                break;

            position += size;
        }

        throw new InvalidDataException("YouTube MP4 stream has no top-level sidx box.");
    }

    private static void WriteSegmentBase(
        XmlWriter writer,
        string dashNamespace,
        string fileName,
        SegmentBaseRanges ranges)
    {
        writer.WriteElementString("BaseURL", dashNamespace, fileName);
        writer.WriteStartElement("SegmentBase", dashNamespace);
        writer.WriteAttributeString(
            "indexRange",
            $"{ranges.IndexStart.ToString(CultureInfo.InvariantCulture)}-{ranges.IndexEnd.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteAttributeString("indexRangeExact", "true");
        writer.WriteStartElement("Initialization", dashNamespace);
        writer.WriteAttributeString(
            "range",
            $"0-{ranges.InitializationEnd.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }
}

internal sealed record SegmentBaseRanges(long InitializationEnd, long IndexStart, long IndexEnd);
