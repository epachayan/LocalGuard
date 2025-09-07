using System.Text;

namespace LocalGuard.AI;

public sealed class RegistryFeatureExtractor
{
    static double ShannonEntropy(byte[] data, int len)
    {
        if (len <= 0) return 0.0; Span<int> c = stackalloc int[256];
        for (int i = 0; i < len; i++) c[data[i]]++;
        double H = 0.0; for (int i = 0; i < 256; i++) { if (c[i] == 0) continue; double p = (double)c[i] / len; H -= p * Math.Log(p, 2); }
        return H;
    }

    static (float isStr, float isBin, float isNum, double ent, double lenLog) DescribeValue(object? v)
    {
        if (v is null) return (0, 0, 0, 0, 0);
        if (v is string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            return (1, 0, 0, ShannonEntropy(b, b.Length), Math.Log10(b.Length + 1));
        }
        if (v is byte[] bb)
        {
            return (0, 1, 0, ShannonEntropy(bb, bb.Length), Math.Log10(bb.Length + 1));
        }
        if (v is int or long or uint or ulong or short or ushort or double or float or decimal)
        {
            return (0, 0, 1, 0, Math.Log10(8 + 1));
        }
        try
        {
            var s2 = v.ToString() ?? "";
            var b2 = Encoding.UTF8.GetBytes(s2);
            return (1, 0, 0, ShannonEntropy(b2, b2.Length), Math.Log10(b2.Length + 1));
        }
        catch { return (0, 0, 0, 0, 0); }
    }

    // 15 features:
    // 0..5 one-hot bucket [HKCU/Run, HKLM/Run, HKCU/RunOnce, HKLM/RunOnce, HKCR/*/open/command, Other]
    // 6 isString, 7 isBinary, 8 isNumber, 9 entropy, 10 lenLog
    // 11 created, 12 modified, 13 deleted, 14 burst
    public float[] Extract(string hive, string keyPath, string? valueName, object? valueObj, bool created, bool modified, bool deleted, bool burst)
    {
        var buckets = new float[6];
        string hp = (hive.ToUpperInvariant() + "\\" + keyPath).ToUpperInvariant();

        void Set(int idx) { if (idx >= 0 && idx < 6) buckets[idx] = 1f; }
        if (hp.StartsWith("HKCU\\") && hp.Contains("\\RUNONCE")) Set(2);
        else if (hp.StartsWith("HKLM\\") && hp.Contains("\\RUNONCE")) Set(3);
        else if (hp.StartsWith("HKCU\\") && hp.Contains("\\RUN")) Set(0);
        else if (hp.StartsWith("HKLM\\") && hp.Contains("\\RUN")) Set(1);
        else if (hp.StartsWith("HKCR\\") && hp.EndsWith("\\OPEN\\COMMAND")) Set(4);
        else Set(5);

        var (isStr, isBin, isNum, ent, lenLog) = DescribeValue(valueObj);

        return new float[]
        {
            buckets[0],buckets[1],buckets[2],buckets[3],buckets[4],buckets[5],
            isStr, isBin, isNum, (float)ent, (float)lenLog,
            created?1f:0f, modified?1f:0f, deleted?1f:0f, burst?1f:0f
        };
    }
}
