using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class FrameEventArgsTests
{
    [Fact]
    public void Concurrent_stale_cleanup_returns_a_pooled_buffer_only_once()
    {
        var releaseCount = 0;
        var eventArgs = new FrameEventArgs(
            new byte[4],
            width: 1,
            height: 1,
            stride: 4,
            dataLength: 4,
            pooled: true,
            releaseAction: _ => Interlocked.Increment(ref releaseCount),
            mediaGeneration: 2,
            timelineGeneration: 3);

        Parallel.Invoke(eventArgs.Dispose, eventArgs.Dispose, eventArgs.Dispose);

        Assert.Equal(1, Volatile.Read(ref releaseCount));
    }
}
