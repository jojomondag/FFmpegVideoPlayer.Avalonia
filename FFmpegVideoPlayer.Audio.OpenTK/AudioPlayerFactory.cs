using FFmpegVideoPlayer.Core;
namespace FFmpegVideoPlayer.Audio.OpenTK;

/// <summary>
/// Factory for creating OpenTK-based audio players.
/// </summary>
public static class AudioPlayerFactory
{
    /// <summary>
    /// Checks if OpenAL is available on the system.
    /// </summary>
    /// <returns>True if OpenAL can be initialized, false otherwise.</returns>
    public static bool IsOpenALAvailable()
    {
        try
        {
            using var probe = new OpenTKAudioPlayer(8_000, 1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a new OpenTK-based audio player instance.
    /// </summary>
    /// <param name="sampleRate">Audio sample rate (e.g., 44100, 48000).</param>
    /// <param name="channels">Number of audio channels (will be downmixed to stereo if needed).</param>
    /// <returns>An IAudioPlayer instance, or null if audio initialization fails.</returns>
    /// <remarks>
    /// On Windows, OpenAL native libraries (openal32.dll) must be available.
    /// Install OpenAL Soft from https://www.openal-soft.org/ or include openal32.dll with your application.
    /// </remarks>
    public static IAudioPlayer? Create(int sampleRate, int channels)
    {
        try
        {
            return new OpenTKAudioPlayer(sampleRate, channels);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerFactory] Failed to create OpenTK audio player: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerFactory] On Windows, ensure OpenAL Soft is installed or openal32.dll is available.");
            System.Diagnostics.Debug.WriteLine($"[AudioPlayerFactory] Download from: https://www.openal-soft.org/downloads/");
            return null;
        }
    }
}

