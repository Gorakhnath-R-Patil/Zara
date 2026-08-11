namespace Zara.Filesystem.Shell;

public sealed record OperationOutcome(bool Succeeded, IReadOnlyList<FileOperationItemOutcome> Items);

public sealed record OperationProgress(int ItemsCompleted, int ItemsTotal, string? CurrentItemName);
