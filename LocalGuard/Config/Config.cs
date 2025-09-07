namespace LocalGuard.Config;

public sealed class RegKey
{
    public string Hive { get; set; } = "HKCU";
    public string SubKey { get; set; } = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    public bool IncludeSubtree { get; set; } = false;
}

public sealed class Config
{
    public string[] WatchDirs { get; set; } = new[] { Directory.GetCurrentDirectory() };
    public string[] SuspiciousExtensions { get; set; } = new[] { "exe", "dll", "scr", "ps1", "vbs", "js", "bat", "cmd" };
    public string[] ExcludeSubstrings { get; set; } = new[] { ".git", "node_modules", "bin\\", "obj\\" };

    public string[] ExecExtensions { get; set; } = new[] { "exe", "dll", "scr", "ps1", "vbs", "js", "bat", "cmd" };

    public int BurstWindowSeconds { get; set; } = 10;
    public int BurstEventsThreshold { get; set; } = 8;
    public long LogRotateBytes { get; set; } = 10 * 1024 * 1024;
    public bool WriteWindowsEventLog { get; set; } = true;

    public bool UseAI { get; set; } = true;
    public double AnomalyThreshold { get; set; } = 70.0;

    public string OnnxModelPath { get; set; } = "models/ids.onnx";
    public string Fusion { get; set; } = "max";       // "max" or "weighted"
    public double FusionOnnxWeight { get; set; } = 0.6;
    public int MaxSampleBytes { get; set; } = 32 * 1024;

    public string? BackupDir { get; set; } = null;    // e.g. "_ids_backups"
    public string[] AutoBackupExtensions { get; set; } = new[] { "docx", "xlsx", "pptx", "pdf", "jpg", "png" };
    public long BackupMaxBytes { get; set; } = 50 * 1024 * 1024; // 50MB
    public bool BackupOnlyOnAlerts { get; set; } = true;

    public RegKey[] WatchRegistry { get; set; } = new[]
    {
        new RegKey{ Hive="HKCU", SubKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run", IncludeSubtree=false },
        new RegKey{ Hive="HKLM", SubKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run", IncludeSubtree=false },
        new RegKey{ Hive="HKCU", SubKey="Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", IncludeSubtree=false },
        new RegKey{ Hive="HKLM", SubKey="Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", IncludeSubtree=false },
        new RegKey{ Hive="HKCR", SubKey="*\\shell\\open\\command", IncludeSubtree=true }
    };

    public double RegistryAnomalyThreshold { get; set; } = 70.0;
    public bool UseOnnxRegistry { get; set; } = true;
    public string RegistryOnnxModelPath { get; set; } = "models/ids_registry.onnx";
}
