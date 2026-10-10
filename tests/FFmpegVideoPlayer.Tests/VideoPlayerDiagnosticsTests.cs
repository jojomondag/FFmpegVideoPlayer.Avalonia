using System.Reflection;
using Avalonia.FFmpegVideoPlayer;
using Avalonia.Headless;
using FFmpegVideoPlayer.Core;

namespace FFmpegVideoPlayer.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class VideoPlayerDiagnosticsTests
{
    [Fact]
    public Task Failing_subscriber_does_not_prevent_later_diagnostics_or_audio_creation() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();
        var received = new List<VideoPlayerDiagnosticEventArgs>();
        player.Diagnostic += (_, _) => throw new InvalidOperationException("Consumer logging failed.");
        player.Diagnostic += (sender, args) =>
        {
            Assert.Same(player, sender);
            received.Add(args);
        };

        Report(player, VideoPlayerDiagnosticKind.Playing);
        Report(player, VideoPlayerDiagnosticKind.Paused);
        var audio = new FakeAudioPlayer();
        Assert.Same(audio, CreateAudio(player, (_, _) => audio));

        Assert.Equal(
            new[] { VideoPlayerDiagnosticKind.Playing, VideoPlayerDiagnosticKind.Paused, VideoPlayerDiagnosticKind.AudioOutputReady },
            received.Select(args => args.Kind));
        Assert.All(received, args => Assert.Equal(received[0].PlayerId, args.PlayerId));
    });

    [Fact]
    public Task Different_controls_have_distinct_diagnostic_ids() => RunUiTest(() =>
    {
        var first = new VideoPlayerControl();
        var second = new VideoPlayerControl();
        VideoPlayerDiagnosticEventArgs? firstDiagnostic = null;
        VideoPlayerDiagnosticEventArgs? secondDiagnostic = null;
        first.Diagnostic += (_, args) => firstDiagnostic = args;
        second.Diagnostic += (_, args) => secondDiagnostic = args;

        Report(first, VideoPlayerDiagnosticKind.Opening);
        Report(second, VideoPlayerDiagnosticKind.Opening);

        Assert.NotNull(firstDiagnostic);
        Assert.NotNull(secondDiagnostic);
        Assert.NotEqual(firstDiagnostic.PlayerId, secondDiagnostic.PlayerId);
    });

    [Fact]
    public Task A_null_audio_backend_is_a_nonfatal_structured_diagnostic() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();
        var received = new List<VideoPlayerDiagnosticEventArgs>();
        var fatalFailures = 0;
        player.Diagnostic += (_, args) => received.Add(args);
        player.MediaFailed += (_, _) => fatalFailures++;

        var audio = CreateAudio(player, (_, _) => null);

        Assert.Null(audio);
        var diagnostic = Assert.Single(received);
        Assert.Equal(VideoPlayerDiagnosticKind.AudioOutputFailed, diagnostic.Kind);
        Assert.Equal(MediaErrorCode.AudioOutputUnavailable, diagnostic.Error?.Code);
        Assert.Equal(48_000, diagnostic.SampleRate);
        Assert.Equal(2, diagnostic.Channels);
        Assert.Null(diagnostic.Exception);
        Assert.Equal(0, fatalFailures);
    });

    [Fact]
    public Task A_throwing_audio_factory_preserves_the_exception_without_failing_video() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();
        var failure = new InvalidOperationException("Synthetic audio initialization failure.");
        var received = new List<VideoPlayerDiagnosticEventArgs>();
        var fatalFailures = 0;
        player.Diagnostic += (_, _) => throw new InvalidOperationException("Consumer logging failed.");
        player.Diagnostic += (_, args) => received.Add(args);
        player.MediaFailed += (_, _) => fatalFailures++;

        Assert.Null(CreateAudio(player, (_, _) => throw failure));

        var diagnostic = Assert.Single(received);
        Assert.Equal(VideoPlayerDiagnosticKind.AudioOutputFailed, diagnostic.Kind);
        Assert.Equal(MediaErrorCode.AudioOutputUnavailable, diagnostic.Error?.Code);
        Assert.Same(failure, diagnostic.Exception);
        Assert.Same(failure, diagnostic.Error?.Exception);
        Assert.Equal(48_000, diagnostic.SampleRate);
        Assert.Equal(2, diagnostic.Channels);
        Assert.Equal(0, fatalFailures);
    });

    [Fact]
    public Task A_successful_audio_factory_receives_the_format_and_reports_readiness() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();
        var expected = new FakeAudioPlayer();
        var received = new List<VideoPlayerDiagnosticEventArgs>();
        player.Diagnostic += (_, args) => received.Add(args);

        var actual = CreateAudio(player, (sampleRate, channels) =>
        {
            Assert.Equal(48_000, sampleRate);
            Assert.Equal(2, channels);
            return expected;
        });

        Assert.Same(expected, actual);
        var diagnostic = Assert.Single(received);
        Assert.Equal(VideoPlayerDiagnosticKind.AudioOutputReady, diagnostic.Kind);
        Assert.Equal(48_000, diagnostic.SampleRate);
        Assert.Equal(2, diagnostic.Channels);
        Assert.Null(diagnostic.Error);
        Assert.Null(diagnostic.Exception);
    });

    [Theory]
    [InlineData("OnPlaying", VideoPlayerDiagnosticKind.Playing, false)]
    [InlineData("OnPlaying", VideoPlayerDiagnosticKind.Playing, true)]
    [InlineData("OnPaused", VideoPlayerDiagnosticKind.Paused, false)]
    [InlineData("OnPaused", VideoPlayerDiagnosticKind.Paused, true)]
    [InlineData("OnStopped", VideoPlayerDiagnosticKind.Stopped, false)]
    [InlineData("OnStopped", VideoPlayerDiagnosticKind.Stopped, true)]
    public Task Playback_notifications_observe_changes_made_by_diagnostic_subscribers(
        string handler, VideoPlayerDiagnosticKind expectedKind, bool supersede) => RunUiTest(() =>
    {
        var control = new VideoPlayerControl();
        using var player = new FFmpegMediaPlayer();
        typeof(VideoPlayerControl).GetField("_mediaPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(control, player);
        var notifications = 0;
        VideoPlayerDiagnosticKind? receivedKind = null;
        control.Diagnostic += (_, args) =>
        {
            receivedKind = args.Kind;
            control.PlaybackStarted += CountNotification;
            control.PlaybackPaused += CountNotification;
            control.PlaybackStopped += CountNotification;
            if (supersede) control.Stop();
        };

        PrivateMethod(handler).Invoke(control, [player, EventArgs.Empty]);

        Assert.Equal(expectedKind, receivedKind);
        Assert.Equal(supersede ? 0 : 1, notifications);
        void CountNotification(object? sender, EventArgs args) => notifications++;
    });

    [Fact]
    public Task A_failure_from_an_old_native_player_does_not_end_the_current_session() => RunUiTest(() =>
    {
        var player = new VideoPlayerControl();
        var received = new List<VideoPlayerDiagnosticEventArgs>();
        var fatalFailures = 0;
        player.Diagnostic += (_, args) => received.Add(args);
        player.MediaFailed += (_, _) => fatalFailures++;

        PrivateMethod("OnCoreMediaFailed").Invoke(player,
            [new object(), new MediaFailedEventArgs(new MediaError(
                MediaErrorCode.PlaybackFailed, "Synthetic outgoing player failure."))]);

        Assert.Empty(received);
        Assert.Equal(0, fatalFailures);
    });

    // These helpers exercise the production event boundary without opening media,
    // resolving network sources, or allocating an operating-system audio device.
    private static void Report(VideoPlayerControl player, VideoPlayerDiagnosticKind kind) =>
        PrivateMethod("ReportDiagnostic").Invoke(player, [kind, null, null, null, null, null]);

    private static IAudioPlayer? CreateAudio(
        VideoPlayerControl player, Func<int, int, IAudioPlayer?> factory) =>
        (IAudioPlayer?)PrivateMethod("CreateAudioPlayer").Invoke(player, [48_000, 2, factory]);

    private static MethodInfo PrivateMethod(string name) =>
        typeof(VideoPlayerControl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(VideoPlayerControl).FullName, name);

    private static async Task RunUiTest(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(IconTestApp), AvaloniaTestIsolationLevel.PerTest);
        await session.Dispatch(action, CancellationToken.None);
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public void SetVolume(float volume) { }
        public void Resume() { }
        public void Pause() { }
        public void Stop() { }
        public double GetPlaybackTime() => 0;
        public unsafe void QueueSamplesS16(short* samples, int sampleCount) { }
        public void QueueSamples(float[] samples) { }
        public void Dispose() { }
    }
}
