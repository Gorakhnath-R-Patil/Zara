namespace Zara.Core.Operations;

/// <summary>ARCHITECTURE.md §19.2's status lifecycle. <c>Planned</c> and
/// <c>Executing</c> both get durably written BEFORE any file touches disk
/// (§19.3's write protocol, P6: "every write is journaled before it
/// happens") — that ordering is what makes crash recovery (§19.4)
/// possible: a row stuck at <c>Executing</c> after a restart means exactly
/// what it says, an interrupted operation, never a lost one.</summary>
public enum OperationStatus { Planned, Executing, Completed, Partial, Failed, Undone, Expired }
