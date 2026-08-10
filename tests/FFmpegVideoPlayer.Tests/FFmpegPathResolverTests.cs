using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class FFmpegPathResolverTests
{
    private const string MissingFeatureContinuation =
        " is not implemented. Update your FFmpeg version to the newest one from Git. " +
        "If the problem still occurs, it means that your file has a feature which has not " +
        "been implemented.\n";
    private const string SampleUploadContinuation =
        "If you want to help, upload a sample of this file to https://streams.videolan.org/upload/ " +
        "and contact the ffmpeg-devel mailing list. (ffmpeg-devel@ffmpeg.org)\n";

    [Fact]
    public void LateSeiSequence_SuppressesAllThreeExactFragments()
    {
        var state = LateSeiLogFilterState.None;

        Assert.True(Filter("Late SEI", ref state));
        Assert.Equal(LateSeiLogFilterState.ExpectMissingFeatureContinuation, state);

        Assert.True(Filter(MissingFeatureContinuation, ref state));
        Assert.Equal(LateSeiLogFilterState.ExpectSampleUploadContinuation, state);

        Assert.True(Filter(SampleUploadContinuation, ref state));
        Assert.Equal(LateSeiLogFilterState.None, state);

        Assert.False(Filter("A normal FFmpeg diagnostic\n", ref state));
    }

    [Theory]
    [InlineData("Late sei")]
    [InlineData("Late SEI ")]
    [InlineData("Unexpected Late SEI")]
    [InlineData("Late SEI is not implemented")]
    public void HeaderNearMatches_AreForwarded(string fragment)
    {
        var state = LateSeiLogFilterState.None;

        Assert.False(Filter(fragment, ref state));
        Assert.Equal(LateSeiLogFilterState.None, state);
    }

    [Fact]
    public void GenericContinuations_WithoutExactHeader_AreForwarded()
    {
        var state = LateSeiLogFilterState.None;

        Assert.False(Filter(MissingFeatureContinuation, ref state));
        Assert.False(Filter(SampleUploadContinuation, ref state));
        Assert.Equal(LateSeiLogFilterState.None, state);
    }

    [Fact]
    public void UnexpectedContinuation_ResetsStateAndIsForwarded()
    {
        var state = LateSeiLogFilterState.None;

        Assert.True(Filter("Late SEI", ref state));
        Assert.False(Filter("A different decoder diagnostic\n", ref state));
        Assert.Equal(LateSeiLogFilterState.None, state);

        Assert.False(Filter(MissingFeatureContinuation, ref state));
        Assert.False(Filter(SampleUploadContinuation, ref state));
    }

    [Fact]
    public void UnexpectedUploadFragment_ResetsStateAndIsForwarded()
    {
        var state = LateSeiLogFilterState.None;

        Assert.True(Filter("Late SEI", ref state));
        Assert.True(Filter(MissingFeatureContinuation, ref state));
        Assert.False(Filter("Different upload guidance\n", ref state));
        Assert.Equal(LateSeiLogFilterState.None, state);

        Assert.False(Filter(SampleUploadContinuation, ref state));
    }

    private static bool Filter(string fragment, ref LateSeiLogFilterState state) =>
        FFmpegPathResolver.ShouldSuppressLateSeiLogFragment(fragment, ref state);
}
