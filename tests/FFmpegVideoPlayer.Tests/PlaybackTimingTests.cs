using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class PlaybackTimingTests
{
    [Fact]
    public void Audio_clock_combines_stream_start_with_backend_playback_time()
    {
        var valid = PlaybackTiming.TryComposeAudioMediaClock(12.5, 1.25, out var clock);

        Assert.True(valid);
        Assert.Equal(13.75, clock, precision: 6);
    }

    [Fact]
    public void Zero_stream_start_is_valid_after_backend_clock_advances()
    {
        var valid = PlaybackTiming.TryComposeAudioMediaClock(0, 0.025, out var clock);

        Assert.True(valid);
        Assert.Equal(0.025, clock, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Audio_clock_falls_back_until_backend_time_has_advanced(double playbackTime)
    {
        var valid = PlaybackTiming.TryComposeAudioMediaClock(0, playbackTime, out _);

        Assert.False(valid);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Audio_clock_rejects_an_invalid_stream_start(double audioStartPts)
    {
        var valid = PlaybackTiming.TryComposeAudioMediaClock(audioStartPts, 0.025, out _);

        Assert.False(valid);
    }

    [Fact]
    public void Active_wall_clock_freezes_during_pause_and_resumes_without_a_jump()
    {
        var beforePause = PlaybackTiming.GetActiveElapsedSeconds(
            stopwatchElapsedSeconds: 10,
            totalPauseSeconds: 2,
            currentPauseStartSeconds: 0);
        var duringPause = PlaybackTiming.GetActiveElapsedSeconds(
            stopwatchElapsedSeconds: 15,
            totalPauseSeconds: 2,
            currentPauseStartSeconds: 10);
        var afterResume = PlaybackTiming.GetActiveElapsedSeconds(
            stopwatchElapsedSeconds: 15,
            totalPauseSeconds: 7,
            currentPauseStartSeconds: 0);

        Assert.Equal(8, beforePause);
        Assert.Equal(beforePause, duringPause);
        Assert.Equal(beforePause, afterResume);
    }

    [Fact]
    public void Slightly_early_video_frame_waits_for_its_clock_delta()
    {
        var decision = PlaybackTiming.Decide(videoPts: 10.020, masterClock: 10);

        Assert.Equal(VideoTimingAction.Wait, decision.Action);
        Assert.Equal(20, decision.DelayMilliseconds);
    }

    [Fact]
    public void Large_positive_delta_is_bounded_to_a_short_wait()
    {
        var decision = PlaybackTiming.Decide(videoPts: 12, masterClock: 10);

        Assert.Equal(VideoTimingAction.Wait, decision.Action);
        Assert.Equal(PlaybackTiming.MaximumWaitMilliseconds, decision.DelayMilliseconds);
    }

    [Fact]
    public void Far_early_frame_is_dropped_when_the_audio_master_does_not_advance_within_budget()
    {
        var waited = 0;
        VideoTimingDecision decision;

        do
        {
            decision = PlaybackTiming.DecideAfterAudioWaitBudget(
                videoPts: 10.5,
                masterClock: 10,
                cumulativeAudioWaitMilliseconds: waited);
            waited += decision.DelayMilliseconds;
        }
        while (decision.Action == VideoTimingAction.Wait);

        Assert.Equal(VideoTimingAction.Drop, decision.Action);
        Assert.Equal(PlaybackTiming.MaximumCumulativeWaitMilliseconds, waited);
    }

    [Fact]
    public void Recheck_presents_an_early_frame_once_the_master_clock_catches_up()
    {
        var firstDecision = PlaybackTiming.DecideAfterAudioWaitBudget(
            videoPts: 10.020,
            masterClock: 10,
            cumulativeAudioWaitMilliseconds: 0);
        var secondDecision = PlaybackTiming.DecideAfterAudioWaitBudget(
            videoPts: 10.020,
            masterClock: 10.020,
            cumulativeAudioWaitMilliseconds: firstDecision.DelayMilliseconds);

        Assert.Equal(VideoTimingAction.Wait, firstDecision.Action);
        Assert.Equal(VideoTimingAction.Present, secondDecision.Action);
    }

    [Fact]
    public void Final_wait_slice_is_clamped_to_the_remaining_total_budget()
    {
        var decision = PlaybackTiming.DecideAfterAudioWaitBudget(
            videoPts: 12,
            masterClock: 10,
            cumulativeAudioWaitMilliseconds: PlaybackTiming.MaximumCumulativeWaitMilliseconds - 7);

        Assert.Equal(VideoTimingAction.Wait, decision.Action);
        Assert.Equal(7, decision.DelayMilliseconds);
    }

    [Fact]
    public void Wall_clock_timing_keeps_waiting_after_the_audio_only_budget_would_expire()
    {
        var wallClockDecision = PlaybackTiming.Decide(
            videoPts: 10.5,
            masterClock: 10);
        var audioClockDecision = PlaybackTiming.DecideAfterAudioWaitBudget(
            videoPts: 10.5,
            masterClock: 10,
            cumulativeAudioWaitMilliseconds: PlaybackTiming.MaximumCumulativeWaitMilliseconds);

        Assert.Equal(VideoTimingAction.Wait, wallClockDecision.Action);
        Assert.Equal(VideoTimingAction.Drop, audioClockDecision.Action);
    }

    [Fact]
    public void Transitioning_from_wall_to_audio_starts_a_fresh_audio_wait_budget()
    {
        var wallClockDecision = PlaybackTiming.Decide(
            videoPts: 10.5,
            masterClock: 10);
        var firstAudioDecision = PlaybackTiming.DecideAfterAudioWaitBudget(
            videoPts: 10.5,
            masterClock: 10,
            cumulativeAudioWaitMilliseconds: 0);

        Assert.Equal(VideoTimingAction.Wait, wallClockDecision.Action);
        Assert.Equal(VideoTimingAction.Wait, firstAudioDecision.Action);
        Assert.Equal(PlaybackTiming.MaximumWaitMilliseconds, firstAudioDecision.DelayMilliseconds);
    }

    [Fact]
    public void Clearly_late_video_frame_is_dropped()
    {
        var decision = PlaybackTiming.Decide(videoPts: 9.8, masterClock: 10);

        Assert.Equal(VideoTimingAction.Drop, decision.Action);
        Assert.Equal(0, decision.DelayMilliseconds);
    }

    [Fact]
    public void Modestly_late_video_frame_is_still_presented()
    {
        var decision = PlaybackTiming.Decide(videoPts: 9.9, masterClock: 10);

        Assert.Equal(VideoTimingAction.Present, decision.Action);
    }

    [Fact]
    public void Clock_jitter_inside_tolerance_is_presented_without_waiting()
    {
        var decision = PlaybackTiming.Decide(
            videoPts: 10 + PlaybackTiming.PresentationToleranceSeconds,
            masterClock: 10);

        Assert.Equal(VideoTimingAction.Present, decision.Action);
        Assert.Equal(0, decision.DelayMilliseconds);
    }
}
