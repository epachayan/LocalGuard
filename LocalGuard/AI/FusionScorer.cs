namespace LocalGuard.AI;

public sealed class FusionScorer<TCtx> : IScorer<TCtx>
{
    private readonly IScorer<TCtx> _a;
    private readonly IScorer<TCtx>? _b;
    private readonly FusionMode _mode;
    private readonly double _w;
    public double Threshold { get; }

    public FusionScorer(IScorer<TCtx> a, IScorer<TCtx>? b, FusionMode mode, double weight, double threshold)
    {
        _a = a; _b = b; _mode = mode; _w = Math.Clamp(weight, 0, 1);
        Threshold = threshold;
    }

    public (double score, string reason) Evaluate(TCtx ctx)
    {
        var (sa, ra) = _a.Evaluate(ctx);
        if (_b == null) return (sa, ra);

        var (sb, rb) = _b.Evaluate(ctx);
        double final = _mode == FusionMode.Max ? Math.Max(sa, sb) : (_w * sb + (1 - _w) * sa);
        var reason = $"heur={sa:F0}{(string.IsNullOrEmpty(ra) ? "" : $"({ra})")}, onnx={sb:F0}{(string.IsNullOrEmpty(rb) ? "" : $"({rb})")}";
        return (final, reason);
    }
}
