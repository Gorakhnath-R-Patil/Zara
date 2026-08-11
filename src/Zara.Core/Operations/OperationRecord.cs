namespace Zara.Core.Operations;

public sealed record OperationRecord(
    Guid Id,
    OperationKind Kind,
    OperationStatus Status,
    long CreatedUtc,
    long? CompletedUtc,
    int ItemCount,
    long TotalBytes,
    string RiskClass,
    bool ConfirmedByUser,
    string? UserRequest);

public sealed record OperationItemRecord(
    Guid OperationId,
    int Seq,
    string SourcePath,
    string? DestPath,
    OperationItemStatus Status,
    string? RecycleId,
    string? ErrorCode);
