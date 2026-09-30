using System.Collections.ObjectModel;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Describes how media should be opened. A source is reusable; each open creates a
/// private session whose lifetime is owned by <see cref="FFmpegMediaPlayer"/>.
/// </summary>
public sealed class MediaSource
{
    private readonly Func<CancellationToken, ValueTask<MediaSourceSession>> _openSessionAsync;

    private MediaSource(
        string displayName,
        MediaSourceKind kind,
        Func<CancellationToken, ValueTask<MediaSourceSession>> openSessionAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName;
        Kind = kind;
        _openSessionAsync = openSessionAsync ?? throw new ArgumentNullException(nameof(openSessionAsync));
    }

    /// <summary>Gets a safe display name for diagnostics and UI.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the source kind.</summary>
    public MediaSourceKind Kind { get; }

    /// <summary>Creates a source from a local path, HTTP(S) URI, or supported provider URL.</summary>
    public static MediaSource FromLocation(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        if (YouTubeMediaSourceResolver.IsSupportedUrl(location))
            return YouTubeMediaSourceResolver.Instance.CreateSource(location);

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return uri.IsFile ? FromPath(uri.LocalPath) : FromUri(uri);

        return FromPath(location);
    }

    /// <summary>Creates a source backed by a local media file.</summary>
    public static MediaSource FromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return new MediaSource(
            Path.GetFileName(fullPath),
            MediaSourceKind.File,
            _ =>
            {
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("The media file does not exist.", fullPath);
                return ValueTask.FromResult(new MediaSourceSession(fullPath));
            });
    }

    /// <summary>Creates a direct FFmpeg URI source with optional HTTP headers and FFmpeg options.</summary>
    public static MediaSource FromUri(
        Uri uri,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? ffmpegOptions = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Media URI must be absolute.", nameof(uri));

        var safeName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = uri.Host;

        var headerCopy = CopyAndValidate(headers, nameof(headers), rejectNewLines: true);
        var optionCopy = CopyAndValidate(ffmpegOptions, nameof(ffmpegOptions), rejectNewLines: false);
        return new MediaSource(
            safeName,
            MediaSourceKind.Uri,
            _ => ValueTask.FromResult(new MediaSourceSession(uri.AbsoluteUri, headerCopy, optionCopy)));
    }

    /// <summary>
    /// Creates a seekable stream source. The stream is exposed through a private loopback
    /// range endpoint because FFmpeg can seek it reliably without temporary files.
    /// </summary>
    public static MediaSource FromSeekableStream(
        string fileName,
        string contentType,
        long length,
        Func<CancellationToken, ValueTask<Stream>> openAsync) =>
        FromResources(fileName, [new MediaResource(fileName, contentType, length, openAsync)]);

    /// <summary>Creates a source from a manifest and one or more seekable media resources.</summary>
    public static MediaSource FromManifest(
        string manifestFileName,
        string manifestContentType,
        ReadOnlyMemory<byte> manifest,
        IEnumerable<MediaResource> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestContentType);
        if (manifest.IsEmpty)
            throw new ArgumentException("Manifest content cannot be empty.", nameof(manifest));

        var manifestBytes = manifest.ToArray();
        var allResources = resources
            .Append(new MediaResource(
                manifestFileName,
                manifestContentType,
                manifestBytes.LongLength,
                _ => ValueTask.FromResult<Stream>(new MemoryStream(manifestBytes, writable: false))))
            .ToArray();
        return FromResources(manifestFileName, allResources, MediaSourceKind.Manifest);
    }

    internal static MediaSource FromSessionFactory(
        string displayName,
        MediaSourceKind kind,
        Func<CancellationToken, ValueTask<MediaSourceSession>> openSessionAsync) =>
        new(displayName, kind, openSessionAsync);

    internal ValueTask<MediaSourceSession> OpenSessionAsync(CancellationToken cancellationToken) =>
        _openSessionAsync(cancellationToken);

    private static MediaSource FromResources(
        string primaryFileName,
        IEnumerable<MediaResource> resources,
        MediaSourceKind kind = MediaSourceKind.Stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryFileName);
        var resourceArray = resources?.ToArray() ?? throw new ArgumentNullException(nameof(resources));
        if (resourceArray.Length == 0)
            throw new ArgumentException("At least one media resource is required.", nameof(resources));

        return new MediaSource(
            primaryFileName,
            kind,
            _ =>
            {
                var session = new LoopbackMediaSession(primaryFileName, resourceArray);
                return ValueTask.FromResult(new MediaSourceSession(session.PlaybackUrl, owner: session));
            });
    }

    private static IReadOnlyDictionary<string, string> CopyAndValidate(
        IReadOnlyDictionary<string, string>? values,
        string parameterName,
        bool rejectNewLines)
    {
        if (values is null || values.Count == 0)
            return MediaSourceSession.EmptyValues;

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Keys cannot be empty.", parameterName);
            if (value is null)
                throw new ArgumentException($"Value for '{key}' cannot be null.", parameterName);
            if (rejectNewLines && (key.Contains('\r') || key.Contains('\n') || value.Contains('\r') || value.Contains('\n')))
                throw new ArgumentException("HTTP headers cannot contain newlines.", parameterName);

            copy[key] = value;
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}

/// <summary>Identifies the backing mechanism used by a media source.</summary>
public enum MediaSourceKind
{
    File,
    Uri,
    Stream,
    Manifest,
    YouTube
}

/// <summary>Describes one seekable resource used by a stream or manifest source.</summary>
public sealed record MediaResource
{
    public MediaResource(
        string fileName,
        string contentType,
        long length,
        Func<CancellationToken, ValueTask<Stream>> openAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (contentType.Contains('\r') || contentType.Contains('\n'))
            throw new ArgumentException("Content type cannot contain newlines.", nameof(contentType));
        if (fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("Resource file names cannot contain directory separators.", nameof(fileName));
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Resource length must be greater than zero.");

        FileName = fileName;
        ContentType = contentType;
        Length = length;
        OpenAsync = openAsync ?? throw new ArgumentNullException(nameof(openAsync));
    }

    public string FileName { get; }
    public string ContentType { get; }
    public long Length { get; }
    public Func<CancellationToken, ValueTask<Stream>> OpenAsync { get; }
}

internal sealed class MediaSourceSession : IAsyncDisposable, IDisposable
{
    internal static IReadOnlyDictionary<string, string> EmptyValues { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly IAsyncDisposable? _asyncOwner;
    private readonly IDisposable? _owner;
    private int _disposed;

    internal MediaSourceSession(
        string location,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyDictionary<string, string>? ffmpegOptions = null,
        object? owner = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        Location = location;
        Headers = headers ?? EmptyValues;
        FFmpegOptions = ffmpegOptions ?? EmptyValues;
        _asyncOwner = owner as IAsyncDisposable;
        _owner = owner as IDisposable;
    }

    internal string Location { get; }
    internal IReadOnlyDictionary<string, string> Headers { get; }
    internal IReadOnlyDictionary<string, string> FFmpegOptions { get; }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_asyncOwner is not null)
            await _asyncOwner.DisposeAsync().ConfigureAwait(false);
        else
            _owner?.Dispose();
    }
}
