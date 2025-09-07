namespace LocalGuard.DTOs;

public sealed class EventRecord
{
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = "?";
    public string Path { get; set; } = string.Empty;
    public string? OldHash { get; set; }
    public string? NewHash { get; set; }
}

public sealed class AlertRecord
{
    public DateTime Timestamp { get; set; }
    public string Rule { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public double? Heur { get; set; }
    public double? Onnx { get; set; }
    public double? Final { get; set; }
}

public sealed class RegistryEvent
{
    public string Hive { get; set; } = "HKCU";
    public string SubKey { get; set; } = "";
}
