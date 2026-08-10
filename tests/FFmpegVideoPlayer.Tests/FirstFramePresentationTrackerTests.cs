using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class FirstFramePresentationTrackerTests
{
    [Fact]
    public void TryPresent_succeeds_only_once_for_active_generations()
    {
        var tracker = new FirstFramePresentationTracker();
        tracker.Activate(openGeneration: 4, mediaGeneration: 7);

        Assert.True(tracker.TryPresent(openGeneration: 4, mediaGeneration: 7));
        Assert.False(tracker.TryPresent(openGeneration: 4, mediaGeneration: 7));
    }

    [Fact]
    public void TryPresent_rejects_stale_open_and_media_generations()
    {
        var tracker = new FirstFramePresentationTracker();
        tracker.Activate(openGeneration: 4, mediaGeneration: 7);

        Assert.False(tracker.TryPresent(openGeneration: 3, mediaGeneration: 7));
        Assert.False(tracker.TryPresent(openGeneration: 4, mediaGeneration: 6));
        Assert.True(tracker.TryPresent(openGeneration: 4, mediaGeneration: 7));
    }

    [Fact]
    public void Reset_invalidates_the_active_generation_until_reactivated()
    {
        var tracker = new FirstFramePresentationTracker();
        tracker.Activate(openGeneration: 4, mediaGeneration: 7);

        tracker.Reset();

        Assert.False(tracker.TryPresent(openGeneration: 4, mediaGeneration: 7));

        tracker.Activate(openGeneration: 5, mediaGeneration: 8);

        Assert.False(tracker.TryPresent(openGeneration: 4, mediaGeneration: 7));
        Assert.True(tracker.TryPresent(openGeneration: 5, mediaGeneration: 8));
    }

    [Fact]
    public void Closing_the_player_invalidates_frames_from_its_previous_media_lifetime()
    {
        using var player = new FFmpegMediaPlayer();
        var previousGeneration = player.MediaGeneration;
        var previousTimelineGeneration = player.TimelineGeneration;

        player.Close();

        Assert.NotEqual(previousGeneration, player.MediaGeneration);
        Assert.NotEqual(previousTimelineGeneration, player.TimelineGeneration);
    }
}
