using Zara.Core.Files;

namespace Zara.Core.Operations;

/// <summary>One item within a planned operation, before execution — a
/// declared intent, not yet an outcome.</summary>
public sealed record OperationItemPlan(CanonicalPath SourcePath, CanonicalPath? DestPath = null);

/// <summary>
/// What the caller wants to do, durably recorded via
/// <c>IOperationJournal.BeginAsync</c> BEFORE any file is touched
/// (ARCHITECTURE.md §19.3, principle P6) — a crash between "planned" and
/// "the first file actually moved" leaves a complete, honest record of
/// intent rather than nothing at all.
/// </summary>
public sealed record OperationPlan(
    OperationKind Kind,
    IReadOnlyList<OperationItemPlan> Items,
    string RiskClass = "unknown",
    bool ConfirmedByUser = true,
    string? UserRequest = null);
