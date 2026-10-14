using System.Diagnostics;

namespace Fluxio.Transfer.Transfers;

/// <summary>
/// Measures real throughput over a sliding window (default 3 s) from the bytes that were
/// actually read from or written to the network. Thread-safe.
/// </summary>
public sealed class SpeedMeter
{
    private readonly object _gate = new();
    private readonly Queue<(long Timestamp, long Bytes)> _samples = new();
    private readonly long _window;
    private readonly Func<long> _clock;
    private long _windowBytes;
    private long _firstTimestamp = -1;

    public SpeedMeter(TimeSpan? window = null, Func<long>? clock = null)
    {
        _window = (long)((window ?? TimeSpan.FromSeconds(3)).TotalSeconds * Stopwatch.Frequency);
        _clock = clock ?? Stopwatch.GetTimestamp;
    }

    public void Add(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        var now = _clock();
        lock (_gate)
        {
            if (_firstTimestamp < 0)
            {
                _firstTimestamp = now;
            }

            _samples.Enqueue((now, bytes));
            _windowBytes += bytes;
            Trim(now);
        }
    }

    /// <summary>Average bytes per second over the window; 0 when nothing moved recently.</summary>
    public double BytesPerSecond
    {
        get
        {
            var now = _clock();
            lock (_gate)
            {
                Trim(now);
                if (_samples.Count == 0 || _firstTimestamp < 0)
                {
                    return 0;
                }

                // Divide by the covered span (at least 0.5 s) so the first samples do not spike.
                var covered = Math.Min(_window, now - _firstTimestamp);
                var seconds = Math.Max(covered, Stopwatch.Frequency / 2) / (double)Stopwatch.Frequency;
                return _windowBytes / seconds;
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _samples.Clear();
            _windowBytes = 0;
            _firstTimestamp = -1;
        }
    }

    private void Trim(long now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Timestamp > _window)
        {
            _windowBytes -= _samples.Dequeue().Bytes;
        }

        if (_samples.Count == 0)
        {
            _firstTimestamp = -1;
        }
    }

    /// <summary>Estimated time to transfer <paramref name="remainingBytes"/>, or null when unknown.</summary>
    public static TimeSpan? EstimateRemaining(long remainingBytes, double bytesPerSecond)
    {
        if (remainingBytes <= 0)
        {
            return TimeSpan.Zero;
        }

        if (bytesPerSecond < 1)
        {
            return null;
        }

        var seconds = remainingBytes / bytesPerSecond;
        return seconds > TimeSpan.FromDays(7).TotalSeconds ? null : TimeSpan.FromSeconds(seconds);
    }
}
