using System.Diagnostics;
using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

public sealed class PlayerLifecycleTests
{
    [Fact]
    public async Task Audio_reset_stops_before_restoring_volume_and_tolerates_backend_failure()
    {
        await using var player = new FFmpegMediaPlayer { Volume = 37 };
        var audio = new RecordingAudioPlayer();
        player.AudioPlayer = audio;
        Assert.Equal(new[] { "Volume", "Stop" }, audio.Calls);

        audio.Calls.Clear();
        player.ResetAudioPlayerState();
        Assert.Equal(new[] { "Stop", "Volume" }, audio.Calls);
        Assert.Equal(0.37f, audio.Volume);

        audio.Calls.Clear();
        audio.FailStop = true;
        player.ResetAudioPlayerState();
        Assert.Equal(new[] { "Stop" }, audio.Calls);
    }

    [Fact]
    public async Task Pre_cancelled_open_does_not_change_the_existing_state_or_invoke_the_factory()
    {
        var factoryCalled = false;
        var source = MediaSource.FromSessionFactory(
            "cancelled.mp4",
            MediaSourceKind.Stream,
            _ =>
            {
                factoryCalled = true;
                return ValueTask.FromResult(new MediaSourceSession("unused"));
            });
        await using var player = new FFmpegMediaPlayer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            player.OpenAsync(source, cancellationToken: cancellation.Token));

        Assert.False(factoryCalled);
        Assert.Equal(PlaybackState.Closed, player.State);
    }

    [Fact]
    public async Task Cancellation_during_source_resolution_is_not_reported_as_a_media_failure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = MediaSource.FromSessionFactory(
            "cancelled.mp4",
            MediaSourceKind.Stream,
            async cancellationToken =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new UnreachableException();
            });
        await using var player = new FFmpegMediaPlayer();
        var failures = 0;
        player.MediaFailed += (_, _) => Interlocked.Increment(ref failures);
        using var cancellation = new CancellationTokenSource();

        var opening = player.OpenAsync(source, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await Task.Delay(50);
        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.Equal(PlaybackState.Closed, player.State);
    }

    [Fact]
    public async Task Dispose_is_bounded_when_a_custom_source_ignores_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MediaSourceSession>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = MediaSource.FromSessionFactory(
            "noncooperative.mp4",
            MediaSourceKind.Stream,
            _ =>
            {
                entered.TrySetResult();
                return new ValueTask<MediaSourceSession>(release.Task);
            });
        var player = new FFmpegMediaPlayer();
        var opening = player.OpenAsync(source);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        player.Dispose();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Dispose blocked for {stopwatch.Elapsed}.");

        release.TrySetResult(new MediaSourceSession("unused"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => opening);
        await player.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(PlaybackState.Disposed, player.State);
    }

    private sealed class RecordingAudioPlayer : IAudioPlayer
    {
        public List<string> Calls { get; } = [];
        public float Volume { get; private set; }
        public bool FailStop { get; set; }
        public void SetVolume(float volume) { Volume = volume; Calls.Add("Volume"); }
        public void Stop()
        {
            Calls.Add("Stop");
            if (FailStop)
                throw new InvalidOperationException("Synthetic backend failure.");
        }
        public void Resume() => Calls.Add("Resume");
        public void Pause() { }
        public double GetPlaybackTime() => 0;
        public unsafe void QueueSamplesS16(short* samples, int sampleCount) { }
        public void QueueSamples(float[] samples) { }
        public void Dispose() { }
    }
}
