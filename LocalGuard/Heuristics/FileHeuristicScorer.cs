using LocalGuard.AI;
using LocalGuard.DTOs;

namespace LocalGuard.Heuristics;

public sealed class FileHeuristicScorer : IScorer<FileEventCtx>
{
    private readonly FileHeuristicCore _core;
    public double Threshold { get; }

    public FileHeuristicScorer(FileHeuristicCore core, double threshold)
    {
        _core = core; Threshold = threshold;
    }

    public (double score, string reason) Evaluate(FileEventCtx ctx)
        => _core.Evaluate(ctx.Path, ctx.Created, ctx.Modified);
}
