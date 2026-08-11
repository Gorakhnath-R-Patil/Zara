namespace Zara.Filesystem.Shell;

/// <summary>A batch of same-kind operations, executed as one
/// <c>IFileOperation</c> transaction — ARCHITECTURE.md §10.3: this is the
/// one door every mutating filesystem operation goes through, never
/// <c>System.IO</c> directly, for Recycle Bin support, correct collision
/// handling, and shell notification.</summary>
public sealed record FileOperationBatch(FileOperationKind Kind, IReadOnlyList<FileOperationItem> Items);
