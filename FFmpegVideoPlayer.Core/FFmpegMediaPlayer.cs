using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace FFmpegVideoPlayer.Core;

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
    private IAudioPlayer? _audioPlayer;
    private readonly Func<int, int, IAudioPlayer?>? _audioPlayerFactory;

    /// <summary>
    /// Gets or sets the audio player. When changed, ensures state synchronization.
    /// </summary>
    public IAudioPlayer? AudioPlayer
    {
        get => _audioPlayer;
        set
        {
            lock (_lock)
            {
                // Dispose old audio player
                _audioPlayer?.Dispose();

                // Set new audio player
                _audioPlayer = value;

                // Sync state with new audio player
                if (_audioPlayer != null)
                {
                    _audioPlayer.SetVolume(_volume / 100f);
                    SyncAudioPlayerState();
                }
            }
        }
    }
    private SwrContext* _swrContext;
    private const int MaxPendingFrames = 4;
    private int _pendingFrameCount;
    private int _droppedFrames;
    
    // Synchronization
    private double _startTime; // Master clock start time (in seconds, from media PTS)
    private double _playbackStartWallTime; // Wall clock time when playback started (stopwatch time)
    private double _audioStartPts; // First audio frame's PTS (for audio-video sync)
    private bool _needsResync; // Flag to trigger resync after seek
    private double _audioClock; // Current audio playback time (in seconds)
    private double _videoClock; // Current video presentation time (in seconds)
    private AVRational _videoTimeBase;
    private AVRational _audioTimeBase;
    private double _pauseStartTime; // When pause started (stopwatch time)
    private double _totalPauseTime; // Total accumulated pause time

    // Frame-accurate seek: av_seek_frame with AVSEEK_FLAG_BACKWARD lands on a keyframe
    // at-or-before the target, which can be several seconds earlier with long GOPs.
    // When this is >= 0, decoded frames with pts < _seekTargetPts are consumed but not
    // displayed/queued, so playback resumes at the exact position the user asked for.
    // Cleared (set to -1) as soon as we see the first frame at or after the target.
    private const double SeekTargetEpsilon = 0.001; // 1 ms tolerance
    private double _seekTargetPts = -1;
    
    // Frame stepping
    private bool _stepMode; // True when in step mode (frame-by-frame)
    private readonly Queue<CachedFrame> _frameCache = new(); // Cache for backward stepping
    private const int MaxCachedFrames = 30; // Cache up to 30 frames for backward stepping
    private double _lastFramePts = -1; // PTS of last displayed frame

    
    // Logging
    private readonly PlayerLogger _logger = new();
    
    // Synchronization callback for UI thread marshalling (optional, can be null)
    private readonly Action<Action>? _synchronizationCallback;
    
    /// <summary>
    /// Cached frame data for backward stepping.
    /// </summary>
    private sealed class CachedFrame
    {
        public byte[] Data { get; }
        public int Width { get; }
        public int Height { get; }
        public int Stride { get; }
        public int DataLength { get; }
        public double Pts { get; }

        public CachedFrame(byte[] data, int width, int height, int stride, int dataLength, double pts)
        {
            Data = data;
            Width = width;
            Height = height;
            Stride = stride;
            DataLength = dataLength;
            Pts = pts;
        }
    }

    /// <summary>
    /// Initializes a new instance of FFmpegMediaPlayer.
    /// </summary>
    /// <param name="synchronizationCallback">Optional callback to marshal events to UI thread. If null, events are raised on the current thread.</param>
    /// <param name="audioPlayerFactory">Optional factory function to create audio players. Signature: (sampleRate, channels) => IAudioPlayer?. If null, audio playback will be disabled.</param>
    public FFmpegMediaPlayer(Action<Action>? synchronizationCallback = null, Func<int, int, IAudioPlayer?>? audioPlayerFactory = null)
    {
        _synchronizationCallback = synchronizationCallback;
        _audioPlayerFactory = audioPlayerFactory;
    }

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
            var oldVolume = _volume;
            _volume = Math.Clamp(value, 0, 100);
            _audioPlayer?.SetVolume(_volume / 100f);
            _logger.Log("FFmpegMediaPlayer", "VolumeChanged", new { OldVolume = oldVolume, NewVolume = _volume });
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
    /// Gets whether the currently opened media has a supported video stream.
    /// </summary>
    public bool HasVideo => _videoStreamIndex >= 0 && _videoCodecContext != null;

    /// <summary>
    /// Gets whether the currently opened media has a supported audio stream.
    /// </summary>
    public bool HasAudio => _audioStreamIndex >= 0 && _audioCodecContext != null;

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
            // Clear logs when opening a new movie
            _logger.Clear();
            _logger.Log("FFmpegMediaPlayer", "MovieLoadingStarted", new { Path = path, Timestamp = DateTime.Now });
            
            CloseInternal();

            _pendingFrameCount = 0;
            _droppedFrames = 0;

            _logger.Log("FFmpegMediaPlayer", "OpeningMediaFile", new { Path = path });
            
            fixed (AVFormatContext** formatContext = &_formatContext)
            {
                if (ffmpeg.avformat_open_input(formatContext, path, null, null) != 0)
                {
                    Debug.WriteLine($"[FFmpegMediaPlayer] Failed to open media: {path}");
                    _logger.Log("FFmpegMediaPlayer", "OpenFailed", new { Path = path, Reason = "avformat_open_input failed" });
                    return false;
                }
            }
            
            _logger.Log("FFmpegMediaPlayer", "MediaFileOpened", new { Path = path });

            _logger.Log("FFmpegMediaPlayer", "ReadingStreamInfo", null);
            if (ffmpeg.avformat_find_stream_info(_formatContext, null) < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] Failed to read stream info.");
                _logger.Log("FFmpegMediaPlayer", "StreamInfoFailed", new { Reason = "avformat_find_stream_info failed" });
                CloseInternal();
                return false;
            }
            
            // Collect detailed movie information
            var movieInfo = new Dictionary<string, object?>
            {
                ["TotalStreams"] = _formatContext->nb_streams,
                ["Duration"] = _formatContext->duration != ffmpeg.AV_NOPTS_VALUE 
                    ? _formatContext->duration / (double)ffmpeg.AV_TIME_BASE 
                    : 0,
                ["FormatName"] = Marshal.PtrToStringAnsi((IntPtr)_formatContext->iformat->name) ?? "unknown",
                ["FormatLongName"] = Marshal.PtrToStringAnsi((IntPtr)_formatContext->iformat->long_name) ?? "unknown",
                ["BitRate"] = _formatContext->bit_rate > 0 ? _formatContext->bit_rate : 0,
                ["StartTime"] = _formatContext->start_time != ffmpeg.AV_NOPTS_VALUE 
                    ? _formatContext->start_time / (double)ffmpeg.AV_TIME_BASE 
                    : 0
            };
            
            // Get file size if available
            try
            {
                var fileInfo = new FileInfo(path);
                movieInfo["FileSize"] = fileInfo.Length;
                movieInfo["FileSizeMB"] = Math.Round(fileInfo.Length / (1024.0 * 1024.0), 2);
            }
            catch
            {
                movieInfo["FileSize"] = "unknown";
            }
            
            // Extract metadata
            var metadata = new Dictionary<string, string>();
            if (_formatContext->metadata != null)
            {
                AVDictionaryEntry* tag = null;
                while ((tag = ffmpeg.av_dict_get(_formatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
                {
                    var key = Marshal.PtrToStringAnsi((IntPtr)tag->key) ?? "";
                    var value = Marshal.PtrToStringAnsi((IntPtr)tag->value) ?? "";
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        metadata[key] = value;
                    }
                }
            }
            if (metadata.Count > 0)
            {
                movieInfo["Metadata"] = metadata;
            }
            
            // Collect all stream information
            var allStreams = new List<object>();
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                var stream = _formatContext->streams[i];
                var codecParams = stream->codecpar;
                var streamInfo = new Dictionary<string, object?>
                {
                    ["StreamIndex"] = i,
                    ["CodecType"] = codecParams->codec_type.ToString(),
                    ["CodecId"] = codecParams->codec_id.ToString(),
                    ["TimeBase"] = new { Num = stream->time_base.num, Den = stream->time_base.den },
                    ["StartTime"] = stream->start_time != ffmpeg.AV_NOPTS_VALUE 
                        ? stream->start_time * stream->time_base.num / (double)stream->time_base.den 
                        : 0,
                    ["Duration"] = stream->duration != ffmpeg.AV_NOPTS_VALUE 
                        ? stream->duration * stream->time_base.num / (double)stream->time_base.den 
                        : 0,
                    ["NbFrames"] = stream->nb_frames > 0 ? stream->nb_frames : 0
                };
                
                // Add codec-specific information
                if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    streamInfo["Width"] = codecParams->width;
                    streamInfo["Height"] = codecParams->height;
                    streamInfo["PixelFormat"] = codecParams->format.ToString();
                    streamInfo["BitRate"] = codecParams->bit_rate > 0 ? codecParams->bit_rate : 0;
                    if (stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0)
                    {
                        streamInfo["AvgFrameRate"] = (double)stream->avg_frame_rate.num / stream->avg_frame_rate.den;
                    }
                    if (stream->r_frame_rate.num > 0 && stream->r_frame_rate.den > 0)
                    {
                        streamInfo["RealFrameRate"] = (double)stream->r_frame_rate.num / stream->r_frame_rate.den;
                    }
                }
                else if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO)
                {
                    streamInfo["SampleRate"] = codecParams->sample_rate;
                    streamInfo["Channels"] = codecParams->ch_layout.nb_channels;
                    streamInfo["SampleFormat"] = codecParams->format.ToString();
                    streamInfo["BitRate"] = codecParams->bit_rate > 0 ? codecParams->bit_rate : 0;
                    streamInfo["FrameSize"] = codecParams->frame_size > 0 ? codecParams->frame_size : 0;
                }
                else if (codecParams->codec_type == AVMediaType.AVMEDIA_TYPE_SUBTITLE)
                {
                    streamInfo["SubtitleType"] = "Subtitle";
                }
                
                allStreams.Add(streamInfo);
            }
            movieInfo["AllStreams"] = allStreams;
            
            _logger.Log("FFmpegMediaPlayer", "StreamInfoRead", movieInfo);

            // Find video and audio streams
            for (int i = 0; i < _formatContext->nb_streams; i++)
            {
                var stream = _formatContext->streams[i];
                var codecType = stream->codecpar->codec_type;

                // Embedded cover art (ID3 APIC / MP4 "covr" etc.) is exposed by FFmpeg as a
                // single-frame AVMEDIA_TYPE_VIDEO stream flagged AV_DISPOSITION_ATTACHED_PIC.
                // Treating it as real video pulls the player out of the audio-only code path
                // (WaitForAudioOnlyPlaybackToFinish / UpdateAudioOnlyPositionFromPlaybackClock
                // both bail out whenever _videoStreamIndex >= 0), which removes all real-time
                // pacing and lets decode race through the whole file in well under a second.
                // Most tagged mp3s carry embedded artwork, so skip attached-picture streams
                // when picking the playback video stream.
                bool isAttachedPic = (stream->disposition & ffmpeg.AV_DISPOSITION_ATTACHED_PIC) != 0;

                if (codecType == AVMediaType.AVMEDIA_TYPE_VIDEO && !isAttachedPic && _videoStreamIndex < 0)
                {
                    _videoStreamIndex = i;
                    _logger.Log("FFmpegMediaPlayer", "VideoStreamFound", new { StreamIndex = i });
                }
                else if (codecType == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex < 0)
                {
                    _audioStreamIndex = i;
                    _logger.Log("FFmpegMediaPlayer", "AudioStreamFound", new { StreamIndex = i });
                }
                else if (codecType == AVMediaType.AVMEDIA_TYPE_VIDEO && isAttachedPic)
                {
                    _logger.Log("FFmpegMediaPlayer", "AttachedPictureStreamSkipped", new { StreamIndex = i });
                }
            }

            if (_videoStreamIndex < 0 && _audioStreamIndex < 0)
            {
                Debug.WriteLine("[FFmpegMediaPlayer] No playable streams found in media.");
                CloseInternal();
                return false;
            }

            // Initialize video decoder
            if (_videoStreamIndex >= 0)
            {
                _logger.Log("FFmpegMediaPlayer", "InitializingVideoDecoder", new { StreamIndex = _videoStreamIndex });
                var stream = _formatContext->streams[_videoStreamIndex];
                var codecParams = stream->codecpar;
                _logger.Log("FFmpegMediaPlayer", "VideoStreamInfo", new 
                { 
                    StreamIndex = _videoStreamIndex,
                    CodecId = codecParams->codec_id.ToString(),
                    Width = codecParams->width,
                    Height = codecParams->height,
                    PixelFormat = codecParams->format.ToString(),
                    BitRate = codecParams->bit_rate,
                    TimeBase = new { Num = stream->time_base.num, Den = stream->time_base.den },
                    AvgFrameRate = stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0
                        ? (double)stream->avg_frame_rate.num / stream->avg_frame_rate.den
                        : 0
                });
                
                if (!InitializeVideoDecoder())
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] Unable to initialize video decoder. Video stream disabled.");
                    _logger.Log("FFmpegMediaPlayer", "VideoDecoderInitFailed", new { StreamIndex = _videoStreamIndex });
                    _videoStreamIndex = -1;
                }
                else
                {
                    _videoTimeBase = stream->time_base;
                    _logger.Log("FFmpegMediaPlayer", "VideoDecoderInitialized", new 
                    { 
                        StreamIndex = _videoStreamIndex,
                        Width = _videoWidth,
                        Height = _videoHeight,
                        FrameRate = _frameRate,
                        TimeBase = new { Num = _videoTimeBase.num, Den = _videoTimeBase.den }
                    });
                }
            }

            // Initialize audio decoder
            if (_audioStreamIndex >= 0)
            {
                _logger.Log("FFmpegMediaPlayer", "InitializingAudioDecoder", new { StreamIndex = _audioStreamIndex });
                var stream = _formatContext->streams[_audioStreamIndex];
                var codecParams = stream->codecpar;
                _logger.Log("FFmpegMediaPlayer", "AudioStreamInfo", new 
                { 
                    StreamIndex = _audioStreamIndex,
                    CodecId = codecParams->codec_id.ToString(),
                    SampleRate = codecParams->sample_rate,
                    Channels = codecParams->ch_layout.nb_channels,
                    SampleFormat = codecParams->format.ToString(),
                    BitRate = codecParams->bit_rate,
                    TimeBase = new { Num = stream->time_base.num, Den = stream->time_base.den }
                });
                
                if (!InitializeAudioDecoder())
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] Unable to initialize audio decoder. Audio stream disabled.");
                    _logger.Log("FFmpegMediaPlayer", "AudioDecoderInitFailed", new { StreamIndex = _audioStreamIndex });
                    _audioStreamIndex = -1;
                }
                else
                {
                    _audioTimeBase = stream->time_base;
                    _logger.Log("FFmpegMediaPlayer", "AudioDecoderInitialized", new 
                    { 
                        StreamIndex = _audioStreamIndex,
                        SampleRate = _audioCodecContext->sample_rate,
                        Channels = _audioCodecContext->ch_layout.nb_channels,
                        TimeBase = new { Num = _audioTimeBase.num, Den = _audioTimeBase.den }
                    });
                }
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
                _logger.Log("FFmpegMediaPlayer", "PacketAllocationFailed", null);
                CloseInternal();
                return false;
            }

            _pendingFrameCount = 0;
            _droppedFrames = 0;
            _startTime = 0;
            _playbackStartWallTime = 0;
            _audioStartPts = 0;
            _needsResync = true;
            _audioClock = 0;
            _videoClock = 0;
            _totalPauseTime = 0;
            _pauseStartTime = 0;
            _seekTargetPts = -1;

            _logger.Log("FFmpegMediaPlayer", "MovieLoadingCompleted", new
            { 
                Path = path,
                Duration = _duration,
                VideoStreamIndex = _videoStreamIndex,
                AudioStreamIndex = _audioStreamIndex,
                VideoWidth = _videoWidth,
                VideoHeight = _videoHeight,
                FrameRate = _frameRate,
                Timestamp = DateTime.Now
            });

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
            
            // Create audio player using factory if available
            if (_audioPlayerFactory != null)
            {
                AudioPlayer = _audioPlayerFactory(sampleRate, 2); // Always output stereo
                if (AudioPlayer != null)
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] AudioPlayer created successfully");
                }
                else
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] AudioPlayer factory returned null - audio disabled");
                }
            }
            else
            {
                Debug.WriteLine("[FFmpegMediaPlayer] No audio player factory provided - audio disabled");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FFmpegMediaPlayer] AudioPlayer creation failed: {ex.Message}");
            AudioPlayer = null;
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
                _logger.Log("FFmpegMediaPlayer", "Resume", new { Position = _position });
                Playing?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_isPlaying) return;

            _isPlaying = true;
            _isPaused = false;
            _cancellationTokenSource = new CancellationTokenSource();

            // Ensure audio player state is synchronized
            SyncAudioPlayerState();

            _logger.Log("FFmpegMediaPlayer", "Play", new { Position = _position, Duration = _duration });

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
            _logger.Log("FFmpegMediaPlayer", "Pause", new { Position = _position });
            Paused?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Synchronizes the audio player state with the main player state.
    /// This ensures that different audio libraries stay in sync with playback commands.
    /// </summary>
    private void SyncAudioPlayerState()
    {
        if (_audioPlayer == null) return;

        try
        {
            // Stop any current audio playback to ensure clean state
            _audioPlayer.Stop();

            // Small delay to ensure stop completes
            Thread.Sleep(10);

            // Resume if we should be playing
            if (_isPlaying && !_isPaused)
            {
                _audioPlayer.Resume();
            }
        }
        catch (Exception ex)
        {
            _logger.Log("FFmpegMediaPlayer", "AudioPlayerSyncError", new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Validates and resets audio player state to prevent desynchronization.
    /// Call this when switching between different audio libraries.
    /// </summary>
    public void ResetAudioPlayerState()
    {
        lock (_lock)
        {
            if (_audioPlayer != null)
            {
                try
                {
                    _audioPlayer.Stop();
                    Thread.Sleep(20); // Give time for stop to complete
                    _audioPlayer.SetVolume(_volume / 100f);

                    // Re-sync state
                    if (_isPlaying && !_isPaused)
                    {
                        _audioPlayer.Resume();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log("FFmpegMediaPlayer", "AudioPlayerResetError", new { Error = ex.Message });
                }
            }
        }
    }

    /// <summary>
    /// Stops playback.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isPlaying && !_isPaused) return;

            _logger.Log("FFmpegMediaPlayer", "Stop", new { Position = _position });

            _cancellationTokenSource?.Cancel();
            _playbackThread?.Join(1000);
            _playbackThread = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _isPlaying = false;
            _isPaused = false;
            _position = 0;
            _startTime = 0;
            _playbackStartWallTime = 0;
            _audioStartPts = 0;
            _needsResync = true;
            _audioClock = 0;
            _videoClock = 0;
            _totalPauseTime = 0;
            _pauseStartTime = 0;
            _seekTargetPts = -1;

            // Seek to beginning
            if (_formatContext != null)
            {
                ffmpeg.av_seek_frame(_formatContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
            }

            // Ensure audio player is properly stopped and state is synchronized
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

            var oldPosition = _position;
            var targetSec = _duration * positionPercent;
            var targetTime = (long)(targetSec * ffmpeg.AV_TIME_BASE);

            // av_seek_frame with AVSEEK_FLAG_BACKWARD lands on the nearest keyframe
            // at or before targetTime. We then set _seekTargetPts so the decode loops
            // drop frames between the keyframe and targetSec — that's what makes seeks
            // land on the user's position instead of snapping to keyframe boundaries.
            ffmpeg.av_seek_frame(_formatContext, -1, targetTime, ffmpeg.AVSEEK_FLAG_BACKWARD);

            if (_videoCodecContext != null)
                ffmpeg.avcodec_flush_buffers(_videoCodecContext);
            if (_audioCodecContext != null)
                ffmpeg.avcodec_flush_buffers(_audioCodecContext);

            // Reset clocks for resynchronization after seek
            _startTime = 0;
            _playbackStartWallTime = 0;
            _audioStartPts = 0;
            _needsResync = true; // Flag to update audio start PTS on next frame
            _audioClock = 0;
            _videoClock = 0;
            _totalPauseTime = 0;
            _pauseStartTime = 0;
            _position = targetSec;
            _seekTargetPts = targetSec;

            // Stop and reset audio player to reset sample counter
            _audioPlayer?.Stop();

            _logger.Log("FFmpegMediaPlayer", "Seek", new
            {
                PositionPercent = positionPercent,
                OldPosition = oldPosition,
                NewPosition = _position,
                TargetTime = targetSec
            });
        }
    }


    /// <summary>
    /// Caches a frame for backward stepping.
    /// </summary>
    private void CacheFrame(byte[] frameData, int width, int height, int stride, int dataLength, double pts)
    {
        lock (_lock)
        {
            // Create a copy of the frame data for caching
            var cachedData = new byte[dataLength];
            Array.Copy(frameData, cachedData, dataLength);

            _frameCache.Enqueue(new CachedFrame(cachedData, width, height, stride, dataLength, pts));

            // Limit cache size
            while (_frameCache.Count > MaxCachedFrames)
            {
                _frameCache.Dequeue();
            }
        }
    }

    /// <summary>
    /// Steps forward exactly one frame. Pauses playback after displaying the frame.
    /// </summary>
    /// <returns>True if a frame was successfully decoded and displayed, false otherwise.</returns>
    /// <summary>
    /// Decodes and displays one frame at the current position without affecting playback state.
    /// Use after Seek() while paused to show the frame at the new position.
    /// </summary>
    public bool ShowFrameAtCurrentPosition()
    {
        lock (_lock)
        {
            if (_formatContext == null || _videoCodecContext == null || _packet == null)
                return false;
            // Allow when paused (_isPlaying=true, _isPaused=true) or stopped (_isPlaying=false)
            if (_isPlaying && !_isPaused)
                return false;

            _logger.Log("FFmpegMediaPlayer", "ShowFrameAtCurrentPosition", new { Position = _position, IsPaused = _isPaused, IsPlaying = _isPlaying });
            return DecodeSingleFrame();
        }
    }

    public bool StepForward()
    {
        lock (_lock)
        {
            if (_formatContext == null || _videoCodecContext == null || _packet == null)
                return false;

            // Enter step mode - next frame will pause automatically
            _stepMode = true;
            
            // If paused and playback thread is running, temporarily unpause to decode one frame
            if (_isPaused && _isPlaying)
            {
                _isPaused = false;
                // The playback loop will process one frame and pause again due to _stepMode
                return true;
            }
            
            // If playback thread is not running, decode one frame manually
            if (!_isPlaying)
            {
                return DecodeSingleFrame();
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// Decodes and displays the first video frame without starting playback.
    /// Call after Open() to show a thumbnail/preview. Skips non-video packets
    /// (e.g. audio) until a video frame is found, then seeks back to the start.
    /// </summary>
    /// <returns>True if a frame was successfully decoded and displayed.</returns>
    public bool DecodeFirstFrame()
    {
        lock (_lock)
        {
            if (_formatContext == null || _videoCodecContext == null || _packet == null)
                return false;

            if (_isPlaying)
                return false;

            _logger.Log("FFmpegMediaPlayer", "DecodeFirstFrame", null);

            // Read packets until we find a video frame
            const int maxAttempts = 200; // Safety limit
            for (int i = 0; i < maxAttempts; i++)
            {
                var readResult = ffmpeg.av_read_frame(_formatContext, _packet);
                if (readResult < 0)
                    break;

                if (_packet->stream_index == _videoStreamIndex)
                {
                    var sendResult = ffmpeg.avcodec_send_packet(_videoCodecContext, _packet);
                    if (sendResult >= 0)
                    {
                        if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame) >= 0)
                        {
                            ProcessSingleFrame();
                            ffmpeg.av_packet_unref(_packet);

                            // Seek back to start so Play() begins from the beginning
                            ffmpeg.av_seek_frame(_formatContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
                            ffmpeg.avcodec_flush_buffers(_videoCodecContext);
                            if (_audioCodecContext != null)
                                ffmpeg.avcodec_flush_buffers(_audioCodecContext);

                            // Reset position state
                            _position = 0;
                            _videoClock = 0;
                            _audioClock = 0;
                            _lastFramePts = -1;
                            _needsResync = true;
                            _seekTargetPts = -1;

                            _logger.Log("FFmpegMediaPlayer", "FirstFrameDecoded", new { Width = _videoWidth, Height = _videoHeight });
                            return true;
                        }
                    }
                }

                ffmpeg.av_packet_unref(_packet);
            }

            _logger.Log("FFmpegMediaPlayer", "FirstFrameDecodeFailed", null);
            return false;
        }
    }

    /// <summary>
    /// Decodes a single frame manually (used for step mode when playback thread is not running).
    /// </summary>
    private bool DecodeSingleFrame()
    {
        if (_formatContext == null || _videoCodecContext == null || _packet == null)
            return false;

        // Read packets until we find a video frame (skip audio packets)
        const int maxAttempts = 200;
        for (int i = 0; i < maxAttempts; i++)
        {
            var readResult = ffmpeg.av_read_frame(_formatContext, _packet);
            if (readResult < 0)
                return false;

            if (_packet->stream_index == _videoStreamIndex)
            {
                var sendResult = ffmpeg.avcodec_send_packet(_videoCodecContext, _packet);
                if (sendResult >= 0)
                {
                    if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame) >= 0)
                    {
                        // After a paused seek, the first decodable frame is the keyframe
                        // at-or-before the target. Keep draining until we reach the frame
                        // the user actually asked for so the preview matches the slider.
                        if (_seekTargetPts >= 0)
                        {
                            var pts = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : _frame->best_effort_timestamp;
                            if (pts != ffmpeg.AV_NOPTS_VALUE)
                            {
                                var frameTime = pts * _videoTimeBase.num / (double)_videoTimeBase.den;
                                if (frameTime + SeekTargetEpsilon < _seekTargetPts)
                                {
                                    ffmpeg.av_packet_unref(_packet);
                                    continue;
                                }
                            }
                            _seekTargetPts = -1;
                        }

                        ProcessSingleFrame();
                        ffmpeg.av_packet_unref(_packet);
                        return true;
                    }
                }
            }

            ffmpeg.av_packet_unref(_packet);
        }

        return false;
    }
    
    /// <summary>
    /// Processes a single decoded frame (converts to RGB and raises FrameReady event).
    /// </summary>
    private void ProcessSingleFrame()
    {
        if (_frame == null || _rgbFrame == null || _swsContext == null)
            return;

        // Get presentation timestamp
        var pts = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : _frame->best_effort_timestamp;
        if (pts == ffmpeg.AV_NOPTS_VALUE)
            return;

        // Convert PTS to seconds
        var frameTime = pts * _videoTimeBase.num / (double)_videoTimeBase.den;
        
        _videoClock = frameTime;
        _position = frameTime;
        _lastFramePts = frameTime;

        // Convert to BGRA
        ffmpeg.sws_scale(_swsContext,
            _frame->data, _frame->linesize, 0, _videoHeight,
            _rgbFrame->data, _rgbFrame->linesize);

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
            Debug.WriteLine($"[FFmpegMediaPlayer] Unable to rent frame buffer: {ex.Message}");
            return;
        }

        try
        {
            Marshal.Copy((IntPtr)_rgbFrame->data[0], frameBuffer, 0, bufferSize);
        }
        catch (Exception ex)
        {
            ArrayPool<byte>.Shared.Return(frameBuffer);
            Debug.WriteLine($"[FFmpegMediaPlayer] Failed to copy frame data: {ex.Message}");
            return;
        }

        // Cache frame for backward stepping
        CacheFrame(frameBuffer, width, height, stride, bufferSize, frameTime);

        var eventArgs = new FrameEventArgs(
            frameBuffer,
            width,
            height,
            stride,
            bufferSize,
            pooled: true,
            releaseAction: buffer => ArrayPool<byte>.Shared.Return(buffer));

        if (_synchronizationCallback != null)
        {
            _synchronizationCallback(() =>
            {
                FrameReady?.Invoke(this, eventArgs);
                PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
            });
        }
        else
        {
            FrameReady?.Invoke(this, eventArgs);
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
        }
    }

    /// <summary>
    /// Steps backward one frame. Uses cached frames when available, otherwise seeks to previous keyframe and decodes forward.
    /// </summary>
    /// <returns>True if a frame was successfully displayed, false otherwise.</returns>
    public bool StepBackward()
    {
        lock (_lock)
        {
            if (_formatContext == null || _videoCodecContext == null)
                return false;

            // Check if we have a cached frame
            CachedFrame? previousFrame = null;
            if (_frameCache.Count > 1)
            {
                // Remove current frame from cache
                _frameCache.Dequeue();
                // Get previous frame
                if (_frameCache.Count > 0)
                {
                    previousFrame = _frameCache.Peek();
                }
            }
            
            if (previousFrame != null)
            {
                // Use cached frame
                // Use cached frame (create a copy since cached data is not pooled)
                var frameDataCopy = new byte[previousFrame.DataLength];
                Array.Copy(previousFrame.Data, frameDataCopy, previousFrame.DataLength);
                
                var eventArgs = new FrameEventArgs(
                    frameDataCopy,
                    previousFrame.Width,
                    previousFrame.Height,
                    previousFrame.Stride,
                    previousFrame.DataLength,
                    pooled: false,
                    releaseAction: null);
                
                _position = previousFrame.Pts;
                _videoClock = previousFrame.Pts;
                _lastFramePts = previousFrame.Pts;
                
                if (_synchronizationCallback != null)
                    _synchronizationCallback(() => FrameReady?.Invoke(this, eventArgs));
                else
                    FrameReady?.Invoke(this, eventArgs);
                
                if (_synchronizationCallback != null)
                    _synchronizationCallback(() => PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position)));
                else
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
                
                // Clear cache after this frame (we've used it)
                _frameCache.Clear();
                
                return true;
            }
            else
            {
                // No cached frame - need to seek backward to previous keyframe
                // Calculate target time (one frame back)
                var targetTime = (long)((_lastFramePts - (1.0 / _frameRate)) * ffmpeg.AV_TIME_BASE);
                if (targetTime < 0) targetTime = 0;
                
                // Seek to keyframe before target (AVSEEK_FLAG_BACKWARD ensures we seek to keyframe before target)
                var seekResult = ffmpeg.av_seek_frame(_formatContext, _videoStreamIndex, targetTime, ffmpeg.AVSEEK_FLAG_BACKWARD);
                
                if (seekResult < 0)
                {
                    Debug.WriteLine("[FFmpegMediaPlayer] StepBackward: Seek failed");
                    return false;
                }
                
                // Flush codec buffers to clear any buffered frames
                if (_videoCodecContext != null)
                    ffmpeg.avcodec_flush_buffers(_videoCodecContext);
                if (_audioCodecContext != null)
                    ffmpeg.avcodec_flush_buffers(_audioCodecContext);
                
                // Clear frame cache since we're seeking
                _frameCache.Clear();
                
                // Reset timing
                _startTime = 0;
                _playbackStartWallTime = 0;
                _audioStartPts = 0;
                _needsResync = true;
                _audioClock = 0;
                _videoClock = 0;
                // StepBackward manages its own target via the decode-forward loop below,
                // so make sure a pending Seek target can't interfere.
                _seekTargetPts = -1;

                // Stop audio
                _audioPlayer?.Stop();

                // Decode forward from keyframe until we reach the frame before current position
                // This handles the keyframe issue - we decode from the keyframe forward
                var targetPts = targetTime / (double)ffmpeg.AV_TIME_BASE;
                var decodedFrame = false;
                
                // Decode frames until we reach or pass the target
                for (int i = 0; i < 100; i++) // Limit to 100 frames to prevent infinite loop
                {
                    if (DecodeSingleFrame())
                    {
                        decodedFrame = true;
                        // If we've reached or passed the target, we're done
                        if (_lastFramePts >= targetPts - (0.5 / _frameRate))
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (!decodedFrame)
                {
                    // If we couldn't decode, just seek and show the keyframe
                    _position = targetPts;
                    _videoClock = targetPts;
                    _lastFramePts = targetPts;
                }
                
                // Enter step mode
                _stepMode = true;
                _isPaused = true;
                
                return decodedFrame;
            }
        }
    }

    private void PlaybackLoop(object? state)
    {
        var token = (CancellationToken)state!;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        double firstFramePts = 0; // Will be set when first frame is processed
        double lastVideoPts = 0; // Last displayed video frame PTS
        double lastAudioPts = 0; // Last processed audio frame PTS
        _totalPauseTime = 0;
        _pauseStartTime = 0;
        int audioPacketsProcessed = 0;
        int videoPacketsProcessed = 0;
        
        _logger.Log("FFmpegMediaPlayer", "PlaybackLoopStarted", null);

        bool endOfFile = false;

        while (!token.IsCancellationRequested && !endOfFile)
        {
            if (_isPaused)
            {
                if (_pauseStartTime == 0)
                {
                    _pauseStartTime = stopwatch.Elapsed.TotalSeconds;
                }
                Thread.Sleep(10);
                continue;
            }
            else
            {
                // Resume from pause - accumulate pause time
                if (_pauseStartTime > 0)
                {
                    _totalPauseTime += stopwatch.Elapsed.TotalSeconds - _pauseStartTime;
                    _pauseStartTime = 0;
                }
            }

            // Process packets
            int packetsThisIteration = 0;
            const int maxPacketsPerIteration = 15;
            
            while (packetsThisIteration < maxPacketsPerIteration && !token.IsCancellationRequested && !endOfFile)
            {
                int readResult;
                int streamIndex;
                
                lock (_lock)
                {
                    if (_formatContext == null || _packet == null) break;
                    readResult = ffmpeg.av_read_frame(_formatContext, _packet);
                    streamIndex = _packet->stream_index;
                }

                if (readResult < 0)
                {
                    endOfFile = true;
                    _logger.Log("FFmpegMediaPlayer", "EndOfFile", new { ReadResult = readResult });
                    break;
                }

                try
                {
                    if (streamIndex == _audioStreamIndex)
                    {
                        lock (_lock)
                        {
                            if (_audioCodecContext != null)
                            {
                                ProcessAudioPacket(stopwatch, firstFramePts, ref lastAudioPts);
                                audioPacketsProcessed++;
                            }
                        }
                    }
                    else if (streamIndex == _videoStreamIndex)
                    {
                        // ProcessVideoPacket manages its own locking:
                        // holds lock for decode/convert, releases for UI callbacks
                        if (_videoCodecContext != null)
                        {
                            ProcessVideoPacket(stopwatch, ref firstFramePts, ref lastVideoPts);
                            videoPacketsProcessed++;
                        }
                    }
                }
                finally
                {
                    lock (_lock)
                    {
                        if (_packet != null)
                            ffmpeg.av_packet_unref(_packet);
                    }
                }
                
                packetsThisIteration++;
            }
            
            // Minimal sleep only if we processed packets, to prevent CPU spinning
            if (packetsThisIteration > 0 && !endOfFile)
            {
                UpdateAudioOnlyPositionFromPlaybackClock(stopwatch);
                Thread.Sleep(1);
            }
        }

        // Reset state inside lock
        WaitForAudioOnlyPlaybackToFinish(token, stopwatch);

        // Reset state inside lock
        lock (_lock)
        {
            _logger.Log("FFmpegMediaPlayer", "PlaybackLoopEnded", new
            {
                AudioPacketsProcessed = audioPacketsProcessed,
                VideoPacketsProcessed = videoPacketsProcessed
            });
            _isPlaying = false;
            _isPaused = false;

            // Seek back to start so Play() can restart from the beginning
            if (_formatContext != null)
            {
                ffmpeg.av_seek_frame(_formatContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
                if (_videoCodecContext != null)
                    ffmpeg.avcodec_flush_buffers(_videoCodecContext);
                if (_audioCodecContext != null)
                    ffmpeg.avcodec_flush_buffers(_audioCodecContext);
            }

            _position = 0;
            _needsResync = true;
            _lastFramePts = -1;
            _seekTargetPts = -1;

            _audioPlayer?.Stop();
        }

        // Fire EndReached OUTSIDE lock to prevent deadlock with UI thread
        if (_synchronizationCallback != null)
            _synchronizationCallback(() => EndReached?.Invoke(this, EventArgs.Empty));
        else
            EndReached?.Invoke(this, EventArgs.Empty);
    }

    private void WaitForAudioOnlyPlaybackToFinish(CancellationToken token, Stopwatch stopwatch)
    {
        if (_videoStreamIndex >= 0 || _audioPlayer == null || _duration <= 0)
            return;

        _logger.Log("FFmpegMediaPlayer", "AudioOnlyDrainStarted", new
        {
            Duration = _duration,
            AudioStartPts = _audioStartPts
        });

        while (!token.IsCancellationRequested)
        {
            if (_isPaused)
            {
                if (_pauseStartTime == 0)
                    _pauseStartTime = stopwatch.Elapsed.TotalSeconds;

                Thread.Sleep(10);
                continue;
            }

            if (_pauseStartTime > 0)
            {
                _totalPauseTime += stopwatch.Elapsed.TotalSeconds - _pauseStartTime;
                _pauseStartTime = 0;
            }

            var playedPosition = UpdateAudioOnlyPositionFromPlaybackClock(stopwatch);

            if (playedPosition >= _duration - 0.05)
                break;

            Thread.Sleep(50);
        }

        _logger.Log("FFmpegMediaPlayer", "AudioOnlyDrainCompleted", new
        {
            Position = _position,
            ElapsedWallTime = stopwatch.Elapsed.TotalSeconds - _playbackStartWallTime - _totalPauseTime
        });
    }

    private double UpdateAudioOnlyPositionFromPlaybackClock(Stopwatch stopwatch)
    {
        if (_videoStreamIndex >= 0 || _duration <= 0 || _playbackStartWallTime <= 0)
            return _position;

        var elapsedWall = stopwatch.Elapsed.TotalSeconds - _playbackStartWallTime - _totalPauseTime;
        var playedPosition = _startTime + Math.Max(0, elapsedWall);
        _position = Math.Clamp(playedPosition, 0, _duration);
        var positionSnapshot = Position;

        if (_synchronizationCallback != null)
            _synchronizationCallback(() => PositionChanged?.Invoke(this, new PositionChangedEventArgs(positionSnapshot)));
        else
            PositionChanged?.Invoke(this, new PositionChangedEventArgs(positionSnapshot));

        return playedPosition;
    }

    /// <summary>
    /// Processes a video packet: decodes frame inside lock, posts callbacks outside lock.
    /// This prevents deadlock where the UI thread waits on _lock while the playback thread
    /// waits for the dispatcher queue (which the UI thread must drain).
    /// </summary>
    private void ProcessVideoPacket(Stopwatch stopwatch, ref double firstFramePts, ref double lastVideoPts)
    {
        lock (_lock)
        {
            var sendResult = ffmpeg.avcodec_send_packet(_videoCodecContext, _packet);
            if (sendResult < 0)
                return;
        }

        while (true)
        {
            // === PHASE 1: Decode and convert inside lock ===
            double frameTime;
            float positionSnapshot;
            FrameEventArgs? eventArgs = null;
            bool shouldSleep = false;
            int sleepMs = 0;

            lock (_lock)
            {
                if (ffmpeg.avcodec_receive_frame(_videoCodecContext, _frame) < 0)
                    break;

                var pts = _frame->pts != ffmpeg.AV_NOPTS_VALUE ? _frame->pts : _frame->best_effort_timestamp;
                if (pts == ffmpeg.AV_NOPTS_VALUE)
                    continue;

                frameTime = pts * _videoTimeBase.num / (double)_videoTimeBase.den;

                // Frame-accurate seek: av_seek_frame landed on the keyframe at-or-before
                // the requested position. Decode-drain everything between that keyframe and
                // the user's target without posting frames or touching the timing anchors.
                // Leave _needsResync alone so the first *displayed* frame is the timing origin.
                if (_seekTargetPts >= 0)
                {
                    if (frameTime + SeekTargetEpsilon < _seekTargetPts)
                        continue;
                    _seekTargetPts = -1;
                }

                // After seek, reset timing so next frame re-initializes
                if (_needsResync)
                {
                    _needsResync = false;
                    firstFramePts = 0;
                    _logger.Log("FFmpegMediaPlayer", "Resync", new { FrameTime = frameTime });
                }

                // Frame timing
                if (firstFramePts == 0)
                {
                    firstFramePts = frameTime;
                    lastVideoPts = frameTime;
                    _startTime = frameTime;
                    _playbackStartWallTime = stopwatch.Elapsed.TotalSeconds;
                    _videoClock = frameTime;
                    _audioClock = frameTime;
                    _logger.Log("FFmpegMediaPlayer", "FirstVideoFrame", new { FrameTime = frameTime, PTS = pts });
                }
                else
                {
                    var frameDelay = frameTime - lastVideoPts;

                    if (Math.Abs(frameDelay) > 1.0)
                    {
                        firstFramePts = frameTime;
                        lastVideoPts = frameTime;
                        _startTime = frameTime;
                        _playbackStartWallTime = stopwatch.Elapsed.TotalSeconds;
                    }
                    else if (frameDelay > 0.002)
                    {
                        var elapsedWall = stopwatch.Elapsed.TotalSeconds - _playbackStartWallTime - _totalPauseTime;
                        var mediaElapsed = frameTime - _startTime;
                        sleepMs = (int)((mediaElapsed - elapsedWall) * 1000);
                        shouldSleep = sleepMs > 1 && sleepMs < 1000;
                    }
                    lastVideoPts = frameTime;
                }

                // Convert to BGRA
                ffmpeg.sws_scale(_swsContext,
                    _frame->data, _frame->linesize, 0, _videoHeight,
                    _rgbFrame->data, _rgbFrame->linesize);

                _videoClock = frameTime;
                _position = frameTime;
                _lastFramePts = frameTime;

                if (_stepMode)
                {
                    _isPaused = true;
                    _audioPlayer?.Pause();
                }

                // Check pending frames
                var pendingFrames = Interlocked.Increment(ref _pendingFrameCount);
                if (pendingFrames > MaxPendingFrames)
                {
                    Interlocked.Decrement(ref _pendingFrameCount);
                    Interlocked.Increment(ref _droppedFrames);
                    continue;
                }

                // Copy frame buffer while holding lock (accessing native _rgbFrame)
                var stride = _rgbFrame->linesize[0];
                var width = _videoWidth;
                var height = _videoHeight;
                var bufferSize = _rgbBufferSize;
                positionSnapshot = Position;

                byte[]? frameBuffer = null;
                try
                {
                    frameBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                    Marshal.Copy((IntPtr)_rgbFrame->data[0], frameBuffer, 0, bufferSize);
                }
                catch
                {
                    if (frameBuffer != null) ArrayPool<byte>.Shared.Return(frameBuffer);
                    Interlocked.Decrement(ref _pendingFrameCount);
                    continue;
                }

                CacheFrame(frameBuffer, width, height, stride, bufferSize, frameTime);

                eventArgs = new FrameEventArgs(
                    frameBuffer, width, height, stride, bufferSize,
                    pooled: true,
                    releaseAction: buffer => ArrayPool<byte>.Shared.Return(buffer));

            } // === lock released here ===

            // Sleep OUTSIDE lock for frame timing
            if (shouldSleep)
            {
                Thread.Sleep(sleepMs);
            }

            // === PHASE 2: Post callbacks OUTSIDE lock ===
            if (eventArgs != null)
            {
                if (_synchronizationCallback != null)
                {
                    _synchronizationCallback(() => PositionChanged?.Invoke(this, new PositionChangedEventArgs(positionSnapshot)));
                    _synchronizationCallback(() =>
                    {
                        try { FrameReady?.Invoke(this, eventArgs); }
                        finally { Interlocked.Decrement(ref _pendingFrameCount); }
                    });
                }
                else
                {
                    PositionChanged?.Invoke(this, new PositionChangedEventArgs(positionSnapshot));
                    try { FrameReady?.Invoke(this, eventArgs); }
                    finally { Interlocked.Decrement(ref _pendingFrameCount); }
                }
            }
        }
    }


    private void ProcessAudioPacket(Stopwatch stopwatch, double firstFramePts, ref double lastAudioPts)
    {
        if (_audioPlayer == null)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] ProcessAudioPacket: _audioPlayer is null!");
            _logger.Log("FFmpegMediaPlayer", "ProcessAudioPacketFailed", new { Reason = "AudioPlayer is null" });
            return;
        }
        if (ffmpeg.avcodec_send_packet(_audioCodecContext, _packet) < 0)
        {
            Debug.WriteLine("[FFmpegMediaPlayer] ProcessAudioPacket: avcodec_send_packet failed");
            _logger.Log("FFmpegMediaPlayer", "ProcessAudioPacketFailed", new { Reason = "avcodec_send_packet failed" });
            return;
        }

        var tempFrame = ffmpeg.av_frame_alloc();
        // Pre-allocate the output buffer pointer array outside the loop to avoid stack overflow (CA2014)
        var outBuffer = stackalloc byte*[1];
        try
        {
            while (ffmpeg.avcodec_receive_frame(_audioCodecContext, tempFrame) >= 0)
            {
                var samples = tempFrame->nb_samples;
                var channels = _audioCodecContext->ch_layout.nb_channels;
                var sampleRate = _audioCodecContext->sample_rate;

                // Get audio PTS and convert to seconds
                var pts = tempFrame->pts != ffmpeg.AV_NOPTS_VALUE ? tempFrame->pts : tempFrame->best_effort_timestamp;
                if (pts != ffmpeg.AV_NOPTS_VALUE)
                {
                    var audioTime = pts * _audioTimeBase.num / (double)_audioTimeBase.den;

                    // Frame-accurate seek: drop audio that belongs to the pre-target region
                    // so the audio stream starts exactly where the user seeked to instead of
                    // at the preceding keyframe. Video has its own matching guard.
                    if (_seekTargetPts >= 0 && audioTime + SeekTargetEpsilon < _seekTargetPts)
                        continue;

                    // Track audio clock
                    _audioClock = audioTime;

                    // Update audio start PTS on first frame or after seek
                    if (lastAudioPts == 0 || _needsResync)
                    {
                        lastAudioPts = audioTime;
                        _audioStartPts = audioTime; // Track when audio stream starts for sync
                        if (_videoStreamIndex < 0)
                        {
                            _startTime = audioTime;
                            _playbackStartWallTime = stopwatch.Elapsed.TotalSeconds;
                            _totalPauseTime = 0;
                            _pauseStartTime = 0;
                        }
                        _needsResync = false;
                        _logger.Log("FFmpegMediaPlayer", "FirstAudioFrame", new
                        {
                            AudioTime = audioTime,
                            PTS = pts,
                            Samples = samples,
                            SampleRate = sampleRate,
                            Channels = channels
                        });
                    }

                    // Audio is the master clock - let it play continuously without sync adjustments.
                    // Video timing is controlled separately in ProcessVideoPacket to stay in sync.
                }
                else
                {
                    _logger.Log("FFmpegMediaPlayer", "AudioFrameNoPTS", new { Samples = samples });
                }
                
                // Use SwrContext for proper resampling
                if (_swrContext != null)
                {
                    // Calculate output samples
                    var outSamples = (int)ffmpeg.swr_get_delay(_swrContext, sampleRate) + samples;
                    
                    // Allocate output buffer (stereo S16 = 2 channels * 2 bytes per sample)
                    var outBufferSize = outSamples * 2; // stereo sample count
                    var outData = new short[outBufferSize];
                    
                    fixed (short* outPtr = outData)
                    {
                        outBuffer[0] = (byte*)outPtr;
                        
                        // Resample
                        var convertedSamples = ffmpeg.swr_convert(
                            _swrContext,
                            outBuffer, outSamples,
                            tempFrame->extended_data, samples);
                        
                        if (convertedSamples > 0)
                        {
                            // Queue the S16 samples directly
                            // convertedSamples is per-channel, so for stereo we have convertedSamples * 2 total samples
                            // QueueSamplesS16 expects total sample count (stereo interleaved: L, R, L, R, ...)
                            _audioPlayer.QueueSamplesS16(outPtr, convertedSamples * 2);
                            _logger.Log("FFmpegMediaPlayer", "AudioQueued", new 
                            { 
                                InputSamples = samples,
                                ConvertedSamples = convertedSamples,
                                OutputSamples = convertedSamples * 2,
                                AudioTime = pts != ffmpeg.AV_NOPTS_VALUE ? pts * _audioTimeBase.num / (double)_audioTimeBase.den : 0,
                                Format = "S16"
                            });
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
                    _logger.Log("FFmpegMediaPlayer", "AudioQueued", new 
                    { 
                        Samples = samples,
                        Channels = channels,
                        AudioTime = pts != ffmpeg.AV_NOPTS_VALUE ? pts * _audioTimeBase.num / (double)_audioTimeBase.den : 0,
                        Format = format2.ToString()
                    });
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

        AudioPlayer = null;

        _videoStreamIndex = -1;
        _audioStreamIndex = -1;
        _position = 0;
        _duration = 0;
        _pendingFrameCount = 0;
        _droppedFrames = 0;
        _startTime = 0;
        _playbackStartWallTime = 0;
        _audioStartPts = 0;
        _needsResync = true;
        _audioClock = 0;
        _videoClock = 0;
        _totalPauseTime = 0;
        _pauseStartTime = 0;
        _seekTargetPts = -1;
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

        _logger.Log("FFmpegMediaPlayer", "Dispose", null);
        Close();
        _logger.Dispose();
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
