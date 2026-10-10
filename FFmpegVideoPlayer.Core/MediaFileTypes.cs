namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Common file extensions suitable for offering a media preview. These are filename hints,
/// not a codec guarantee; opening a source through FFmpeg determines actual support.
/// </summary>
public static class MediaFileTypes
{
    /// <summary>Recognizes a common video extension, including its leading dot, ignoring case.</summary>
    public static bool IsVideoExtension(string? extension) => extension?.ToLowerInvariant() is
        ".mov" or ".avi" or ".mp4" or ".mkv" or ".webm" or ".flv" or ".wmv" or ".m4v"
        or ".3gp" or ".mpg" or ".mpeg";

    /// <summary>Recognizes a common audio extension, including its leading dot, ignoring case.</summary>
    public static bool IsAudioExtension(string? extension) => extension?.ToLowerInvariant() is
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".oga" or ".m4a" or ".wma"
        or ".opus" or ".aiff" or ".aif";

    /// <summary>Recognizes a common audio or video extension without probing the file.</summary>
    public static bool IsPlayableMediaExtension(string? extension) =>
        IsVideoExtension(extension) || IsAudioExtension(extension);
}
