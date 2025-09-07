namespace LocalGuard
{
    using System;
    using System.Collections.Generic;
    using System.Text;


    public sealed class RegistryFeatureExtractor
    {
        // Known persistence / hijack key buckets
        private static readonly string[] buckets = new[] {
"HKCU/Run","HKLM/Run","HKCU/RunOnce","HKLM/RunOnce","HKCR/*/open/command","Other"
};


        private static int BucketIndex(string hive, string subKey)
        {
            var k = (hive + "/" + subKey).Replace("\\", "//").ToUpperInvariant();
            if (k.EndsWith("/CURRENTVERSION/RUN")) return hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            if (k.EndsWith("/CURRENTVERSION/RUNONCE")) return hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
            if (k.Contains("HKEY_CLASSES_ROOT/*/SHELL/OPEN/COMMAND") || k.Contains("HKCR/*/SHELL/OPEN/COMMAND")) return 4;
            return 5;
        }


        private static double ShannonEntropy(byte[] data)
        {
            if (data.Length == 0) return 0.0;
            Span<int> counts = stackalloc int[256];
            for (int i = 0; i < data.Length; i++) counts[data[i]]++;
            double H = 0.0; for (int i = 0; i < 256; i++) { if (counts[i] == 0) continue; double p = (double)counts[i] / data.Length; H -= p * Math.Log(p, 2); }
            return H; // 0..8
        }


        private static byte[] SerializeValue(object? val)
        {
            if (val == null) return Array.Empty<byte>();
            return val switch
            {
                byte[] b => b,
                string s => Encoding.UTF8.GetBytes(s),
                string[] sa => Encoding.UTF8.GetBytes(string.Join("�", sa)),
                int i => BitConverter.GetBytes(i),
                long l => BitConverter.GetBytes(l),
                _ => Encoding.UTF8.GetBytes(val.ToString() ?? string.Empty)
            };
        }


        public float[] Extract(string hive, string keyPath, string? valueName, object? valueObj, bool created, bool modified, bool deleted, bool burst)
        {
            // one-hot bucket (6)
            var idx = BucketIndex(hive, keyPath);
            var oneHot = new float[6]; oneHot[idx] = 1f;


            // value type flags (4): isString, isBinary, isNumber, isMultiString
            bool isString = valueObj is string; bool isBinary = valueObj is byte[]; bool isNumber = valueObj is int || valueObj is long; bool isMulti = valueObj is string[];


            // value entropy + length (2)
            var bytes = SerializeValue(valueObj);
            double ent = ShannonEntropy(bytes);
            double lenLog = bytes.Length > 0 ? Math.Log10(bytes.Length + 1) : 0.0;


            // change flags (3): created, modified, deleted
            var feats = new List<float>(10);
            feats.AddRange(oneHot); // 6
            feats.Add(isString ? 1f : 0f); // +1 = 7
            feats.Add(isBinary ? 1f : 0f); // +1 = 8
            feats.Add(isNumber ? 1f : 0f); // +1 = 9 (multi can be inferred via isString + len)
            feats.Add((float)ent); // +1 = 10
            feats.Add((float)lenLog); // +1 = 11
            feats.Add(created ? 1f : 0f); // +1 = 12
            feats.Add(modified ? 1f : 0f); // +1 = 13
            feats.Add(deleted ? 1f : 0f); // +1 = 14
            feats.Add(burst ? 1f : 0f); // +1 = 15
                                        // Total = 15 dims (we started with 10; expanded to 15 for richer signal)
            return feats.ToArray();
        }
    }
}
