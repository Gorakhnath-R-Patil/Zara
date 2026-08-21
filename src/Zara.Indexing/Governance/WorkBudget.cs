namespace Zara.Indexing.Governance;

/// <param name="Threads">How many worker threads the indexing pipeline should run.</param>
/// <param name="BatchSize">Items per batch — larger batches when there's headroom.</param>
/// <param name="IoRateMbPerSec"><see cref="double.PositiveInfinity"/> means "no cap".</param>
/// <param name="Paused">When true, the indexer should stop entirely, not just
/// throttle down to 1 thread — §25.1's distinction between "slow" and "off".</param>
public sealed record WorkBudget(int Threads, int BatchSize, double IoRateMbPerSec, bool Paused)
{
    public static readonly WorkBudget PausedBudget = new(0, 0, 0, Paused: true);
}
