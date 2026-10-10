using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Avalonia.FFmpegVideoPlayer;

/// <summary>Closes native media sessions before an application's dispatcher and process exit.</summary>
public static class MediaPlayerLifetime
{
    /// <summary>
    /// Closes each distinct player concurrently and waits for every close, even when another fails.
    /// Call while the Avalonia dispatcher is running. This does not dispose the controls;
    /// they may subsequently open another source. Inspect the returned results for failures.
    /// A null collection or collection element is rejected before closing any player.
    /// </summary>
    public static async Task<IReadOnlyList<VideoPlayerCloseResult>> CloseAllAsync(
        IEnumerable<VideoPlayerControl> players)
    {
        ArgumentNullException.ThrowIfNull(players);
        var distinctPlayers = players.Distinct().ToArray();
        if (distinctPlayers.Any(player => player is null))
            throw new ArgumentException("The player collection must not contain null elements.", nameof(players));
        return await Task.WhenAll(distinctPlayers.Select(ClosePlayerAsync));
    }

    private static async Task<VideoPlayerCloseResult> ClosePlayerAsync(VideoPlayerControl player)
    {
        try
        {
            await player.CloseAsync();
            return new VideoPlayerCloseResult(player, null);
        }
        catch (Exception exception)
        {
            return new VideoPlayerCloseResult(player, exception);
        }
    }
}

/// <summary>The outcome of closing one player; a failed close does not hide other outcomes.</summary>
public sealed record VideoPlayerCloseResult(VideoPlayerControl Player, Exception? Exception)
{
    /// <summary>Whether the media session was closed successfully.</summary>
    public bool Succeeded => Exception is null;
}
