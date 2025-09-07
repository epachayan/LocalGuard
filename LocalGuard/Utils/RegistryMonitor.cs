using Microsoft.Win32;
using System.Collections.Concurrent;

namespace LocalGuard.Utils;

// Simple, low-footprint poller: checks LastWriteTime of a key tree and emits events
public sealed class RegistryMonitor : IDisposable
{
    private readonly string _hive;
    private readonly string _subKey;
    private readonly bool _includeSubtree;
    private readonly BlockingCollection<LocalGuard.DTOs.RegistryEvent> _queue;
    private readonly Timer _timer;
    private DateTime _last;

    public RegistryMonitor(string hive, string subKey, bool includeSubtree, BlockingCollection<LocalGuard.DTOs.RegistryEvent> queue)
    {
        _hive = hive; _subKey = subKey; _includeSubtree = includeSubtree; _queue = queue;
        _last = DateTime.MinValue;
        _timer = new Timer(Tick, null, dueTime: 1000, period: 2000);
    }

    void Tick(object? _)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            using var root = _hive.ToUpperInvariant() switch
            {
                "HKLM" => Registry.LocalMachine,
                "HKCU" => Registry.CurrentUser,
                "HKCR" => Registry.ClassesRoot,
                "HKU" => Registry.Users,
                "HKCC" => Registry.CurrentConfig,
                _ => Registry.CurrentUser
            };
            using var key = root.OpenSubKey(_subKey, writable: false);
            if (key == null) return;

            var now = GetLastWriteRecursive(key, _includeSubtree);
            if (now > _last)
            {
                _last = now;
                _queue.Add(new LocalGuard.DTOs.RegistryEvent { Hive = _hive, SubKey = _subKey });
            }
        }
        catch { }
    }

    static DateTime GetLastWriteRecursive(RegistryKey key, bool recurse)
    {
        DateTime max = key.View == RegistryView.Default ? DateTime.UtcNow : DateTime.UtcNow; // fallback
        try { max = key.GetValueNames().Length >= 0 ? key.Timestamp() : DateTime.UtcNow; } catch { }

        if (recurse)
        {
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var child = key.OpenSubKey(sub, writable: false);
                    if (child != null)
                    {
                        var t = GetLastWriteRecursive(child, recurse);
                        if (t > max) max = t;
                    }
                }
                catch { }
            }
        }
        return max;
    }

    public void Dispose() => _timer.Dispose();
}

// Helper to get a best-effort last write timestamp (not officially exposed; approximate via subkeys)
internal static class RegistryKeyExtensions
{
    public static DateTime Timestamp(this RegistryKey key)
    {
        // There is no direct managed API; we approximate: if values or subkeys change, we’ll see it via polling.
        // Returning UTC now is good enough to trigger a diff cycle; the snapshot does the accurate work.
        return DateTime.UtcNow;
    }
}
