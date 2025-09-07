using LocalGuard.Utils;

namespace LocalGuard.AI;

public sealed class FeatureExtractor
{
    private readonly int _sampleBytes;
    private readonly string[] _startupDirs;
    private readonly string[] _execExts;
    private readonly BurstDetector _burst;

    public FeatureExtractor(int sampleBytes, string[] startupDirs, BurstDetector burst, string[] execExts)
    {
        _sampleBytes = sampleBytes; _startupDirs = startupDirs; _burst = burst; _execExts = execExts;
    }

    static double ShannonEntropy(byte[] data, int len)
    {
        if (len <= 0) return 0.0; Span<int> c = stackalloc int[256];
        for (int i = 0; i < len; i++) c[data[i]]++;
        double H = 0.0; for (int i = 0; i < 256; i++) { if (c[i] == 0) continue; double p = (double)c[i] / len; H -= p * Math.Log(p, 2); }
        return H;
    }
    static double NameEntropy(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return 0.0;
        var b = System.Text.Encoding.UTF8.GetBytes(name);
        return ShannonEntropy(b, b.Length);
    }

    static uint Fnv1a(string s) { unchecked { uint h = 2166136261; foreach (var ch in s) { h ^= ch; h *= 16777619; } return h; } }

    public float[] Extract(string path, bool isCreated, bool isModified)
    {
        var ext = Path.GetExtension(path).Trim('.');
        int K = 8; var buckets = new float[K];
        if (ext.Length > 0) buckets[(int)(Fnv1a(ext) % (uint)K)] = 1f;

        double eName = NameEntropy(path);
        double eContent = 0.0; long size = 0;
        try
        {
            var fi = new FileInfo(path);
            size = fi.Exists ? fi.Length : 0;
            if (fi.Exists && fi.Length > 0 && fi.Length <= 5 * 1024 * 1024)
            {
                var buf = new byte[Math.Min(_sampleBytes, (int)fi.Length)];
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                int read = fs.Read(buf, 0, buf.Length);
                eContent = ShannonEntropy(buf, read);
            }
        }
        catch { }

        bool isExec = Array.Exists(_execExts, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
        bool inStartup = false;
        foreach (var s in _startupDirs) { if (!string.IsNullOrEmpty(s) && path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) { inStartup = true; break; } }
        bool burstHit = _burst.Record(path);
        double sizeLog = size > 0 ? Math.Log10(size + 1) : 0.0;

        var feats = new List<float>();
        feats.AddRange(buckets); // 8
        feats.Add((float)eName); // 9
        feats.Add((float)eContent); // 10
        feats.Add((float)sizeLog);  // 11
        feats.Add(isExec ? 1f : 0f);    // 12
        feats.Add(inStartup ? 1f : 0f); // 13
        feats.Add(burstHit ? 1f : 0f);  // 14
        return feats.ToArray();     // length = 14
    }
}
