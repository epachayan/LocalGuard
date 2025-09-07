namespace LocalGuard.DTOs;

public sealed class FileEventCtx
{
    public required string Path { get; init; }
    public bool Created { get; init; }
    public bool Modified { get; init; }
}

public sealed class RegistryEventCtx
{
    public required string Hive { get; init; }      // "HKCU", "HKLM", ...
    public required string KeyPath { get; init; }   // "Software\\...\\Run"
    public string? ValueName { get; init; }         // null => (Default)
    public object? ValueObj { get; init; }          // best-effort current value (null if deleted)
    public bool Created { get; init; }
    public bool Modified { get; init; }
    public bool Deleted { get; init; }
    public bool Burst { get; init; }
}
