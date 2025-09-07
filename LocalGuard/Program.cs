using LocalGuard.AI;
using LocalGuard.Baseline;
using LocalGuard.Config;
using LocalGuard.DTOs;
using LocalGuard.Heuristics;
using LocalGuard.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

class Program
{
    // ---------- OS helpers ----------
    static string[] WindowsStartupDirs()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return Array.Empty<string>();
        try
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            return new[] { user, common };
        }
        catch { return Array.Empty<string>(); }
    }

    static bool PathStartsWith(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    // ---------- Excludes / rotate ----------
    static bool ShouldExclude(string path, string[] excludes, string[]? selfFiles = null)
    {
        foreach (var ex in excludes)
            if (path.IndexOf(ex, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        if (selfFiles != null)
        {
            foreach (var f in selfFiles)
                if (path.Equals(f, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static void RotateIfNeeded(string filePath, long limitBytes)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists && fi.Length >= limitBytes)
            {
                var rotated = filePath + ".1";
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(filePath, rotated);
            }
        }
        catch { }
    }

    // ---------- Windows Event Log (no CA1416 warnings) ----------
    static void WriteWindowsEventIfAvailable(string source, string message, EventLogEntryType type)
    {
        if (!OperatingSystem.IsWindows()) return;
        WriteWindowsEvent_WindowsOnly(source, message, type);
    }

    [SupportedOSPlatform("windows")]
    static void WriteWindowsEvent_WindowsOnly(string source, string message, EventLogEntryType type)
    {
        try
        {
            if (!EventLog.SourceExists(source))
            {
                try { EventLog.CreateEventSource(source, "Application"); } catch { }
            }
            using var log = new EventLog("Application") { Source = source };
            log.WriteEntry(message, type);
        }
        catch { }
    }

    // ---------- Backups ----------
    static bool ShouldBackup(string path, Config cfg, bool hadAlert)
    {
        if (string.IsNullOrWhiteSpace(cfg.BackupDir)) return false;
        var ext = Path.GetExtension(path).Trim('.');

        if (cfg.BackupOnlyOnAlerts && !hadAlert) return false;
        if (Array.Exists(cfg.AutoBackupExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return false;
                if (fi.Length > cfg.BackupMaxBytes) return false;
                return true;
            }
            catch { return false; }
        }
        return false;
    }

    static void BackupFile(string path, string backupDir)
    {
        try
        {
            Directory.CreateDirectory(backupDir);
            var name = Path.GetFileName(path);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
            var dest = Path.Combine(backupDir, $"{name}.{stamp}.bak");
            File.Copy(path, dest, overwrite: false);
            Console.WriteLine($"[BACKUP] {dest}");
        }
        catch (Exception ex) { Console.WriteLine($"[BACKUP] failed: {ex.Message}"); }
    }

    static async Task<int> Main(string[] args)
    {
        // Load config
        var cfg = new Config();
        var cfgFile = Path.Combine(Directory.GetCurrentDirectory(), "tinyids.json");
        if (File.Exists(cfgFile))
        {
            try { cfg = JsonSerializer.Deserialize<Config>(await File.ReadAllTextAsync(cfgFile)) ?? cfg; }
            catch { Console.WriteLine("Warning: failed to parse tinyids.json; using defaults."); }
        }

        // Explicit watch dir?
        if (args.Length > 0 && Directory.Exists(args[0])) cfg.WatchDirs = new[] { args[0] };
        bool rebuildBaseline = Array.Exists(args, a => a.Equals("--baseline", StringComparison.OrdinalIgnoreCase));
        if (Array.Exists(args, a => a.Equals("--ai", StringComparison.OrdinalIgnoreCase))) cfg.UseAI = true;

        // Adjust away from bin/obj if launched from there
        string dir0 = cfg.WatchDirs.Length > 0 ? cfg.WatchDirs[0] : Directory.GetCurrentDirectory();
        if (dir0.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) || dir0.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetFullPath(Path.Combine(dir0, @"..\..\.."));
            if (Directory.Exists(root)) { dir0 = root; cfg.WatchDirs = new[] { dir0 }; Console.WriteLine($"Adjusted watch dir to project root: {dir0}"); }
        }

        // Files/baselines/logs
        string baselinePath = Path.Combine(dir0, ".ids_baseline.csv");
        string eventLogPath = Path.Combine(dir0, "ids_events.jsonl");
        string alertLogPath = Path.Combine(dir0, "ids_alerts.jsonl");
        string heurModelPath = Path.Combine(dir0, ".ids_model.json");

        var selfFiles = new[] { baselinePath, eventLogPath, alertLogPath, heurModelPath };

        var baseline = new BaselineStore(baselinePath);
        if (rebuildBaseline || baseline.Hashes.Count == 0)
        {
            Console.WriteLine("Building baseline...");
            foreach (var watchDir in cfg.WatchDirs)
            {
                if (!Directory.Exists(watchDir)) continue;
                foreach (var file in Directory.EnumerateFiles(watchDir, "*", SearchOption.AllDirectories))
                {
                    if (ShouldExclude(file, cfg.ExcludeSubstrings, selfFiles)) continue;
                    try { baseline.Hashes[file] = Hasher.HashFile(file); } catch { }
                }
            }
            baseline.Save();
            Console.WriteLine($"Baseline saved to {baselinePath} ({baseline.Hashes.Count} files).");
        }

        // Watchers
        var queue = new BlockingCollection<FileSystemEventArgs>(new ConcurrentQueue<FileSystemEventArgs>());
        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in cfg.WatchDirs)
        {
            if (!Directory.Exists(dir)) { Console.Error.WriteLine($"Directory not found: {dir}"); continue; }
            var w = new FileSystemWatcher(dir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
                Filter = "*"
            };
            w.Created += (s, e) => queue.Add(e);
            w.Changed += (s, e) => queue.Add(e);
            w.Renamed += (s, e) => queue.Add(e);
            w.Deleted += (s, e) => queue.Add(e);
            w.EnableRaisingEvents = true; watchers.Add(w);
        }

        Console.WriteLine($"Monitoring {string.Join(", ", cfg.WatchDirs)}. Press Ctrl+C to exit.");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        var burst = new BurstDetector(cfg.BurstWindowSeconds, cfg.BurstEventsThreshold);
        var startupDirs = WindowsStartupDirs();

        // --------- Build scorers (FILES) ----------
        var heurCore = new FileHeuristicCore(heurModelPath, startupDirs, burst, cfg.ExecExtensions);
        IScorer<FileEventCtx> fileHeur = new FileHeuristicScorer(heurCore, cfg.AnomalyThreshold);
        IScorer<FileEventCtx>? fileOnnx = null;

        var fileFx = new FeatureExtractor(cfg.MaxSampleBytes, startupDirs, burst, cfg.ExecExtensions);
        var fileModelCandidate = Path.Combine(Directory.GetCurrentDirectory(), cfg.OnnxModelPath);
        if (cfg.UseAI && File.Exists(fileModelCandidate))
        {
            try { fileOnnx = new FileOnnxScorer(fileModelCandidate, fileFx, cfg.AnomalyThreshold); Console.WriteLine($"ONNX(file) enabled: {cfg.OnnxModelPath}"); }
            catch (Exception ex) { Console.WriteLine($"ONNX(file) load failed: {ex.Message}."); }
        }
        else if (cfg.UseAI) Console.WriteLine("ONNX(file) model missing; continuing without it.");

        var fileFusion = new FusionScorer<FileEventCtx>(
            fileHeur, fileOnnx,
            cfg.Fusion.Equals("weighted", StringComparison.OrdinalIgnoreCase) ? FusionMode.Weighted : FusionMode.Max,
            cfg.FusionOnnxWeight, cfg.AnomalyThreshold);

        // --------- Registry baseline + monitors ----------
        string regBaselinePath = Path.Combine(dir0, ".ids_reg_baseline.csv");
        var regBase = new RegistryBaselineStore(regBaselinePath);
        if (rebuildBaseline || regBase.Hashes.Count == 0)
        {
            Console.WriteLine("Building registry baseline...");
            foreach (var rk in cfg.WatchRegistry)
            {
                foreach (var (k, vname, h) in RegistrySnapshot.WalkAndHash(rk.Hive, rk.SubKey, rk.IncludeSubtree))
                {
                    var id = k + "|" + (vname ?? "<default>");
                    regBase.Hashes[id] = h;
                }
            }
            regBase.Save();
        }

        var regQueue = new BlockingCollection<RegistryEvent>(new ConcurrentQueue<RegistryEvent>());
        var regMonitors = new List<RegistryMonitor>();
        foreach (var rk in cfg.WatchRegistry)
        {
            try { regMonitors.Add(new RegistryMonitor(rk.Hive, rk.SubKey, rk.IncludeSubtree, regQueue)); } catch { }
        }

        // --------- Build scorers (REGISTRY) ----------
        IScorer<RegistryEventCtx> regHeur = new RegistryHeuristicScorer(cfg.RegistryAnomalyThreshold);
        IScorer<RegistryEventCtx>? regOnnx = null;
        var regFx = new RegistryFeatureExtractor();
        var regModelCandidate = Path.Combine(Directory.GetCurrentDirectory(), cfg.RegistryOnnxModelPath);
        if (cfg.UseOnnxRegistry && File.Exists(regModelCandidate))
        {
            try { regOnnx = new RegistryOnnxScorer(regModelCandidate, regFx, cfg.RegistryAnomalyThreshold); Console.WriteLine($"ONNX(reg) enabled: {cfg.RegistryOnnxModelPath}"); }
            catch (Exception ex) { Console.WriteLine($"ONNX(reg) load failed: {ex.Message}."); }
        }
        else if (cfg.UseOnnxRegistry) Console.WriteLine("ONNX(reg) model missing; continuing without it.");

        var regFusion = new FusionScorer<RegistryEventCtx>(
            regHeur, regOnnx,
            cfg.Fusion.Equals("weighted", StringComparison.OrdinalIgnoreCase) ? FusionMode.Weighted : FusionMode.Max,
            cfg.FusionOnnxWeight, cfg.RegistryAnomalyThreshold);

        // --------- Logs ----------
        using var evLog = new StreamWriter(eventLogPath, append: true);
        using var alLog = new StreamWriter(alertLogPath, append: true);

        // --------- Registry drain task ----------
        var regTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                RegistryEvent re;
                try { re = regQueue.Take(cts.Token); } catch (OperationCanceledException) { break; }

                // Re-snapshot subtree and diff with baseline
                var current = new List<(string key, string? vname, string hash)>();
                foreach (var t in RegistrySnapshot.WalkAndHash(re.Hive, re.SubKey, includeSubtree: true))
                    current.Add((t.key, t.valueName, t.hash));

                var currentSet = new HashSet<string>(current.Select(t => t.key + "|" + (t.vname ?? "<default>")),
                                                     StringComparer.OrdinalIgnoreCase);

                // creates/mods
                foreach (var (k, vname, hNew) in current)
                {
                    var id = k + "|" + (vname ?? "<default>");
                    regBase.Hashes.TryGetValue(id, out var hOld);

                    bool created = hOld == null;
                    bool modified = (!created) && !string.Equals(hOld, hNew, StringComparison.OrdinalIgnoreCase);

                    if (created || modified)
                    {
                        var rule = created ? "registry_create" : "registry_modify";
                        var ar = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = rule, Path = id, Details = created ? "New registry value" : "Registry value changed" };
                        await alLog.WriteLineAsync(JsonSerializer.Serialize(ar)); await alLog.FlushAsync();
                        Console.WriteLine($"[ALERT] {rule} {id}");
                        regBase.Hashes[id] = hNew; regBase.Save();

                        // AI scoring
                        var ctx = BuildRegistryCtxFromId(id, created, modified, deleted: false);
                        var (score, reason) = regFusion.Evaluate(ctx);
                        if (score >= regFusion.Threshold)
                        {
                            var aa = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "registry_ai", Path = id, Details = reason, Heur = null, Onnx = null, Final = score };
                            await alLog.WriteLineAsync(JsonSerializer.Serialize(aa)); await alLog.FlushAsync();
                            Console.WriteLine($"[ALERT] registry_ai {score:F0} {id}");
                        }
                    }
                }

                // deletions
                var toRemove = new List<string>();
                foreach (var id in regBase.Hashes.Keys.ToList())
                {
                    if (id.StartsWith(re.Hive + "\\" + re.SubKey, StringComparison.OrdinalIgnoreCase) && !currentSet.Contains(id))
                        toRemove.Add(id);
                }
                foreach (var id in toRemove)
                {
                    var ar = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "registry_delete", Path = id, Details = "Registry value deleted" };
                    await alLog.WriteLineAsync(JsonSerializer.Serialize(ar)); await alLog.FlushAsync();
                    Console.WriteLine($"[ALERT] registry_delete {id}");

                    // AI scoring with delete flag
                    var ctx = BuildRegistryCtxFromId(id, created: false, modified: false, deleted: true);
                    var (score, reason) = regFusion.Evaluate(ctx);
                    if (score >= regFusion.Threshold)
                    {
                        var aa = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "registry_ai", Path = id, Details = reason, Heur = null, Onnx = null, Final = score };
                        await alLog.WriteLineAsync(JsonSerializer.Serialize(aa)); await alLog.FlushAsync();
                        Console.WriteLine($"[ALERT] registry_ai {score:F0} {id}");
                    }

                    regBase.Hashes.Remove(id); regBase.Save();
                }
            }
        });

        // --------- File event loop ----------
        try
        {
            while (!cts.IsCancellationRequested)
            {
                FileSystemEventArgs e;
                try { e = queue.Take(cts.Token); } catch (OperationCanceledException) { break; }

                var path = e.FullPath;
                if (ShouldExclude(path, cfg.ExcludeSubstrings, selfFiles)) continue;

                RotateIfNeeded(eventLogPath, cfg.LogRotateBytes);
                RotateIfNeeded(alertLogPath, cfg.LogRotateBytes);

                var rec = new EventRecord
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = e.ChangeType.ToString(),
                    Path = path
                };

                bool isCreated = e.ChangeType == WatcherChangeTypes.Created;
                bool isModified = e.ChangeType == WatcherChangeTypes.Changed;

                if (e.ChangeType != WatcherChangeTypes.Deleted && File.Exists(path))
                {
                    try
                    {
                        var newHash = Hasher.HashFile(path);
                        rec.NewHash = newHash;
                        baseline.Hashes.TryGetValue(path, out var oldHash);
                        rec.OldHash = oldHash;
                        if (isCreated || (oldHash != null && oldHash != newHash))
                        {
                            baseline.Hashes[path] = newHash; baseline.Save();
                        }
                    }
                    catch { }
                }
                else
                {
                    if (baseline.Hashes.Remove(path)) baseline.Save();
                }

                await evLog.WriteLineAsync(JsonSerializer.Serialize(rec)); await evLog.FlushAsync();
                Console.WriteLine($"{rec.Timestamp:o} {rec.EventType} {rec.Path}");

                // Rule: suspicious extension on create
                if (isCreated)
                {
                    var ext = Path.GetExtension(path).TrimStart('.');
                    foreach (var s in cfg.SuspiciousExtensions)
                    {
                        if (ext.Equals(s, StringComparison.OrdinalIgnoreCase))
                        {
                            var ar = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "suspicious_ext", Path = path, Details = $"Created file with extension .{ext}" };
                            await alLog.WriteLineAsync(JsonSerializer.Serialize(ar)); await alLog.FlushAsync();
                            Console.WriteLine($"[ALERT] suspicious_ext {path}");
                            if (cfg.WriteWindowsEventLog) WriteWindowsEventIfAvailable("LocalGuard", $"suspicious_ext: {path}", EventLogEntryType.Warning);
                            if (!string.IsNullOrWhiteSpace(cfg.BackupDir) && ShouldBackup(path, cfg, hadAlert: true))
                                BackupFile(path, Path.Combine(dir0, cfg.BackupDir!));
                            break;
                        }
                    }
                }

                // Rule: Startup folder writes
                foreach (var sd in startupDirs)
                {
                    if (!string.IsNullOrEmpty(sd) && PathStartsWith(path, sd))
                    {
                        var ar = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "startup_write", Path = path, Details = $"Change in Startup folder: {sd}" };
                        await alLog.WriteLineAsync(JsonSerializer.Serialize(ar)); await alLog.FlushAsync();
                        Console.WriteLine($"[ALERT] startup_write {path}");
                        if (cfg.WriteWindowsEventLog) WriteWindowsEventIfAvailable("LocalGuard", $"startup_write: {path}", EventLogEntryType.Warning);
                        if (!string.IsNullOrWhiteSpace(cfg.BackupDir) && ShouldBackup(path, cfg, hadAlert: true))
                            BackupFile(path, Path.Combine(dir0, cfg.BackupDir!));
                        break;
                    }
                }

                // AI (files)
                if (e.ChangeType != WatcherChangeTypes.Deleted && File.Exists(path))
                {
                    try
                    {
                        var fctx = new FileEventCtx { Path = path, Created = isCreated, Modified = isModified };
                        var (score, reason) = fileFusion.Evaluate(fctx);
                        if (score >= fileFusion.Threshold)
                        {
                            var ar = new AlertRecord { Timestamp = DateTime.UtcNow, Rule = "ai_anomaly", Path = path, Details = reason, Heur = null, Onnx = null, Final = score };
                            await alLog.WriteLineAsync(JsonSerializer.Serialize(ar)); await alLog.FlushAsync();
                            Console.WriteLine($"[ALERT] ai_anomaly {score:F0} {path}");
                            if (cfg.WriteWindowsEventLog) WriteWindowsEventIfAvailable("LocalGuard", $"ai_anomaly={score:F0}: {path}", EventLogEntryType.Warning);
                            if (!string.IsNullOrWhiteSpace(cfg.BackupDir) && ShouldBackup(path, cfg, hadAlert: true))
                                BackupFile(path, Path.Combine(dir0, cfg.BackupDir!));
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(cfg.BackupDir) && ShouldBackup(path, cfg, hadAlert: false))
                                BackupFile(path, Path.Combine(dir0, cfg.BackupDir!));
                        }
                    }
                    catch { }
                }
            }
        }
        finally
        {
            baseline.Save();
            foreach (var w in watchers) w.Dispose();
            cts.Cancel();
            try { await regTask; } catch { }
        }

        return 0;
    }

    static RegistryEventCtx BuildRegistryCtxFromId(string id, bool created, bool modified, bool deleted)
    {
        // id = "<Hive>\\<KeyPath>|<ValueName or <default>>"
        var pipe = id.LastIndexOf('|');
        var fullKey = pipe >= 0 ? id[..pipe] : id;
        var valueName = pipe >= 0 ? id[(pipe + 1)..] : "<default>";

        var firstSlash = fullKey.IndexOf('\\');
        var hive = firstSlash > 0 ? fullKey[..firstSlash] : "HKCU";
        var keyPath = firstSlash > 0 ? fullKey[(firstSlash + 1)..] : fullKey;

        object? valueObj = null;
        if (!deleted && OperatingSystem.IsWindows())
        {
            try
            {
                using var baseKey = hive.ToUpperInvariant() switch
                {
                    "HKLM" => Microsoft.Win32.Registry.LocalMachine,
                    "HKCU" => Microsoft.Win32.Registry.CurrentUser,
                    "HKCR" => Microsoft.Win32.Registry.ClassesRoot,
                    "HKU" => Microsoft.Win32.Registry.Users,
                    "HKCC" => Microsoft.Win32.Registry.CurrentConfig,
                    _ => Microsoft.Win32.Registry.CurrentUser
                };
                using var k = baseKey.OpenSubKey(keyPath, writable: false);
                if (k != null) valueObj = k.GetValue(valueName == "<default>" ? "" : valueName);
            }
            catch { }
        }

        return new RegistryEventCtx
        {
            Hive = hive,
            KeyPath = keyPath,
            ValueName = valueName == "<default>" ? null : valueName,
            ValueObj = valueObj,
            Created = created,
            Modified = modified,
            Deleted = deleted,
            Burst = false
        };
    }
}
