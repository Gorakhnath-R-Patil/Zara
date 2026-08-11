using Zara.Core.Files;

namespace Zara.Core.Operations;

/// <summary>Recorded once an item has actually been attempted — the
/// counterpart to <see cref="OperationItemPlan"/>, which only records intent.</summary>
public sealed record OperationItemOutcome(
    int Seq,
    OperationItemStatus Status,
    CanonicalPath? DestPath = null,
    string? ErrorCode = null,
    string? RecycleId = null);
