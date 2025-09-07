using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;

namespace LocalGuard.Utils;

public static class RegistrySnapshot
{
    public static IEnumerable<(string key, string? valueName, string hash)> WalkAndHash(string hive, string subKey, bool includeSubtree)
    {
        if (!OperatingSystem.IsWindows()) yield break;

        using var root = hive.ToUpperInvariant() switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            "HKCR" => Registry.ClassesRoot,
            "HKU" => Registry.Users,
            "HKCC" => Registry.CurrentConfig,
            _ => Registry.CurrentUser
        };

        using var start = root.OpenSubKey(subKey, writable: false);
        if (start == null) yield break;

        foreach (var item in WalkKey(start, includeSubtree))
            yield return item;
    }

    private static IEnumerable<(string key, string? valueName, string hash)> WalkKey(RegistryKey k, bool recurse)
    {
        var keyPath = k.Name; // includes hive prefix
        foreach (var name in k.GetValueNames())
        {
            object? v = null;
            try { v = k.GetValue(name); } catch { }
            var bytes = ValueToBytes(v);
            var hash = HashBytes(bytes);
            yield return (keyPath, string.IsNullOrEmpty(name) ? null : name, hash);
        }

        if (recurse)
        {
            foreach (var sub in k.GetSubKeyNames())
            {
                using var child = k.OpenSubKey(sub, writable: false);
                if (child != null)
                {
                    foreach (var t in WalkKey(child, recurse)) yield return t;
                }
            }
        }
    }

    private static byte[] ValueToBytes(object? v)
    {
        if (v is null) return Array.Empty<byte>();
        if (v is byte[] bb) return bb;
        if (v is string s) return Encoding.UTF8.GetBytes(s);
        try { return Encoding.UTF8.GetBytes(v.ToString() ?? ""); } catch { return Array.Empty<byte>(); }
    }

    private static string HashBytes(byte[] b)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(b));
    }
}
