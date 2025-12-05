using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using OpenTK.Audio.OpenAL;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// Cross-platform audio player using OpenAL for low-latency audio playback.
/// Handles sample format conversion and buffer management.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly ALDevice _device;
    private readonly ALContext _context;
    private readonly int _source;
    private readonly ConcurrentQueue<int> _availableBuffers;
    private readonly ConcurrentQueue<float[]> _pendingSamples;
    private readonly Thread _audioThread;
    private readonly CancellationTokenSource _cts;
    private float _volume = 1.0f;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isDisposed;

    private const int BufferCount = 4;
    private const int SamplesPerBuffer = 4096;

    public AudioPlayer(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _channels = Math.Min(channels, 2); // Limit to stereo
        
        // Initialize OpenAL
        _device = ALC.OpenDevice(null);
        if (_device == ALDevice.Null)
        {
            throw new InvalidOperationException("Failed to open audio device");
        }

        _context = ALC.CreateContext(_device, (int[]?)null);
        ALC.MakeContextCurrent(_context);

        // Create source
        _source = AL.GenSource();
        AL.Source(_source, ALSourcef.Gain, _volume);

        // Create buffers
        _availableBuffers = new ConcurrentQueue<int>();
        for (int i = 0; i < BufferCount; i++)
        {
            _availableBuffers.Enqueue(AL.GenBuffer());
        }

        _pendingSamples = new ConcurrentQueue<float[]>();
        _cts = new CancellationTokenSource();

        // Start audio processing thread
        _audioThread = new Thread(AudioLoop)
        {
            Name = "AudioPlayer",
            IsBackground = true
        };
        _audioThread.Start();
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        AL.Source(_source, ALSourcef.Gain, _volume);
    }

    public void QueueSamples(float[] samples)
    {
        if (_isDisposed || _isPaused) return;
        _pendingSamples.Enqueue(samples);
    }

    public void Resume()
    {
        _isPaused = false;
        if (_isPlaying)
        {
            AL.SourcePlay(_source);
        }
    }

    public void Pause()
    {
        _isPaused = true;
        AL.SourcePause(_source);
    }

    public void Stop()
    {
        AL.SourceStop(_source);
        _isPlaying = false;
        
        // Clear pending samples
        while (_pendingSamples.TryDequeue(out _)) { }
        
        // Unqueue all buffers
        AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
        if (queued > 0)
        {
            var buffers = new int[queued];
            AL.SourceUnqueueBuffers(_source, queued, buffers);
            foreach (var buffer in buffers)
            {
                _availableBuffers.Enqueue(buffer);
            }
        }
    }

    private void AudioLoop()
    {
        var token = _cts.Token;
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Check for processed buffers to recycle
                AL.GetSource(_source, ALGetSourcei.BuffersProcessed, out int processed);
                while (processed > 0)
                {
                    var buffer = AL.SourceUnqueueBuffer(_source);
                    _availableBuffers.Enqueue(buffer);
                    processed--;
                }

                // Queue new buffers if we have samples and available buffers
                while (_pendingSamples.TryDequeue(out var samples) && 
                       _availableBuffers.TryDequeue(out var buffer))
                {
                    // Convert float samples to 16-bit PCM
                    var pcmData = new short[samples.Length];
                    for (int i = 0; i < samples.Length; i++)
                    {
                        pcmData[i] = (short)(samples[i] * 32767);
                    }

                    var format = _channels == 1 ? ALFormat.Mono16 : ALFormat.Stereo16;
                    AL.BufferData(buffer, format, pcmData, _sampleRate);
                    AL.SourceQueueBuffer(_source, buffer);
                }

                // Start playing if we have queued buffers and aren't playing
                AL.GetSource(_source, ALGetSourcei.SourceState, out int state);
                if (state != (int)ALSourceState.Playing && !_isPaused)
                {
                    AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                    if (queued > 0)
                    {
                        AL.SourcePlay(_source);
                        _isPlaying = true;
                    }
                }

                Thread.Sleep(5);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayer] Error: {ex.Message}");
                Thread.Sleep(10);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cts.Cancel();
        _audioThread.Join(1000);
        _cts.Dispose();

        Stop();

        // Delete buffers
        while (_availableBuffers.TryDequeue(out var buffer))
        {
            AL.DeleteBuffer(buffer);
        }

        AL.DeleteSource(_source);

        if (_context != ALContext.Null)
        {
            ALC.MakeContextCurrent(ALContext.Null);
            ALC.DestroyContext(_context);
        }

        if (_device != ALDevice.Null)
        {
            ALC.CloseDevice(_device);
        }
    }
}
