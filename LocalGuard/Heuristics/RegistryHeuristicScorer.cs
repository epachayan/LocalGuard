using LocalGuard.AI;
using LocalGuard.DTOs;
using System.Text.RegularExpressions;

namespace LocalGuard.Heuristics;

public sealed class RegistryHeuristicScorer : IScorer<RegistryEventCtx>
{
    public double Threshold { get; }

    public RegistryHeuristicScorer(double threshold) { Threshold = threshold; }

    public (double score, string reason) Evaluate(RegistryEventCtx c)
    {
        double s = 0; var reasons = new List<string>();

        if (c.Created) { s += 45; reasons.Add("created"); }
        if (c.Modified) { s += 30; reasons.Add("modified"); }
        if (c.Deleted) { s += 35; reasons.Add("deleted"); }

        var kp = c.KeyPath ?? "";
        bool isAutorun = kp.Contains("\\Run", StringComparison.OrdinalIgnoreCase) || kp.Contains("\\RunOnce", StringComparison.OrdinalIgnoreCase);
        bool isOpenCmd = kp.EndsWith("\\open\\command", StringComparison.OrdinalIgnoreCase);

        if (isAutorun) s += 25;
        if (isOpenCmd) s += 25;
        if (isAutorun) reasons.Add("autorun");
        if (isOpenCmd) reasons.Add("open\\command");

        if (c.ValueObj is string sv)
        {
            if (Regex.IsMatch(sv, @"(?i)\.(exe|dll|scr|bat|cmd|ps1)\b")) { s += 20; reasons.Add("exec_path"); }
            if (Regex.IsMatch(sv, @"(?i)AppData\\Roaming|Temp|Downloads")) { s += 15; reasons.Add("user_path"); }
        }

        if (c.Burst) { s += 10; reasons.Add("burst"); }

        s = Math.Clamp(s, 0, 100);
        return (s, reasons.Count > 0 ? string.Join(",", reasons) : "ok");
    }
}
