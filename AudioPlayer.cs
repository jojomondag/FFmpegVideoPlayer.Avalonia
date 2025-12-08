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
    private readonly int _inputChannels;
    private readonly ALDevice _device;
    private readonly ALContext _context;
    private readonly int _source;
    private readonly ConcurrentQueue<int> _availableBuffers;
    private readonly ConcurrentQueue<float[]> _pendingSamples;
    private readonly ConcurrentQueue<short[]> _pendingS16Samples;
    private readonly Thread _audioThread;
    private readonly CancellationTokenSource _cts;
    private float _volume = 1.0f;
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isDisposed;

    private const int BufferCount = 16;
    private const int SamplesPerBuffer = 8192;

    public AudioPlayer(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _inputChannels = channels;
        _channels = Math.Min(channels, 2); // Output is limited to stereo
        
        Debug.WriteLine($"[AudioPlayer] Initializing: sampleRate={sampleRate}, inputChannels={channels}, outputChannels={_channels}");
        
        // Initialize OpenAL
        _device = ALC.OpenDevice(null);
        if (_device == ALDevice.Null)
        {
            Debug.WriteLine("[AudioPlayer] Failed to open audio device!");
            throw new InvalidOperationException("Failed to open audio device");
        }
        
        Debug.WriteLine($"[AudioPlayer] Audio device opened successfully");

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
        _pendingS16Samples = new ConcurrentQueue<short[]>();
        _cts = new CancellationTokenSource();

        // Start audio processing thread
        _audioThread = new Thread(AudioLoop)
        {
            Name = "AudioPlayer",
            IsBackground = true
        };
        _audioThread.Start();
        
        Debug.WriteLine("[AudioPlayer] Audio thread started");
    }

    public void SetVolume(float volume)
    {
        _volume = Math.Clamp(volume, 0f, 1f);
        AL.Source(_source, ALSourcef.Gain, _volume);
    }

    /// <summary>
    /// Queue pre-converted S16 stereo samples directly (more efficient when using SwrContext)
    /// </summary>
    public unsafe void QueueSamplesS16(short* samples, int sampleCount)
    {
        if (_isDisposed || _isPaused) return;
        
        // Copy to managed array
        var pcmData = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            pcmData[i] = samples[i];
        }
        
        _pendingS16Samples.Enqueue(pcmData);
    }

    public void QueueSamples(float[] samples)
    {
        if (_isDisposed || _isPaused) return;
        
        // If input has more channels than output, downmix
        if (_inputChannels > _channels)
        {
            samples = DownmixToStereo(samples, _inputChannels);
        }
        
        _pendingSamples.Enqueue(samples);
    }
    
    private float[] DownmixToStereo(float[] input, int inputChannels)
    {
        int samplesPerChannel = input.Length / inputChannels;
        var output = new float[samplesPerChannel * 2]; // Stereo output
        
        for (int i = 0; i < samplesPerChannel; i++)
        {
            int inputOffset = i * inputChannels;
            int outputOffset = i * 2;
            
            // Simple downmix: average channels for left/right
            // For 5.1 (6ch): FL, FR, FC, LFE, BL, BR
            // Left = FL + 0.707*FC + 0.707*BL
            // Right = FR + 0.707*FC + 0.707*BR
            
            if (inputChannels >= 6)
            {
                // 5.1 surround downmix
                float fl = input[inputOffset + 0];     // Front Left
                float fr = input[inputOffset + 1];     // Front Right
                float fc = input[inputOffset + 2];     // Front Center
                float lfe = input[inputOffset + 3];    // LFE (subwoofer)
                float bl = input[inputOffset + 4];     // Back Left
                float br = input[inputOffset + 5];     // Back Right
                
                output[outputOffset + 0] = Math.Clamp(fl + 0.707f * fc + 0.707f * bl + 0.5f * lfe, -1f, 1f);
                output[outputOffset + 1] = Math.Clamp(fr + 0.707f * fc + 0.707f * br + 0.5f * lfe, -1f, 1f);
            }
            else
            {
                // Generic downmix: average odd channels to left, even to right
                float left = 0, right = 0;
                for (int c = 0; c < inputChannels; c++)
                {
                    if (c % 2 == 0)
                        left += input[inputOffset + c];
                    else
                        right += input[inputOffset + c];
                }
                output[outputOffset + 0] = Math.Clamp(left / (inputChannels / 2f), -1f, 1f);
                output[outputOffset + 1] = Math.Clamp(right / (inputChannels / 2f), -1f, 1f);
            }
        }
        
        return output;
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
        _hasStartedOnce = false;
        
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

    private static int _queuedBufferCount = 0;
    private bool _hasStartedOnce = false;
    
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

                // Queue S16 samples first (from SwrContext - already in correct format)
                while (_pendingS16Samples.TryDequeue(out var pcmData) && 
                       _availableBuffers.TryDequeue(out var buffer))
                {
                    AL.BufferData(buffer, ALFormat.Stereo16, pcmData, _sampleRate);
                    AL.SourceQueueBuffer(_source, buffer);
                    
                    _queuedBufferCount++;
                    if (_queuedBufferCount <= 5 || _queuedBufferCount % 100 == 0)
                    {
                        Debug.WriteLine($"[AudioPlayer] Queued S16 buffer #{_queuedBufferCount}: {pcmData.Length} samples");
                    }
                }

                // Queue float samples (fallback - convert to S16)
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
                    
                    _queuedBufferCount++;
                    if (_queuedBufferCount <= 5 || _queuedBufferCount % 100 == 0)
                    {
                        Debug.WriteLine($"[AudioPlayer] Queued float buffer #{_queuedBufferCount}: {pcmData.Length} samples, format={format}");
                    }
                }

                // Start or resume playback
                AL.GetSource(_source, ALGetSourcei.SourceState, out int state);
                AL.GetSource(_source, ALGetSourcei.BuffersQueued, out int queued);
                
                if (!_isPaused && state != (int)ALSourceState.Playing && queued > 0)
                {
                    // First time start: wait for enough buffers
                    // Subsequent restarts: start immediately if we have any buffers
                    if (!_hasStartedOnce && queued >= 4)
                    {
                        Debug.WriteLine($"[AudioPlayer] Initial playback start with {queued} queued buffers");
                        AL.SourcePlay(_source);
                        _isPlaying = true;
                        _hasStartedOnce = true;
                    }
                    else if (_hasStartedOnce && queued >= 1)
                    {
                        // Restart after buffer underrun - don't log every time to reduce spam
                        AL.SourcePlay(_source);
                        _isPlaying = true;
                    }
                }

                Thread.Sleep(1); // Very fast polling for smoother audio
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
