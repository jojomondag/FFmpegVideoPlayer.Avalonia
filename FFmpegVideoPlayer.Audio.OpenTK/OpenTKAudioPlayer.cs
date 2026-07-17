using System.Collections.Concurrent;
using System.Diagnostics;
using FFmpegVideoPlayer.Core;
using OpenTK.Audio.OpenAL;

namespace FFmpegVideoPlayer.Audio.OpenTK;

/// <summary>
/// OpenAL audio backend with a single owner thread for every AL/ALC operation.
/// PCM producers are backpressured by a bounded managed queue, while control
/// operations are delivered to the owner thread as commands.
/// </summary>
public sealed class OpenTKAudioPlayer : IAudioPlayer
{
    private const int BufferCount = 16;
    private const int SamplesPerPacket = 16_384;
    private const int InitialBufferTarget = 4;
    private const int InitialStartDelayMilliseconds = 75;
    private const int MaximumPendingSeconds = 2;
    private const int StartupTimeoutMilliseconds = 10_000;
    private const int DisposeJoinTimeoutMilliseconds = 5_000;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _inputChannels;
    private readonly int _maximumPendingSamples;
    private readonly PlayerLogger _logger;
    private readonly ConcurrentQueue<AudioCommand> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<Exception?> _startup =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _pcmLock = new();
    private readonly Queue<PcmPacket> _pendingPcm = new();
    private readonly Thread _audioThread;

    // These native handles and collections are accessed by the audio thread only.
    private ALDevice _device = ALDevice.Null;
    private ALContext _context = ALContext.Null;
    private int _source;
    private readonly Queue<int> _availableBuffers = new();
    private readonly List<int> _allBuffers = new(BufferCount);
    private readonly Dictionary<int, int> _bufferSampleCounts = new(BufferCount);
    private long _playedSamples;
    private bool _hasStartedOnce;
    private bool _workerInputCompleted;
    private long _firstQueuedTimestamp;

    // Cross-thread state. Native state is sampled and published by the audio thread.
    private int _generation;
    private int _pendingSampleCount;
    private int _inputCompleted;
    private int _desiredPaused;
    private int _disposeRequested;
    private int _workerExited;
    private int _isDrained = 1;
    private int _volumeBits = BitConverter.SingleToInt32Bits(1.0f);
    private long _cachedPlaybackTimeBits;

    public OpenTKAudioPlayer(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        _sampleRate = sampleRate;
        _inputChannels = channels;
        _channels = Math.Min(channels, 2);
        _maximumPendingSamples = checked((int)Math.Min(
            int.MaxValue,
            Math.Max(SamplesPerPacket, (long)sampleRate * _channels * MaximumPendingSeconds)));
        _logger = new PlayerLogger();

        _logger.Log("OpenTKAudioPlayer", "Initialize", new
        {
            SampleRate = sampleRate,
            InputChannels = channels,
            OutputChannels = _channels,
            MaximumPendingSamples = _maximumPendingSamples
        });

        _audioThread = new Thread(AudioLoop)
        {
            Name = "OpenTKAudioPlayer",
            IsBackground = true
        };
        _audioThread.Start();

        if (!_startup.Task.Wait(StartupTimeoutMilliseconds))
        {
            RequestShutdown();
            _audioThread.Join(1_000);
            throw new TimeoutException("Timed out while initializing the OpenAL audio thread.");
        }

        var startupError = _startup.Task.GetAwaiter().GetResult();
        if (startupError != null)
        {
            RequestShutdown();
            _audioThread.Join(1_000);

            var message = startupError is DllNotFoundException
                ? "OpenAL native library was not found. Ensure the OpenAL Soft runtime is deployed with the application."
                : $"Failed to initialize OpenAL: {startupError.Message}";
            throw new InvalidOperationException(message, startupError);
        }
    }

    /// <inheritdoc />
    public void SetVolume(float volume)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        var clamped = Math.Clamp(volume, 0f, 1f);
        Interlocked.Exchange(ref _volumeBits, BitConverter.SingleToInt32Bits(clamped));
        EnqueueCommand(new AudioCommand(AudioCommandKind.SetVolume, clamped));
    }

    /// <inheritdoc />
    public void Resume()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        Volatile.Write(ref _desiredPaused, 0);
        EnqueueCommand(new AudioCommand(AudioCommandKind.Resume));
    }

    /// <inheritdoc />
    public void Pause()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        Volatile.Write(ref _desiredPaused, 1);
        EnqueueCommand(new AudioCommand(AudioCommandKind.Pause));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        var generation = Interlocked.Increment(ref _generation);
        Volatile.Write(ref _inputCompleted, 0);
        Volatile.Write(ref _isDrained, 1);
        PublishPlaybackTime(0);
        ClearPendingPcm();
        EnqueueCommand(new AudioCommand(AudioCommandKind.Stop, Generation: generation));
    }

    /// <inheritdoc />
    public double GetPlaybackTime() =>
        BitConverter.Int64BitsToDouble(Interlocked.Read(ref _cachedPlaybackTimeBits));

    /// <inheritdoc />
    public bool IsDrained => Volatile.Read(ref _isDrained) != 0;

    /// <inheritdoc />
    public unsafe void QueueSamplesS16(short* samples, int sampleCount) =>
        QueueSamplesS16(samples, sampleCount, CancellationToken.None);

    /// <inheritdoc />
    public unsafe void QueueSamplesS16(short* samples, int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount < 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (sampleCount == 0)
            return;
        if (samples == null)
            throw new ArgumentNullException(nameof(samples));
        if (sampleCount % _channels != 0)
            throw new ArgumentException("The sample count must contain complete interleaved frames.", nameof(sampleCount));

        var generation = Volatile.Read(ref _generation);
        EnsureCanQueue(cancellationToken);

        var offset = 0;
        while (offset < sampleCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(SamplesPerPacket, sampleCount - offset);
            count -= count % _channels;
            if (count == 0)
                count = sampleCount - offset;

            var copy = new short[count];
            for (var index = 0; index < count; index++)
                copy[index] = samples[offset + index];

            if (!TryEnqueuePcm(new PcmPacket(generation, copy), cancellationToken))
                return;

            offset += count;
        }
    }

    /// <inheritdoc />
    public void QueueSamples(float[] samples) =>
        QueueSamples(samples, CancellationToken.None);

    /// <inheritdoc />
    public void QueueSamples(float[] samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0)
            return;
        if (samples.Length % _inputChannels != 0)
            throw new ArgumentException("The sample array must contain complete interleaved frames.", nameof(samples));

        var generation = Volatile.Read(ref _generation);
        EnsureCanQueue(cancellationToken);

        var output = _inputChannels > _channels
            ? DownmixToStereo(samples, _inputChannels)
            : samples;

        var offset = 0;
        while (offset < output.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(SamplesPerPacket, output.Length - offset);
            count -= count % _channels;
            if (count == 0)
                count = output.Length - offset;

            var pcm = new short[count];
            for (var index = 0; index < count; index++)
            {
                var value = output[offset + index];
                if (!float.IsFinite(value))
                    value = 0;
                pcm[index] = (short)Math.Round(Math.Clamp(value, -1f, 1f) * short.MaxValue);
            }

            if (!TryEnqueuePcm(new PcmPacket(generation, pcm), cancellationToken))
                return;

            offset += count;
        }
    }

    /// <inheritdoc />
    public void CompleteInput()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        var generation = Volatile.Read(ref _generation);
        Volatile.Write(ref _inputCompleted, 1);
        EnqueueCommand(new AudioCommand(AudioCommandKind.CompleteInput, Generation: generation));
    }

    private void EnsureCanQueue(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);

        if (Volatile.Read(ref _workerExited) != 0)
            throw new InvalidOperationException("The OpenAL audio thread is no longer running.");
        if (Volatile.Read(ref _inputCompleted) != 0)
            throw new InvalidOperationException("Input has already been completed. Call Stop before starting another input generation.");
    }

    private bool TryEnqueuePcm(PcmPacket packet, CancellationToken cancellationToken)
    {
        lock (_pcmLock)
        {
            while (Volatile.Read(ref _disposeRequested) == 0 &&
                   Volatile.Read(ref _workerExited) == 0 &&
                   packet.Generation == Volatile.Read(ref _generation) &&
                   Volatile.Read(ref _inputCompleted) == 0 &&
                   _pendingSampleCount + packet.Samples.Length > _maximumPendingSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Monitor.Wait(_pcmLock, 50);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposeRequested) != 0 ||
                Volatile.Read(ref _workerExited) != 0 ||
                packet.Generation != Volatile.Read(ref _generation) ||
                Volatile.Read(ref _inputCompleted) != 0)
            {
                return false;
            }

            _pendingPcm.Enqueue(packet);
            _pendingSampleCount += packet.Samples.Length;
            Volatile.Write(ref _isDrained, 0);
        }

        _wake.Set();
        return true;
    }

    private bool TryDequeuePcm(out PcmPacket packet)
    {
        lock (_pcmLock)
        {
            if (!_pendingPcm.TryDequeue(out packet))
                return false;

            _pendingSampleCount -= packet.Samples.Length;
            Monitor.PulseAll(_pcmLock);
            return true;
        }
    }

    private void ClearPendingPcm()
    {
        lock (_pcmLock)
        {
            _pendingPcm.Clear();
            _pendingSampleCount = 0;
            Monitor.PulseAll(_pcmLock);
        }
    }

    private bool HasPendingPcm()
    {
        lock (_pcmLock)
            return _pendingPcm.Count != 0;
    }

    private void EnqueueCommand(AudioCommand command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    private void AudioLoop()
    {
        var startupSignalled = false;
        try
        {
            InitializeOpenAl();
            _startup.TrySetResult(null);
            startupSignalled = true;

            while (!_shutdown.IsCancellationRequested)
            {
                if (!ProcessCommands())
                    break;

                RecycleProcessedBuffers();
                if (!QueuePendingPcm())
                    continue;

                UpdatePlaybackState();
                UpdatePlaybackClock();
                _wake.WaitOne(2);
            }
        }
        catch (Exception ex)
        {
            if (!startupSignalled)
            {
                _startup.TrySetResult(ex);
                startupSignalled = true;
            }
            else
            {
                Debug.WriteLine($"[OpenTKAudioPlayer] Audio thread stopped: {ex.Message}");
                _logger.Log("OpenTKAudioPlayer", "AudioThreadFailed", new { Exception = ex.Message });
            }
        }
        finally
        {
            if (!startupSignalled)
                _startup.TrySetResult(new InvalidOperationException("The OpenAL audio thread exited during initialization."));

            CleanupOpenAl();
            ClearPendingPcm();
            PublishPlaybackTime(0);
            Volatile.Write(ref _isDrained, 1);
            Volatile.Write(ref _workerExited, 1);
            lock (_pcmLock)
                Monitor.PulseAll(_pcmLock);
            _logger.Dispose();
        }
    }

    private void InitializeOpenAl()
    {
        _device = ALC.OpenDevice(null);
        if (_device == ALDevice.Null)
            throw new InvalidOperationException("OpenAL could not open the default audio device.");

        _context = ALC.CreateContext(_device, (int[]?)null);
        if (_context == ALContext.Null)
            throw new InvalidOperationException($"OpenAL could not create a context ({ALC.GetError(_device)}).");

        ALC.MakeContextCurrent(_context);
        var contextError = ALC.GetError(_device);
        if (contextError != AlcError.NoError)
            throw new InvalidOperationException($"OpenAL could not make its context current ({contextError}).");

        _source = AL.GenSource();
        ThrowOnAlError("create an audio source");

        for (var index = 0; index < BufferCount; index++)
        {
            var buffer = AL.GenBuffer();
            ThrowOnAlError("create an audio buffer");
            _allBuffers.Add(buffer);
            _availableBuffers.Enqueue(buffer);
        }

        var volume = BitConverter.Int32BitsToSingle(Volatile.Read(ref _volumeBits));
        AL.Source(_source, ALSourcef.Gain, volume);
        ThrowOnAlError("set source gain");
        _logger.Log("OpenTKAudioPlayer", "OpenALReady", new { BufferCount });
    }

    private bool ProcessCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case AudioCommandKind.SetVolume:
                    AL.Source(_source, ALSourcef.Gain, command.Value);
                    LogAlError("set source gain");
                    break;

                case AudioCommandKind.Resume:
                    Volatile.Write(ref _desiredPaused, 0);
                    break;

                case AudioCommandKind.Pause:
                    Volatile.Write(ref _desiredPaused, 1);
                    AL.SourcePause(_source);
                    LogAlError("pause playback");
                    break;

                case AudioCommandKind.Stop:
                    if (command.Generation == Volatile.Read(ref _generation))
                    {
                        StopOpenAlPipeline();
                        _workerInputCompleted = false;
                    }
                    break;

                case AudioCommandKind.CompleteInput:
                    if (command.Generation == Volatile.Read(ref _generation))
                        _workerInputCompleted = true;
                    break;

                case AudioCommandKind.Shutdown:
                    return false;
            }
        }

        return !_shutdown.IsCancellationRequested;
    }

    private void RecycleProcessedBuffers()
    {
        AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out var processed);
        if (!LogAlError("query processed buffers"))
            return;

        while (processed-- > 0)
        {
            var buffer = AL.SourceUnqueueBuffer(_source);
            if (!LogAlError("unqueue a processed buffer"))
                break;

            if (_bufferSampleCounts.Remove(buffer, out var sampleCount))
                _playedSamples += sampleCount;
            _availableBuffers.Enqueue(buffer);
        }
    }

    private bool QueuePendingPcm()
    {
        while (_availableBuffers.Count > 0 && TryDequeuePcm(out var packet))
        {
            var currentGeneration = Volatile.Read(ref _generation);
            if (packet.Generation != currentGeneration)
                continue;

            var buffer = _availableBuffers.Dequeue();
            var format = _channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;
            AL.BufferData(buffer, format, packet.Samples, _sampleRate);
            if (!LogAlError("fill an audio buffer"))
            {
                _availableBuffers.Enqueue(buffer);
                continue;
            }

            if (packet.Generation != Volatile.Read(ref _generation))
            {
                _availableBuffers.Enqueue(buffer);
                StopOpenAlPipeline();
                return false;
            }

            AL.SourceQueueBuffer(_source, buffer);
            if (!LogAlError("queue an audio buffer"))
            {
                _availableBuffers.Enqueue(buffer);
                continue;
            }

            _bufferSampleCounts[buffer] = packet.Samples.Length;
            if (_firstQueuedTimestamp == 0)
                _firstQueuedTimestamp = Stopwatch.GetTimestamp();

            if (packet.Generation != Volatile.Read(ref _generation))
            {
                StopOpenAlPipeline();
                return false;
            }
        }

        return true;
    }

    private void UpdatePlaybackState()
    {
        AL.GetSource(_source, ALGetSourcei.SourceState, out var stateValue);
        if (!LogAlError("query source state"))
            return;

        AL.GetSource(_source, ALGetSourcei.BuffersQueued, out var queued);
        if (!LogAlError("query queued buffers"))
            return;

        var state = (ALSourceState)stateValue;
        var paused = Volatile.Read(ref _desiredPaused) != 0;
        if (!paused && state != ALSourceState.Playing && queued > 0)
        {
            var waitedLongEnough = _firstQueuedTimestamp != 0 &&
                Stopwatch.GetElapsedTime(_firstQueuedTimestamp).TotalMilliseconds >= InitialStartDelayMilliseconds;
            var shouldStart = _hasStartedOnce ||
                queued >= InitialBufferTarget ||
                _workerInputCompleted ||
                waitedLongEnough;

            if (shouldStart)
            {
                AL.SourcePlay(_source);
                if (LogAlError("start playback"))
                    _hasStartedOnce = true;
            }
        }

        var drained = _workerInputCompleted && queued == 0 && !HasPendingPcm();
        Volatile.Write(ref _isDrained, drained ? 1 : 0);
    }

    private void UpdatePlaybackClock()
    {
        var offsetFrames = 0;
        AL.GetSource(_source, ALGetSourcei.SampleOffset, out offsetFrames);
        if (!LogAlError("query playback offset"))
            offsetFrames = 0;

        var currentSamples = _playedSamples + (long)offsetFrames * _channels;
        PublishPlaybackTime(currentSamples / _channels / (double)_sampleRate);
    }

    private void StopOpenAlPipeline()
    {
        AL.SourceStop(_source);
        LogAlError("stop playback");

        AL.GetSource(_source, ALGetSourcei.BuffersQueued, out var queued);
        if (LogAlError("query buffers while stopping") && queued > 0)
        {
            var buffers = new int[queued];
            AL.SourceUnqueueBuffers(_source, queued, buffers);
            LogAlError("unqueue buffers while stopping");
        }

        _availableBuffers.Clear();
        foreach (var buffer in _allBuffers)
            _availableBuffers.Enqueue(buffer);
        _bufferSampleCounts.Clear();
        _playedSamples = 0;
        _hasStartedOnce = false;
        _firstQueuedTimestamp = 0;
        PublishPlaybackTime(0);
    }

    private void CleanupOpenAl()
    {
        try
        {
            if (_context != ALContext.Null)
                ALC.MakeContextCurrent(_context);

            if (_source != 0)
            {
                try
                {
                    StopOpenAlPipeline();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OpenTKAudioPlayer] Failed to stop during cleanup: {ex.Message}");
                }

                AL.DeleteSource(_source);
                LogAlError("delete the audio source");
                _source = 0;
            }

            foreach (var buffer in _allBuffers)
            {
                AL.DeleteBuffer(buffer);
                LogAlError("delete an audio buffer");
            }
            _allBuffers.Clear();
            _availableBuffers.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OpenTKAudioPlayer] OpenAL cleanup failed: {ex.Message}");
        }
        finally
        {
            try
            {
                if (_context != ALContext.Null)
                {
                    ALC.MakeContextCurrent(ALContext.Null);
                    ALC.DestroyContext(_context);
                    _context = ALContext.Null;
                }

                if (_device != ALDevice.Null)
                {
                    ALC.CloseDevice(_device);
                    _device = ALDevice.Null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenTKAudioPlayer] OpenAL context cleanup failed: {ex.Message}");
            }
        }
    }

    private static float[] DownmixToStereo(float[] input, int inputChannels)
    {
        var frameCount = input.Length / inputChannels;
        var output = new float[frameCount * 2];

        for (var frame = 0; frame < frameCount; frame++)
        {
            var inputOffset = frame * inputChannels;
            var outputOffset = frame * 2;

            if (inputChannels >= 6)
            {
                var frontLeft = input[inputOffset];
                var frontRight = input[inputOffset + 1];
                var center = input[inputOffset + 2];
                var lfe = input[inputOffset + 3];
                var backLeft = input[inputOffset + 4];
                var backRight = input[inputOffset + 5];

                output[outputOffset] = Math.Clamp(frontLeft + 0.707f * center + 0.707f * backLeft + 0.5f * lfe, -1f, 1f);
                output[outputOffset + 1] = Math.Clamp(frontRight + 0.707f * center + 0.707f * backRight + 0.5f * lfe, -1f, 1f);
            }
            else
            {
                float left = 0;
                float right = 0;
                var leftCount = 0;
                var rightCount = 0;
                for (var channel = 0; channel < inputChannels; channel++)
                {
                    if ((channel & 1) == 0)
                    {
                        left += input[inputOffset + channel];
                        leftCount++;
                    }
                    else
                    {
                        right += input[inputOffset + channel];
                        rightCount++;
                    }
                }

                output[outputOffset] = Math.Clamp(left / Math.Max(leftCount, 1), -1f, 1f);
                output[outputOffset + 1] = Math.Clamp(right / Math.Max(rightCount, 1), -1f, 1f);
            }
        }

        return output;
    }

    private void PublishPlaybackTime(double seconds) =>
        Interlocked.Exchange(ref _cachedPlaybackTimeBits, BitConverter.DoubleToInt64Bits(seconds));

    private void ThrowOnAlError(string operation)
    {
        var error = AL.GetError();
        if (error != ALError.NoError)
            throw new InvalidOperationException($"OpenAL failed to {operation} ({error}).");
    }

    private bool LogAlError(string operation)
    {
        var error = AL.GetError();
        if (error == ALError.NoError)
            return true;

        Debug.WriteLine($"[OpenTKAudioPlayer] OpenAL failed to {operation}: {error}");
        _logger.Log("OpenTKAudioPlayer", "OpenALError", new { Operation = operation, Error = error.ToString() });
        return false;
    }

    private void RequestShutdown()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            return;

        Interlocked.Increment(ref _generation);
        Volatile.Write(ref _inputCompleted, 1);
        ClearPendingPcm();
        _commands.Enqueue(new AudioCommand(AudioCommandKind.Shutdown));
        _shutdown.Cancel();
        _wake.Set();
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
            return;

        _logger.Log("OpenTKAudioPlayer", "Dispose", null);
        RequestShutdown();

        if (Thread.CurrentThread == _audioThread)
            return;

        if (!_audioThread.Join(DisposeJoinTimeoutMilliseconds))
        {
            // Native resources remain owned by the still-running audio thread. It will
            // destroy them in its finally block if/when the blocking driver call returns.
            Debug.WriteLine("[OpenTKAudioPlayer] Audio thread did not stop before the dispose timeout; native cleanup was deferred to that thread.");
            _logger.Log("OpenTKAudioPlayer", "DisposeDeferred", new { DisposeJoinTimeoutMilliseconds });
        }
    }

    private readonly record struct PcmPacket(int Generation, short[] Samples);

    private readonly record struct AudioCommand(
        AudioCommandKind Kind,
        float Value = 0,
        int Generation = 0);

    private enum AudioCommandKind
    {
        SetVolume,
        Resume,
        Pause,
        Stop,
        CompleteInput,
        Shutdown
    }
}
