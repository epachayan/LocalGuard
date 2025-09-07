using System.Text;
using System.Text.Json;

namespace LocalGuard.Heuristics;

public sealed class OnlineStats
{
    public long Count { get; private set; }
    public double Mean { get; private set; }
    public double M2 { get; private set; }
    public void Update(double x) { Count++; var d = x - Mean; Mean += d / Count; var d2 = x - Mean; M2 += d * d2; }
    public double Variance => Count > 1 ? M2 / (Count - 1) : 0.0;
    public double StdDev => Math.Sqrt(Math.Max(Variance, 1e-9));
    public double Z(double x) => (x - Mean) / StdDev;
}

public sealed class HeuristicState
{
    public Dictionary<string, long> ExtCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long ExtTotal { get; set; } = 0;
    public OnlineStats EntropyName { get; set; } = new();
    public OnlineStats EntropyContent { get; set; } = new();
}

public sealed class FileHeuristicCore
{
    private readonly string _statePath;
    private readonly HeuristicState _state;
    private readonly int _sampleBytes;
    private readonly string[] _execExts;
    private readonly string[] _startupDirs;
    private readonly Utils.BurstDetector _burst;

    public FileHeuristicCore(string statePath, string[] startupDirs, Utils.BurstDetector burst, string[] execExts, int sampleBytes = 32 * 1024)
    {
        _statePath = statePath; _startupDirs = startupDirs; _burst = burst;
        _sampleBytes = sampleBytes; _execExts = execExts;
        _state = Load();
    }

    HeuristicState Load()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                var s = JsonSerializer.Deserialize<HeuristicState>(json);
                if (s != null) return s;
            }
        }
        catch { }
        return new HeuristicState();
    }

    void Save()
    {
        try { File.WriteAllText(_statePath, JsonSerializer.Serialize(_state)); } catch { }
    }

    static double ShannonEntropy(byte[] data, int len)
    {
        if (len <= 0) return 0.0;
        Span<int> counts = stackalloc int[256];
        for (int i = 0; i < len; i++) counts[data[i]]++;
        double H = 0.0;
        for (int i = 0; i < 256; i++) { if (counts[i] == 0) continue; double p = (double)counts[i] / len; H -= p * Math.Log(p, 2); }
        return H;
    }

    static double NameEntropy(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return 0.0;
        var bytes = Encoding.UTF8.GetBytes(name);
        return ShannonEntropy(bytes, bytes.Length);
    }

    public (double score, string reason) Evaluate(string path, bool isCreated, bool isModified)
    {
        var ext = Path.GetExtension(path).Trim('.');
        _state.ExtCounts.TryGetValue(ext, out var count);
        long total = Math.Max(_state.ExtTotal, 1);
        double p = (count + 1.0) / (total + (_state.ExtCounts.Count + 1));
        double rarity = -Math.Log(p + 1e-12);

        double eName = NameEntropy(path);
        double eContent = 0.0;
        try
        {
            var fi = new FileInfo(path);
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
        foreach (var s in _startupDirs)
        {
            if (!string.IsNullOrEmpty(s) && path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) { inStartup = true; break; }
        }
        bool burstHit = _burst.Record(path);

        double zName = _state.EntropyName.Count >= 30 ? _state.EntropyName.Z(eName) : 0.0;
        double zCont = (eContent > 0 && _state.EntropyContent.Count >= 30) ? _state.EntropyContent.Z(eContent) : 0.0;

        double score = 0.0; var reasons = new List<string>();
        score += Math.Clamp(rarity * 8, 0, 40); if (rarity > 2.5) reasons.Add($"rare ext {ext}");
        score += Math.Clamp(Math.Abs(zName) * 10, 0, 25); if (Math.Abs(zName) > 2.0) reasons.Add("odd filename");
        if (eContent > 0) { score += Math.Clamp(Math.Max(0, zCont) * 8, 0, 20); if (zCont > 2.0) reasons.Add("high content entropy"); }
        if (isExec) { score += 15; reasons.Add("exec"); }
        if (inStartup) { score += 20; reasons.Add("startup"); }
        if (burstHit) { score += 10; reasons.Add("burst"); }
        if (isCreated) score += 5;
        if (isModified) score += 2;

        score = Math.Clamp(score, 0, 100);
        string reason = reasons.Count > 0 ? string.Join(",", reasons) : "ok";

        // update state
        _state.ExtCounts[ext] = count + 1; _state.ExtTotal++;
        _state.EntropyName.Update(eName);
        if (eContent > 0) _state.EntropyContent.Update(eContent);
        Save();

        return (score, reason);
    }
}
