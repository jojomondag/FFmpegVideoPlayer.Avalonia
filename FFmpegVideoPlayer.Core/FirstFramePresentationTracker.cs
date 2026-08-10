namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Coordinates the one-shot first-frame notification across control and media generations.
/// Kept independent of Avalonia so lifecycle races can be covered by ordinary unit tests.
/// </summary>
internal sealed class FirstFramePresentationTracker
{
    private readonly object _gate = new();
    private long _openGeneration = -1;
    private long _mediaGeneration = -1;
    private bool _presented;

    public void Activate(long openGeneration, long mediaGeneration)
    {
        lock (_gate)
        {
            _openGeneration = openGeneration;
            _mediaGeneration = mediaGeneration;
            _presented = false;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _openGeneration = -1;
            _mediaGeneration = -1;
            _presented = false;
        }
    }

    public bool TryPresent(long openGeneration, long mediaGeneration)
    {
        lock (_gate)
        {
            if (_presented ||
                openGeneration != _openGeneration ||
                mediaGeneration != _mediaGeneration)
            {
                return false;
            }

            _presented = true;
            return true;
        }
    }
}
