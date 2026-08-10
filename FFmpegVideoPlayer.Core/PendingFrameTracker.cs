using System.Diagnostics;

namespace FFmpegVideoPlayer.Core;

/// <summary>
/// Counts frame callbacks that still own a decoded buffer. Leases are one-shot, so
/// callbacks from an older media/timeline generation cannot drive the count negative
/// after a reset and the global pending-frame limit remains meaningful.
/// </summary>
internal sealed class PendingFrameTracker
{
    private readonly int _maximumCount;
    private int _count;

    internal PendingFrameTracker(int maximumCount)
    {
        if (maximumCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));

        _maximumCount = maximumCount;
    }

    internal int Count => Volatile.Read(ref _count);

    internal bool TryAcquire(out PendingFrameLease? lease)
    {
        while (true)
        {
            var count = Volatile.Read(ref _count);
            if (count >= _maximumCount)
            {
                lease = null;
                return false;
            }

            if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
            {
                lease = new PendingFrameLease(this);
                return true;
            }
        }
    }

    internal void Release()
    {
        var remaining = Interlocked.Decrement(ref _count);
        Debug.Assert(remaining >= 0, "A pending frame lease was released more than once.");
        if (remaining < 0)
            Interlocked.Exchange(ref _count, 0);
    }
}

internal sealed class PendingFrameLease : IDisposable
{
    private PendingFrameTracker? _owner;

    internal PendingFrameLease(PendingFrameTracker owner) => _owner = owner;

    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
}
