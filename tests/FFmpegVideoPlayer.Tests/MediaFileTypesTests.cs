using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class MediaFileTypesTests
{
    public static TheoryData<string> VideoExtensions =>
    [
        ".mov", ".avi", ".mp4", ".mkv", ".webm", ".flv",
        ".wmv", ".m4v", ".3gp", ".mpg", ".mpeg"
    ];

    public static TheoryData<string> AudioExtensions =>
    [
        ".mp3", ".wav", ".flac", ".aac", ".ogg", ".oga",
        ".m4a", ".wma", ".opus", ".aiff", ".aif"
    ];

    [Theory]
    [MemberData(nameof(VideoExtensions))]
    public void Video_extensions_are_case_insensitive_and_do_not_become_audio(string extension)
    {
        foreach (var value in new[] { extension, extension.ToUpperInvariant() })
        {
            Assert.True(MediaFileTypes.IsVideoExtension(value));
            Assert.True(MediaFileTypes.IsPlayableMediaExtension(value));
            Assert.False(MediaFileTypes.IsAudioExtension(value));
        }
    }

    [Theory]
    [MemberData(nameof(AudioExtensions))]
    public void Audio_extensions_are_case_insensitive_and_do_not_become_video(string extension)
    {
        foreach (var value in new[] { extension, extension.ToUpperInvariant() })
        {
            Assert.True(MediaFileTypes.IsAudioExtension(value));
            Assert.True(MediaFileTypes.IsPlayableMediaExtension(value));
            Assert.False(MediaFileTypes.IsVideoExtension(value));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("mp4")]
    [InlineData("video.mp4")]
    [InlineData(".mp4 ")]
    [InlineData(".pdf")]
    [InlineData(".png")]
    [InlineData(".unknown")]
    public void Only_supported_dotted_extensions_are_classified(string? value)
    {
        Assert.False(MediaFileTypes.IsVideoExtension(value));
        Assert.False(MediaFileTypes.IsAudioExtension(value));
        Assert.False(MediaFileTypes.IsPlayableMediaExtension(value));
    }
}
