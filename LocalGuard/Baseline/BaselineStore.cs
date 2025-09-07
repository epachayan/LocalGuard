using System.Security.Cryptography;

namespace LocalGuard.Baseline;

public sealed class BaselineStore
{
    private readonly string _storePath;
    public Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public BaselineStore(string storePath) { _storePath = storePath; Load(); }

    private void Load()
    {
        if (!File.Exists(_storePath)) return;
        foreach (var line in File.ReadAllLines(_storePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',', 2);
            if (parts.Length == 2) Hashes[parts[0]] = parts[1];
        }
    }

    public void Save()
    {
        using var sw = new StreamWriter(_storePath, false);
        foreach (var kv in Hashes) sw.WriteLine($"{kv.Key},{kv.Value}");
    }
}

public static class Hasher
{
    public static string HashFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}

public sealed class RegistryBaselineStore
{
    private readonly string _storePath;
    public Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public RegistryBaselineStore(string storePath) { _storePath = storePath; Load(); }

    private void Load()
    {
        if (!File.Exists(_storePath)) return;
        foreach (var line in File.ReadAllLines(_storePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',', 2);
            if (parts.Length == 2) Hashes[parts[0]] = parts[1];
        }
    }

    public void Save()
    {
        using var sw = new StreamWriter(_storePath, false);
        foreach (var kv in Hashes) sw.WriteLine($"{kv.Key},{kv.Value}");
    }
}
