using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;
using FFmpeg.AutoGen;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>
/// FFmpeg-based media player that decodes video and audio.
/// Provides cross-platform support including ARM64 macOS.
/// Uses FFmpeg.AutoGen 8.x bindings (requires FFmpeg 8.x / libavcodec.62).
/// </summary>
public sealed unsafe class FFmpegMediaPlayer : IDisposable
{
    private AVFormatContext* _formatContext;
    private AVCodecContext* _videoCodecContext;
    private AVCodecContext* _audioCodecContext;
    private SwsContext* _swsContext;
    private AVFrame* _frame;
    private AVFrame* _rgbFrame;
    private AVPacket* _packet;
    
    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;
    
    private byte* _rgbBuffer;
    private int _rgbBufferSize;
    
    private Thread? _playbackThread;
    private CancellationTokenSource? _cancellationTokenSource;
    
    private bool _isPlaying;
    private bool _isPaused;
    private bool _isDisposed;
    private double _position;
    private double _duration;
    private int _volume = 100;
    private readonly object _lock = new();
    
    private int _videoWidth;
    private int _videoHeight;
    private double _frameRate;
    
    // Audio playback
    private AudioPlayer? _audioPlayer;
    private SwrContext* _swrContext;
    private const int MaxPendingFrames = 4;
    private int _pendingFrameCount;
    private int _droppedFrames;

    /// <summary>
    /// Gets whether media is currently playing.
    /// </summary>
    public bool IsPlaying => _isPlaying && !_isPaused;

    /// <summary>
    /// Gets the current position as a percentage (0.0 to 1.0).
    /// </summary>
    public float Position => _duration > 0 ? (float)(_position / _duration) : 0f;

    /// <summary>
    /// Gets the total duration in milliseconds.
    /// </summary>
    public long Length => (long)(_duration * 1000);

    /// <summary>
    /// Gets or sets the volume (0-100).
    /// </summary>
    public int Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _audioPlayer?.SetVolume(_volume / 100f);
        }
    }

    /// <summary>
    /// Gets the video width.
    /// </summary>
    public int VideoWidth => _videoWidth;

    /// <summary>
    /// Gets the video height.
    /// </summary>
    public int VideoHeight => _videoHeight;

    /// <summary>
    /// Raised when the position changes during playback.
    /// </summary>
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>
    /// Raised when the media duration becomes known.
    /// </summary>
    public event EventHandler<LengthChangedEventArgs>? LengthChanged;

    /// <summary>
    /// Raised when a new video frame is available.
    /// </summary>
    public event EventHandler<FrameEventArgs>? FrameReady;

    /// <summary>
    /// Raised when playback starts.
    /// </summary>
    public event EventHandler? Playing;

    /// <summary>
    /// Raised when playback is paused.
    /// </summary>
    public event EventHandler? Paused;

    /// <summary>
    /// Raised when playback is stopped.
    /// </summary>
    public event EventHandler? Stopped;

    /// <summary>
    /// Raised when media reaches the end.
    /// </summary>
    public event EventHandler? EndReached;

    /// <summary>
    /// Opens a media file for playback.
    /// </summary>
    /// <param name="path">The path to the media file.</param>
    /// <returns>True if the file was opened successfully.</returns>
    public bool Open(string path)
    {
        lock (_lock)
        {
            CloseInternal();

            _pendingFrameCount = 0;
            _droppedFrames = 0;

            fixed (AVFormatContext** formatContext = &_formatContext)
            {
                if (ffmpeg.avformat_open_input(formatContext, path, null, null) != 0)
                {
                    Debug.WriteLine($"[FFmpegMediaPlayer] Failed to open media: {path}");
                    return false;
                }
            }

            if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Failed to read stream info.");
                CloseInternal();
                return false;
            }

            // Find video and audio streams
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                var codecType = _formatContext->streams[i]->codecpar->codec_type;
                if (codecType == AVMediaType.AVMEDIA_TYPE_VIDEO && _videoStreamIndex < 0)
                {
                    _videoStreamIndex = i;
                }
                else if (codecType == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex < 0)
                {
                    _audioStreamIndex = i;
                }
            }

            if (_videoStreamIndex < 0 && _audioStreamIndex < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] No playable streams found in media.");
                CloseInternal();
                return false;
            }

            // Initialize video decoder
            if (_videoStreamIndex >= 0 && !InitializeVideoDecoder())
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Unable to initialize video decoder. Video stream disabled.");
                _videoStreamIndex = -1;
            }

            // Initialize audio decoder
            if (_audioStreamIndex >= 0 && !InitializeAudioDecoder())
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Unable to initialize audio decoder. Audio stream disabled.");
                _audioStreamIndex = -1;
            }

            if (_videoStreamIndex < 0 && _audioStreamIndex < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Media does not contain a supported audio or video stream.");
                CloseInternal();
                return false;
            }

            // Get duration
            if (_formatContext->duration != ffmpeg.AV_NOPTS_VALUE)
            {
                _duration = _formatContext->duration / (double)ffmpeg.AV_TIME_BASE;
                LengthChanged?.Invoke(this, new LengthChangedEventArgs((long)(_duration * 1000)));
            }

            // Allocate packet
            _packet = ffmpeg.av_packet_alloc();
            if (_packet == null)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Failed to allocate packet.");
                CloseInternal();
                return false;
            }

            _pendingFrameCount = 0;
            _droppedFrames = 0;

            return true;
        }
    }

    private bool InitializeVideoDecoder()
    {
        var stream = _formatContext->streams[_videoStreamIndex];
        var codecParams = stream->codecpar;

        var codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
        if (codec == null)
        {
            return false;
        }

        _videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (ffmpeg.avcodec_parameters_to_context(_videoCodecContext, codecParams) < 0)
        {
            return false;
        }

        // Enable multi-threaded decoding
        _videoCodecContext->thread_count = Math.Max(1, Environment.ProcessorCount - 1);
        _videoCodecContext->thread_type = ffmpeg.FF_THREAD_FRAME | ffmpeg.FF_THREAD_SLICE;

        if (ffmpeg.avcodec_open2(_videoCodecContext, codec, null) < 0)
        {
            return false;
        }

        _videoWidth = _videoCodecContext->width;
        _videoHeight = _videoCodecContext->height;

        // Calculate frame rate
        var timeBase = stream->avg_frame_rate;
        _frameRate = timeBase.num > 0 && timeBase.den > 0 
            ? (double)timeBase.num / timeBase.den 
            : 30.0;

        // Allocate frames
        _frame = ffmpeg.av_frame_alloc();
        _rgbFrame = ffmpeg.av_frame_alloc();

        // Set up RGB frame
        _rgbBufferSize = ffmpeg.av_image_get_buffer_size(AVPixelFormat.AV_PIX_FMT_BGRA, _videoWidth, _videoHeight, 1);
        _rgbBuffer = (byte*)ffmpeg.av_malloc((ulong)_rgbBufferSize);

        // Fill the RGB frame data pointers using ref parameters
        byte_ptrArray4 dataPtr = new byte_ptrArray4();
        int_array4 linesizePtr = new int_array4();
        
        ffmpeg.av_image_fill_arrays(ref dataPtr, ref linesizePtr, _rgbBuffer, AVPixelFormat.AV_PIX_FMT_BGRA, _videoWidth, _videoHeight, 1);
        
        _rgbFrame->data[0] = dataPtr[0];
        _rgbFrame->data[1] = dataPtr[1];
        _rgbFrame->data[2] = dataPtr[2];
        _rgbFrame->data[3] = dataPtr[3];
        _rgbFrame->linesize[0] = linesizePtr[0];
        _rgbFrame->linesize[1] = linesizePtr[1];
        _rgbFrame->linesize[2] = linesizePtr[2];
        _rgbFrame->linesize[3] = linesizePtr[3];

        // Initialize scaler
        _swsContext = ffmpeg.sws_getContext(
            _videoWidth, _videoHeight, _videoCodecContext->pix_fmt,
            _videoWidth, _videoHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);

        return _swsContext != null;
    }

    private bool InitializeAudioDecoder()
    {
        var stream = _formatContext->streams[_audioStreamIndex];
        var codecParams = stream->codecpar;

        var codec = ffmpeg.avcodec_find_decoder(codecParams->codec_id);
        if (codec == null)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] Audio codec not found");
            return false;
        }

        _audioCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (ffmpeg.avcodec_parameters_to_context(_audioCodecContext, codecParams) < 0)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] Failed to copy audio codec params");
            return false;
        }

        if (ffmpeg.avcodec_open2(_audioCodecContext, codec, null) < 0)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] Failed to open audio codec");
            return false;
        }

        // Initialize audio player and resampler
        try
        {
            var sampleRate = _audioCodecContext->sample_rate;
            var channels = _audioCodecContext->ch_layout.nb_channels;
            Debug.WriteLine($"[FFmpegMediaPlayer] Audio: sampleRate={sampleRate}, channels={channels}");
            
            // Initialize SwrContext for audio resampling to stereo S16
            _swrContext = ffmpeg.swr_alloc();
            
            // Set input options
            AVChannelLayout inChLayout = _audioCodecContext->ch_layout;
            ffmpeg.av_opt_set_chlayout(_swrContext, "in_chlayout", &inChLayout, 0);
            ffmpeg.av_opt_set_int(_swrContext, "in_sample_rate", sampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrContext, "in_sample_fmt", _audioCodecContext->sample_fmt, 0);
            
            // Set output options - stereo S16 for OpenAL
            AVChannelLayout outChLayout;
            ffmpeg.av_channel_layout_default(&outChLayout, 2); // Stereo
            ffmpeg.av_opt_set_chlayout(_swrContext, "out_chlayout", &outChLayout, 0);
            ffmpeg.av_opt_set_int(_swrContext, "out_sample_rate", sampleRate, 0);
            ffmpeg.av_opt_set_sample_fmt(_swrContext, "out_sample_fmt", AVSampleFormat.AV_SAMPLE_FMT_S16, 0);
            
            if (ffmpeg.swr_init(_swrContext) < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Failed to initialize SwrContext");
                var ctx = _swrContext;
                ffmpeg.swr_free(&ctx);
                _swrContext = null;
            }
            else
            {
                Debug.WriteLine("[FFmpegMediaPlayer] SwrContext initialized successfully");
            }
            
            _audioPlayer = new AudioPlayer(sampleRate, 2); // Always output stereo
            _audioPlayer.SetVolume(_volume / 100f);
            Debug.WriteLine("[FFmpegMediaPlayer] AudioPlayer created successfully");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FFmpegMediaPlayer] AudioPlayer creation failed: {ex.Message}");
            _audioPlayer = null;
        }

        return true;
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    public void Play()
    {
        lock (_lock)
        {
            if (_formatContext == null) return;

            if (_isPaused)
            {
                _isPaused = false;
                _audioPlayer?.Resume();
                Playing?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_isPlaying) return;

            _isPlaying = true;
            _isPaused = false;
            _cancellationTokenSource = new CancellationTokenSource();
            
            _playbackThread = new Thread(PlaybackLoop)
            {
                Name = "FFmpegPlayback",
                IsBackground = true
            };
            _playbackThread.Start(_cancellationTokenSource.Token);

            Playing?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (!_isPlaying || _isPaused) return;
            _isPaused = true;
            _audioPlayer?.Pause();
            Paused?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stops playback.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isPlaying) return;

            _cancellationTokenSource?.Cancel();
            _playbackThread?.Join(1000);
            _playbackThread = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _isPlaying = false;
            _isPaused = false;
            _position = 0;

            // Seek to beginning
            if (_formatContext != null)
            {
                ffmpeg.av_seek_frame(_formatContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
            }

            _audioPlayer?.Stop();
            Stopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    /// <param name="positionPercent">Position as a percentage (0.0 to 1.0).</param>
    public void Seek(float positionPercent)
    {
        lock (_lock)
        {
            if (_formatContext == null) return;

            var targetTime = (long)(_duration * positionPercent * ffmpeg.AV_TIME_BASE);
            ffmpeg.av_seek_frame(_formatContext, -1, targetTime, ffmpeg.AVSEEK_FLAG_BACKWARD);
            
            if (_videoCodecContext != null)
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);
            if (_audioCodecContext != null)
                ffmpeg.avcodec_flush_buffers(_audioCodecContext);
        }
    }

    private void PlaybackLoop(object? state)
    {
        var token = (CancellationToken)state!;
        var frameTime = 1000.0 / _frameRate;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var nextVideoTime = 0.0;

        while (!token.IsCancellationRequested)
        {
            if (_isPaused)
            {
                Thread.Sleep(10);
                stopwatch.Restart();
                nextVideoTime = 0.0;
                continue;
            }

            int readResult;
            lock (_lock)
            {
                if (_formatContext == null || _packet == null) break;
                readResult = ffmpeg.av_read_frame(_formatContext, _packet);
            }

            if (readResult < 0)
            {
                // End of file or error
                Dispatcher.UIThread.Post(() => EndReached?.Invoke(this, EventArgs.Empty));
                break;
            }

            try
            {
                lock (_lock)
                {
                    if (_packet->stream_index == _videoStreamIndex && _videoCodecContext != null)
                    {
                        // Check if we're ahead of schedule - wait for the right time
                        var currentTime = stopwatch.Elapsed.TotalMilliseconds;
                        if (currentTime < nextVideoTime)
                        {
                            var waitTime = (int)(nextVideoTime - currentTime);
                            if (waitTime > 1)
                            {
                                Thread.Sleep(waitTime);
                            }
                        }
                        
                        ProcessVideoPacket();
                        nextVideoTime += frameTime;
                        
                        // If we're falling behind, catch up by skipping timing
                        currentTime = stopwatch.Elapsed.TotalMilliseconds;
                        if (currentTime > nextVideoTime + frameTime * 2)
                        {
                            nextVideoTime = currentTime; // Reset timing to catch up
                        }
                    }
                    else if (_packet->stream_index == _audioStreamIndex && _audioCodecContext != null)
                    {
                        ProcessAudioPacket();
                    }
                }
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }

        _isPlaying = false;
    }

    private void ProcessVideoPacket()
    {
        var sendResult = ffmpeg.avcodec_send_packet(_videoCodecContext, _packet);
        if (sendResult < 0)
        {
            return;
        }

        int frameCount = 0;
        while (ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame) >= 0)
        {
            frameCount++;
            // Convert to BGRA
            ffmpeg.sws_scale(_swsContext,
                _frame->data, _frame->linesize, 0, _videoHeight,
                _rgbFrame->data, _rgbFrame->linesize);

            // Update position
            var pts = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : _frame->best_effort_timestamp;
            if (pts != ffmpeg.AV_NOPTS_VALUE)
            {
                var timeBase = _formatContext->streams[_videoStreamIndex]->time_base;
                _position = pts * timeBase.num / (double)timeBase.den;
                
                Dispatcher.UIThread.Post(() =>
                {
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
                });
            }

            // Notify frame ready with pooled buffer and bounded queue to prevent UI overload
            var pendingFrames = Interlocked.Increment(ref _pendingFrameCount);
            if (pendingFrames > MaxPendingFrames)
            {
                Interlocked.Decrement(ref _pendingFrameCount);
                var drops = Interlocked.Increment(ref _droppedFrames);
                if (drops <= 5 || drops % 60 == 0)
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] Dropping video frame to keep UI responsive.");
                }
                continue;
            }

            var stride = _rgbFrame->linesize[0];
            var width = _videoWidth;
            var height = _videoHeight;
            var bufferSize = _rgbBufferSize;

            byte[] frameBuffer;
            try
            {
                frameBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _pendingFrameCount);
                Debug.WriteLine($"[FFmpegMediaPlayer] Unable to rent frame buffer of {bufferSize} bytes: {ex.Message}");
                break;
            }

            try
            {
                Marshal.Copy((IntPtr)_rgbFrame->data[0], frameBuffer, 0, bufferSize);
            }
            catch (Exception ex)
            {
                ArrayPool<byte>.Shared.Return(frameBuffer);
                Interlocked.Decrement(ref _pendingFrameCount);
                Debug.WriteLine($"[FFmpegMediaPlayer] Failed to copy frame data: {ex.Message}");
                break;
            }

            var eventArgs = new FrameEventArgs(
                frameBuffer,
                width,
                height,
                stride,
                bufferSize,
                pooled: true,
                releaseAction: buffer => ArrayPool<byte>.Shared.Return(buffer));

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    FrameReady?.Invoke(this, eventArgs);
                }
                finally
                {
                    Interlocked.Decrement(ref _pendingFrameCount);
                }
            });
        }
    }

    private static int _audioPacketCount = 0;
    
    private void ProcessAudioPacket()
    {
        if (_audioPlayer == null)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] ProcessAudioPacket: _audioPlayer is null!");
            return;
        }
        if (ffmpeg.avcodec_send_packet(_audioCodecContext, _packet) < 0)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] ProcessAudioPacket: avcodec_send_packet failed");
            return;
        }

        var tempFrame = ffmpeg.av_frame_alloc();
        try
        {
            while (ffmpeg.avcodec_receive_frame(_audioCodecContext, tempFrame) >= 0)
            {
                var samples = tempFrame->nb_samples;
                var channels = _audioCodecContext->ch_layout.nb_channels;
                
                _audioPacketCount++;
                if (_audioPacketCount <= 5 || _audioPacketCount % 100 == 0)
                {
                    var format = (AVSampleFormat)tempFrame->format;
                    Debug.WriteLine($"[FFmpegMediaPlayer] Audio frame #{_audioPacketCount}: samples={samples}, channels={channels}, format={format}");
                }
                
                // Use SwrContext for proper resampling
                if (_swrContext != null)
                {
                    // Calculate output samples
                    var outSamples = (int)ffmpeg.swr_get_delay(_swrContext, _audioCodecContext->sample_rate) + samples;
                    
                    // Allocate output buffer (stereo S16 = 2 channels * 2 bytes per sample)
                    var outBufferSize = outSamples * 2; // stereo sample count
                    var outData = new short[outBufferSize];
                    
                    fixed (short* outPtr = outData)
                    {
                        var outBuffer = stackalloc byte*[1];
                        outBuffer[0] = (byte*)outPtr;
                        
                        // Resample
                        var convertedSamples = ffmpeg.swr_convert(
                            _swrContext,
                            outBuffer, outSamples,
                            tempFrame->extended_data, samples);
                        
                        if (convertedSamples > 0)
                        {
                            // Queue the S16 samples directly
                            _audioPlayer.QueueSamplesS16(outPtr, convertedSamples * 2); // stereo
                        }
                    }
                }
                else
                {
                    // Fallback to manual conversion
                    var floatBuffer = new float[samples * 2]; // Output stereo
                    var format2 = (AVSampleFormat)tempFrame->format;
                    ConvertAudioSamples(tempFrame, floatBuffer, samples, channels, format2);
                    _audioPlayer.QueueSamples(floatBuffer);
                }
            }
        }
        finally
        {
            ffmpeg.av_frame_free(&tempFrame);
        }
    }

    private void ConvertAudioSamples(AVFrame* frame, float[] output, int samples, int channels, AVSampleFormat format)
    {
        int outputIndex = 0;
        
        for (int s = 0; s < samples; s++)
        {
            for (int c = 0; c < channels; c++)
            {
                float value = 0f;
                uint sampleIndex = (uint)(s * channels + c);
                uint channelIndex = (uint)c;
                uint planarIndex = (uint)s;
                
                switch (format)
                {
                    case AVSampleFormat.AV_SAMPLE_FMT_FLT:
                        value = ((float*)frame->data[0])[sampleIndex];
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_FLTP:
                        value = ((float*)frame->data[channelIndex])[planarIndex];
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S16:
                        value = ((short*)frame->data[0])[sampleIndex] / 32768f;
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S16P:
                        value = ((short*)frame->data[channelIndex])[planarIndex] / 32768f;
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S32:
                        value = ((int*)frame->data[0])[sampleIndex] / 2147483648f;
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S32P:
                        value = ((int*)frame->data[channelIndex])[planarIndex] / 2147483648f;
                        break;
                    default:
                        // Try to handle as planar float
                        if (frame->data[channelIndex] != null)
                        {
                            value = ((float*)frame->data[channelIndex])[planarIndex];
                        }
                        break;
                }
                
                output[outputIndex++] = Math.Clamp(value, -1f, 1f);
            }
        }
    }

    private void CloseInternal()
    {
        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }

        if (_swrContext != null)
        {
            var ctx = _swrContext;
            ffmpeg.swr_free(&ctx);
            _swrContext = null;
        }

        if (_rgbBuffer != null)
        {
            ffmpeg.av_free(_rgbBuffer);
            _rgbBuffer = null;
        }

        if (_frame != null)
        {
            var frame = _frame;
            ffmpeg.av_frame_free(&frame);
            _frame = null;
        }

        if (_rgbFrame != null)
        {
            var frame = _rgbFrame;
            ffmpeg.av_frame_free(&frame);
            _rgbFrame = null;
        }

        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_videoCodecContext != null)
        {
            var ctx = _videoCodecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _videoCodecContext = null;
        }

        if (_audioCodecContext != null)
        {
            var ctx = _audioCodecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _audioCodecContext = null;
        }

        if (_formatContext != null)
        {
            var ctx = _formatContext;
            ffmpeg.avformat_close_input(&ctx);
            _formatContext = null;
        }

        _audioPlayer?.Dispose();
        _audioPlayer = null;

        _videoStreamIndex = -1;
        _audioStreamIndex = -1;
        _position = 0;
        _duration = 0;
        _pendingFrameCount = 0;
        _droppedFrames = 0;
    }

    /// <summary>
    /// Closes the current media and releases resources.
    /// </summary>
    public void Close()
    {
        Stop();
        lock (_lock)
        {
            CloseInternal();
        }
    }

    /// <summary>
    /// Disposes the media player and all resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Close();
    }
}

/// <summary>
/// Event arguments for position change events.
/// </summary>
public class PositionChangedEventArgs : EventArgs
{
    public float Position { get; }
    public PositionChangedEventArgs(float position) => Position = position;
}

/// <summary>
/// Event arguments for length change events.
/// </summary>
public class LengthChangedEventArgs : EventArgs
{
    public long Length { get; }
    public LengthChangedEventArgs(long length) => Length = length;
}

/// <summary>
/// Event arguments for frame ready events.
/// </summary>
public sealed class FrameEventArgs : EventArgs, IDisposable
{
    public byte[] Data { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public int DataLength { get; }
    private readonly Action<byte[]>? _releaseAction;
    private bool _disposed;
    
    public FrameEventArgs(byte[] data, int width, int height, int stride)
        : this(data, width, height, stride, data?.Length ?? 0, pooled: false, releaseAction: null)
    {
    }

    internal FrameEventArgs(byte[] data, int width, int height, int stride, int dataLength, bool pooled, Action<byte[]>? releaseAction)
    {
        Data = data;
        Width = width;
        Height = height;
        Stride = stride;
        DataLength = dataLength;
        _releaseAction = pooled ? releaseAction : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _releaseAction?.Invoke(Data);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~FrameEventArgs()
    {
        Dispose();
    }
}
