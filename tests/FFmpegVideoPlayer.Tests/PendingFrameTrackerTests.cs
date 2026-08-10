using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class PendingFrameTrackerTests
{
    [Fact]
    public void Outstanding_leases_remain_counted_until_their_callbacks_complete()
    {
        var tracker = new PendingFrameTracker(maximumCount: 2);

        Assert.True(tracker.TryAcquire(out var first));
        Assert.True(tracker.TryAcquire(out var second));
        Assert.False(tracker.TryAcquire(out _));
        Assert.Equal(2, tracker.Count);

        first!.Dispose();

        Assert.Equal(1, tracker.Count);
        Assert.True(tracker.TryAcquire(out var replacement));
        Assert.Equal(2, tracker.Count);

        second!.Dispose();
        replacement!.Dispose();
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void Completing_a_stale_lease_twice_never_makes_the_count_negative()
    {
        var tracker = new PendingFrameTracker(maximumCount: 1);
        Assert.True(tracker.TryAcquire(out var lease));

        lease!.Dispose();
        lease.Dispose();

        Assert.Equal(0, tracker.Count);
        Assert.True(tracker.TryAcquire(out var next));
        next!.Dispose();
        Assert.Equal(0, tracker.Count);
    }
}
