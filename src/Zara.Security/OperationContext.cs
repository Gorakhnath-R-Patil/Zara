using Zara.Core.Operations;

namespace Zara.Security;

/// <summary>
/// The facts <see cref="RiskClassifier"/> needs beyond the bare
/// <see cref="OperationPlan"/> — total byte count, whether it crosses
/// volumes, whether it touches a reparse point, whether it's a permanent
/// (non-Recycle-Bin) delete. Deliberately a caller-supplied aggregate rather
/// than something the classifier computes itself: gathering it means
/// touching the live filesystem (file sizes, volume serials, attributes),
/// which the caller (already walking the items to build the plan) has
/// cheaply in hand and the classifier — kept pure and fuzz-testable — should
/// not need to.
/// </summary>
public sealed record OperationContext(
    OperationPlan Plan,
    long TotalBytes,
    bool CrossesVolumes,
    bool TouchesReparsePoint,
    bool IsPermanentDelete)
{
    public int ItemCount => Plan.Items.Count;
}
