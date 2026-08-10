namespace FFmpegVideoPlayer.Core;

internal enum VideoTimingAction
{
    Present,
    Wait,
    Drop,
}

internal readonly record struct VideoTimingDecision(
    VideoTimingAction Action,
    int DelayMilliseconds = 0);

/// <summary>
/// Pure A/V timing policy shared by the native playback loop and unit tests.
/// Audio is the preferred master clock once its backend clock has advanced.
/// </summary>
internal static class PlaybackTiming
{
    private const double ComparisonEpsilonSeconds = 1e-9;

    // Differences below two milliseconds are smaller than the scheduler precision
    // we can reliably act on and should not cause extra thread wakeups.
    internal const double PresentationToleranceSeconds = 0.002;

    // A video frame must be at least 150 ms behind the master clock before it is
    // discarded. This is deliberately conservative: ordinary decode jitter remains
    // visible, while frames that can no longer help A/V sync are skipped.
    internal const double LateFrameDropThresholdSeconds = 0.150;

    // Demux, audio decode and video presentation currently share one playback thread.
    // Each wait is short so the clock can be sampled again instead of blindly presenting
    // a frame after one sleep.
    internal const int MaximumWaitMilliseconds = 50;

    // Demux, audio decode and video presentation currently share one playback thread.
    // A stalled audio clock must not keep that thread away from the audio packets needed
    // to advance it. If a frame remains early after this budget, drop it and continue
    // demuxing rather than presenting it ahead of the master clock.
    internal const int MaximumCumulativeWaitMilliseconds = 150;

    internal static bool TryComposeAudioMediaClock(
        double audioStartPts,
        double audioPlaybackTime,
        out double mediaClock)
    {
        mediaClock = 0;
        if (!double.IsFinite(audioStartPts)
            || !double.IsFinite(audioPlaybackTime)
            || audioPlaybackTime <= 0)
        {
            return false;
        }

        mediaClock = audioStartPts + audioPlaybackTime;
        return double.IsFinite(mediaClock);
    }

    internal static double GetActiveElapsedSeconds(
        double stopwatchElapsedSeconds,
        double totalPauseSeconds,
        double currentPauseStartSeconds)
    {
        if (!double.IsFinite(stopwatchElapsedSeconds)
            || !double.IsFinite(totalPauseSeconds)
            || !double.IsFinite(currentPauseStartSeconds))
        {
            return 0;
        }

        var currentPauseSeconds = currentPauseStartSeconds > 0
            ? Math.Max(0, stopwatchElapsedSeconds - currentPauseStartSeconds)
            : 0;
        return Math.Max(
            0,
            stopwatchElapsedSeconds - Math.Max(0, totalPauseSeconds) - currentPauseSeconds);
    }

    internal static VideoTimingDecision Decide(double videoPts, double masterClock)
    {
        if (!double.IsFinite(videoPts) || !double.IsFinite(masterClock))
            return new VideoTimingDecision(VideoTimingAction.Present);

        var delta = videoPts - masterClock;
        if (delta <= -LateFrameDropThresholdSeconds)
            return new VideoTimingDecision(VideoTimingAction.Drop);

        // Decimal media timestamps commonly land a few ULPs above their nominal
        // boundary after rational-to-double conversion. Keep the documented 2 ms
        // tolerance inclusive despite that representation noise.
        if (delta <= PresentationToleranceSeconds + ComparisonEpsilonSeconds)
            return new VideoTimingDecision(VideoTimingAction.Present);

        var requestedDelay = (int)Math.Ceiling(delta * 1000);
        return new VideoTimingDecision(
            VideoTimingAction.Wait,
            Math.Clamp(requestedDelay, 1, MaximumWaitMilliseconds));
    }

    internal static VideoTimingDecision DecideAfterAudioWaitBudget(
        double videoPts,
        double masterClock,
        int cumulativeAudioWaitMilliseconds)
    {
        if (cumulativeAudioWaitMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(cumulativeAudioWaitMilliseconds));

        var decision = Decide(videoPts, masterClock);
        if (decision.Action != VideoTimingAction.Wait)
            return decision;

        var remainingWait = MaximumCumulativeWaitMilliseconds - cumulativeAudioWaitMilliseconds;
        if (remainingWait <= 0)
            return new VideoTimingDecision(VideoTimingAction.Drop);

        return decision with
        {
            DelayMilliseconds = Math.Min(decision.DelayMilliseconds, remainingWait),
        };
    }
}
