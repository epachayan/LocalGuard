namespace LocalGuard.Utils;

public sealed class BurstDetector
{
    private readonly int _windowSeconds;
    private readonly int _threshold;
    private readonly Dictionary<string, Queue<DateTime>> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public BurstDetector(int windowSeconds, int threshold) { _windowSeconds = windowSeconds; _threshold = threshold; }

    public bool Record(string key)
    {
        var now = DateTime.UtcNow;
        if (!_buckets.TryGetValue(key, out var q)) { q = new Queue<DateTime>(); _buckets[key] = q; }
        q.Enqueue(now);
        while (q.Count > 0 && (now - q.Peek()).TotalSeconds > _windowSeconds) q.Dequeue();
        return q.Count >= _threshold;
    }
}
