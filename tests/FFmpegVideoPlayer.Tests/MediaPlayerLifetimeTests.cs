using System.Reflection;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Headless;

namespace FFmpegVideoPlayer.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class MediaPlayerLifetimeTests
{
    [Fact]
    public async Task Closing_a_detached_control_more_than_once_returns_one_result_per_instance()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(async () =>
        {
            var first = new VideoPlayerControl();
            var second = new VideoPlayerControl();

            var results = await MediaPlayerLifetime.CloseAllAsync([first, second, first, second]);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, result => ReferenceEquals(first, result.Player));
            Assert.Contains(results, result => ReferenceEquals(second, result.Player));
            Assert.All(results, result =>
            {
                Assert.True(result.Succeeded);
                Assert.Null(result.Exception);
            });
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Closing_an_empty_collection_has_no_results()
    {
        var results = await MediaPlayerLifetime.CloseAllAsync([]);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Closing_waits_for_pending_native_cleanup_after_the_control_detaches()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(async () =>
        {
            var player = new VideoPlayerControl();
            var cleanup = new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetDetachedDisposal(player, cleanup.Task);
            try
            {
                var closing = MediaPlayerLifetime.CloseAllAsync([player, player]);

                Assert.False(closing.IsCompleted,
                    "Closing completed while the detached native player was still being disposed.");
                cleanup.SetResult(null);

                var result = Assert.Single(await closing.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Same(player, result.Player);
                Assert.True(result.Succeeded);
                Assert.Null(result.Exception);
            }
            finally
            {
                cleanup.TrySetResult(null);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_failed_detached_cleanup_is_reported_without_losing_other_close_results()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(async () =>
        {
            var failedPlayer = new VideoPlayerControl();
            var healthyPlayer = new VideoPlayerControl();
            var failure = new IOException("Synthetic detached media cleanup failure.");
            var cleanup = new TaskCompletionSource<Exception?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            SetDetachedDisposal(failedPlayer, cleanup.Task);
            try
            {
                var closing = MediaPlayerLifetime.CloseAllAsync([failedPlayer, healthyPlayer]);

                Assert.False(closing.IsCompleted,
                    "CloseAllAsync did not wait for the detached cleanup result.");
                cleanup.SetResult(failure);

                var results = await closing.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(2, results.Count);
                var failed = Assert.Single(results, result => ReferenceEquals(result.Player, failedPlayer));
                Assert.False(failed.Succeeded);
                var reported = Assert.IsType<InvalidOperationException>(failed.Exception);
                Assert.Same(failure, reported.InnerException);
                var healthy = Assert.Single(results, result => ReferenceEquals(result.Player, healthyPlayer));
                Assert.True(healthy.Succeeded);
                Assert.Null(healthy.Exception);
            }
            finally
            {
                cleanup.TrySetResult(null);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_null_collection_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => MediaPlayerLifetime.CloseAllAsync(null!));
    }

    [Fact]
    public async Task A_null_collection_element_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => MediaPlayerLifetime.CloseAllAsync([null!]));
    }

    // Model the pending asynchronous native disposal without creating a native
    // player, opening media, or adding a production-only testing seam.
    private static void SetDetachedDisposal(VideoPlayerControl player, Task<Exception?> cleanup)
    {
        var field = typeof(VideoPlayerControl).GetField(
            "_detachedDisposal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(VideoPlayerControl).FullName, "_detachedDisposal");
        field.SetValue(player, cleanup);
    }
}
