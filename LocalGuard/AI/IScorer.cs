namespace LocalGuard.AI;

public interface IScorer<TCtx>
{
    (double score, string reason) Evaluate(TCtx ctx);
    double Threshold { get; }
}

public enum FusionMode { Max, Weighted }
