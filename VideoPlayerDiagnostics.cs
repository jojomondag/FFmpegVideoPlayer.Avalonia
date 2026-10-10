using System;
using FFmpegVideoPlayer.Core;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>Identifies an optional player lifecycle or audio-output diagnostic.</summary>
public enum VideoPlayerDiagnosticKind
{
    Opening,
    Opened,
    FirstFramePresented,
    Playing,
    Paused,
    Stopped,
    Ended,
    Failed,
    Detached,
    AudioOutputReady,
    AudioOutputFailed,
    DetachedCleanupFailed
}

/// <summary>An immutable snapshot of a player lifecycle or audio-output operation.</summary>
/// <remarks>
/// Diagnostics are opt-in and are never automatically written to a file. There is
/// no explicit source, path or URL field, but media information and errors may
/// contain source details. Applications must redact these before persisting them.
/// Callbacks may run on a worker thread; dispatch to the UI thread when needed.
/// </remarks>
public sealed class VideoPlayerDiagnosticEventArgs : EventArgs
{
    public VideoPlayerDiagnosticEventArgs(
        VideoPlayerDiagnosticKind kind,
        int playerId,
        long position,
        long duration,
        MediaInfo? mediaInfo = null,
        MediaError? error = null,
        Exception? exception = null,
        int? sampleRate = null,
        int? channels = null)
    {
        Kind = kind;
        PlayerId = playerId;
        Position = position;
        Duration = duration;
        MediaInfo = mediaInfo;
        Error = error;
        Exception = exception;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public VideoPlayerDiagnosticKind Kind { get; }

    /// <summary>Identifies this control instance within the current process.</summary>
    public int PlayerId { get; }

    /// <summary>Gets the playback position in milliseconds at reporting time.</summary>
    public long Position { get; }

    /// <summary>Gets the media duration in milliseconds at reporting time.</summary>
    public long Duration { get; }

    public MediaInfo? MediaInfo { get; }
    public MediaError? Error { get; }
    public Exception? Exception { get; }
    public int? SampleRate { get; }
    public int? Channels { get; }
}
