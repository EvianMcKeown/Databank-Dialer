public class AudioLimits
{
    public int MaxSessionSeconds { get; set; } = 120;
    public int MaxConcurrentStreams { get; set; } = 50;
    public int MaxStreamsPerIp { get; set; } = 4;
    public int MaxChunkSamples { get; set; } = 2048;
    public double MaxRealtimeFactor { get; set; } = 2.0;
    public int MaxInvocationsPerSecond { get; set; } = 150;
}

public sealed class StreamAdmission
{
    private readonly object _lock = new();
    private readonly Dictionary<string, int> _perIp = new();
    private int _active;

    public int Active
    {
        get { lock (_lock) { return _active; } }
    }

    public bool TryAdmit(string ip, int maxTotal, int maxPerIp)
    {
        lock (_lock)
        {
            if (_active >= maxTotal) return false;
            _perIp.TryGetValue(ip, out var count);
            if (count >= maxPerIp) return false;
            _perIp[ip] = count + 1;
            _active++;
            return true;
        }
    }

    public void Release(string ip)
    {
        lock (_lock)
        {
            if (!_perIp.TryGetValue(ip, out var count)) return;
            if (count <= 1) _perIp.Remove(ip);
            else _perIp[ip] = count - 1;
            _active--;
        }
    }
}

public sealed class SessionBudget
{
    private const int SampleRate = 8000;
    private const double BurstSeconds = 2;

    private readonly AudioLimits _limits;
    private readonly DateTime _startUtc;
    private long _samples;
    private long _calls;

    public SessionBudget(AudioLimits limits, DateTime startUtc)
    {
        _limits = limits;
        _startUtc = startUtc;
    }

    public bool Allow(int chunkLength, DateTime nowUtc)
    {
        if (chunkLength <= 0 || chunkLength > _limits.MaxChunkSamples) return false;

        double elapsed = (nowUtc - _startUtc).TotalSeconds;
        if (elapsed > _limits.MaxSessionSeconds) return false;

        _samples += chunkLength;
        _calls++;

        double window = elapsed + BurstSeconds;
        if (_samples > window * SampleRate * _limits.MaxRealtimeFactor) return false;
        if (_calls > window * _limits.MaxInvocationsPerSecond) return false;

        return true;
    }
}
