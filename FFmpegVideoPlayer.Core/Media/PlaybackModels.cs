namespace FFmpegVideoPlayer.Core;

/// <summary>Represents the complete lifecycle of a media player.</summary>
public enum PlaybackState
{
    Closed,
    Opening,
    Ready,
    Playing,
    Paused,
    Stopped,
    Ended,
    Failed,
    Disposed
}

/// <summary>Stable error categories suitable for application logic.</summary>
public enum MediaErrorCode
{
    Unknown,
    InvalidSource,
    SourceNotFound,
    Network,
    Timeout,
    Cancelled,
    UnsupportedFormat,
    DecoderUnavailable,
    AudioOutputUnavailable,
    NativeLibraryUnavailable,
    PlaybackFailed
}

/// <summary>Describes a media failure without exposing credentials from its source URL.</summary>
public sealed record MediaError(
    MediaErrorCode Code,
    string Message,
    int? NativeErrorCode = null,
    Exception? Exception = null);

/// <summary>Result returned by media open operations.</summary>
public sealed record MediaOpenResult
{
    private MediaOpenResult(bool succeeded, MediaInfo? mediaInfo, MediaError? error)
    {
        Succeeded = succeeded;
        MediaInfo = mediaInfo;
        Error = error;
    }

    public bool Succeeded { get; }
    public MediaInfo? MediaInfo { get; }
    public MediaError? Error { get; }

    public static MediaOpenResult Success(MediaInfo mediaInfo) =>
        new(true, mediaInfo ?? throw new ArgumentNullException(nameof(mediaInfo)), null);

    public static MediaOpenResult Failure(MediaError error) =>
        new(false, null, error ?? throw new ArgumentNullException(nameof(error)));
}

/// <summary>Information discovered when FFmpeg opens a source.</summary>
public sealed record MediaInfo(
    string DisplayName,
    TimeSpan Duration,
    bool HasVideo,
    bool HasAudio,
    int VideoWidth,
    int VideoHeight);

/// <summary>Controls bounded network I/O for open and playback operations.</summary>
public sealed record MediaOpenOptions
{
    public static MediaOpenOptions Default { get; } = new();

    public TimeSpan OpenTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (OpenTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(OpenTimeout));
        if (ReadTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ReadTimeout));
    }
}

public sealed class MediaFailedEventArgs : EventArgs
{
    public MediaFailedEventArgs(MediaError error, MediaSource? source = null)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
        Source = source;
    }

    public MediaError Error { get; }

    /// <summary>
    /// Gets the source that failed. This is null only for failures that happen
    /// before a source has been assigned, such as native-library initialization.
    /// </summary>
    public MediaSource? Source { get; }
}

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackStateChangedEventArgs(PlaybackState previousState, PlaybackState state)
    {
        PreviousState = previousState;
        State = state;
    }

    public PlaybackState PreviousState { get; }
    public PlaybackState State { get; }
}
