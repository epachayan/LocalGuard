# LocalGuard 🌱
A self-sustained host guard for air-gapped / low-connectivity environments. Hybrid **heuristics + offline ONNX ML**, written in **C#/.NET**. Monitors **files** and **Windows Registry** for persistence and anomalous changes.

> Metaphor: like a **plant’s immune system** — local pattern detectors (heuristics) + inherited resistance (ONNX models) — no cloud needed.

## Why
- Air-gapped friendly: zero external dependencies
- Low footprint: event-driven, tiny models
- Explainable: every alert has reasons
- Portable: .NET 8, ONNX Runtime

## What it detects
- **Files**: rare extensions, high entropy, startup writes, burst activity, execs in user paths
- **Registry**: Run/RunOnce/open\\command changes, value type/entropy anomalies, create/modify/delete

## Architecture
```
FileSystemWatcher ─┐ RegistryPoller → Snapshot+Diff
                   ├─ Heuristic (rules, entropy, rarity) ┐
                   └─ ONNX (ids.onnx / ids_registry.onnx) ├→ Fusion (max/weighted) → Alert + (optional) Backup
                                               Baselines ──┘
```

## Getting started
```bash
git clone https://github.com/epachayan/LocalGuard
cd LocalGuard
dotnet build
```

### (A) Use prebuilt models
Place:
```
models/ids.onnx
models/ids_registry.onnx
```

### (B) Or create synthetic models (offline)
```powershell
# Windows PowerShell
python -m pip install --upgrade pip
pip install scikit-learn skl2onnx numpy
python tools\make_model.py
python tools\make_registry_model.py
```

## Run
```powershell
# 1) build baseline (first run)
dotnet run -- --baseline

# 2) monitor current dir, enable AI
dotnet run -- --ai

# 3) monitor a specific path
dotnet run -- "C:\Windows" --ai
```

## Configure (`tinyids.json`)
```json
{
  "WatchDirs": ["C:\\Windows","C:\\Users\\%USERNAME%\\AppData"],
  "SuspiciousExtensions": ["exe","dll","scr","ps1","vbs","js","bat","cmd"],
  "ExcludeSubstrings": [".git","node_modules","\\bin\\","\\obj\\"],
  "ExecExtensions": ["exe","dll","scr","ps1","vbs","js","bat","cmd"],
  "BurstWindowSeconds": 10,
  "BurstEventsThreshold": 8,
  "LogRotateBytes": 10485760,
  "WriteWindowsEventLog": true,
  "UseAI": true,
  "AnomalyThreshold": 70.0,
  "OnnxModelPath": "models/ids.onnx",
  "Fusion": "max",
  "FusionOnnxWeight": 0.6,
  "MaxSampleBytes": 32768,
  "BackupDir": "_ids_backups",
  "AutoBackupExtensions": ["docx","xlsx","pptx","pdf","jpg","png"],
  "BackupMaxBytes": 52428800,
  "BackupOnlyOnAlerts": true,
  "WatchRegistry": [
    { "Hive":"HKCU", "SubKey":"Software\\Microsoft\\Windows\\CurrentVersion\\Run", "IncludeSubtree": false },
    { "Hive":"HKLM", "SubKey":"Software\\Microsoft\\Windows\\CurrentVersion\\Run", "IncludeSubtree": false },
    { "Hive":"HKCU", "SubKey":"Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", "IncludeSubtree": false },
    { "Hive":"HKLM", "SubKey":"Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce", "IncludeSubtree": false },
    { "Hive":"HKCR", "SubKey":"*\\shell\\open\\command", "IncludeSubtree": true }
  ],
  "RegistryAnomalyThreshold": 70.0,
  "UseOnnxRegistry": true,
  "RegistryOnnxModelPath": "models/ids_registry.onnx"
}
```

## Sample alert
```
[ALERT] ai_anomaly 91 C:\Users\...\AppData\Roaming\winhelper.scr
  reason: heur=84(rare ext scr,exec,startup,burst), onnx=91(p=0.91)
```

## Build self-contained binary
```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

## License
MIT (see LICENSE)

## Security
See .github/SECURITY.md. To report a vulnerability, email security@yourdomain.tld.
