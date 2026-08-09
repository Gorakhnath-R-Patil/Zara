namespace Zara.Core.Results;

/// <summary>
/// A closed set of error categories used everywhere in Zara that an operation
/// can fail — filesystem calls, index queries, tool execution (§16.3). Kept
/// closed (not free-form strings) so callers can branch on failure kind
/// without string matching, and so a tool result's error code is safe to hand
/// to an LLM: a fixed enum can't leak a stack trace or a system path.
/// </summary>
public enum ErrorCode
{
    Unknown = 0,

    // ── Path / existence ────────────────────────────────────────────────────
    NotFound,
    InvalidPath,
    OutsideAllowedRoot,
    AlreadyExists,
    PathTooLong,

    // ── Access ───────────────────────────────────────────────────────────────
    AccessDenied,
    Blocked,             // refused by policy (BLOCKED_ROOTS etc.), not by the OS

    // ── Contention / transient ──────────────────────────────────────────────
    SharingViolation,
    Timeout,
    Cancelled,
    Transient,           // retryable I/O hiccup — see ARCHITECTURE.md §16.4

    // ── Capacity ─────────────────────────────────────────────────────────────
    InsufficientSpace,
    QuotaExceeded,

    // ── Shape / support ──────────────────────────────────────────────────────
    Io,
    Unsupported,
    InvalidArgument,
}
