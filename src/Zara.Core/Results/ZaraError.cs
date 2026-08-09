namespace Zara.Core.Results;

/// <summary>
/// A structured failure. <see cref="Message"/> must be safe to show a user or
/// hand to an LLM as a tool result (§16.3) — never a raw exception message or
/// a stack trace; <see cref="Cause"/> carries the real exception for logging
/// only and must never be serialized out of the process.
/// </summary>
/// <param name="Code">The failure category — branch on this, not on <see cref="Message"/>.</param>
/// <param name="Message">A short, user/model-safe description of what went wrong.</param>
/// <param name="Retryable">True if the SAME operation might succeed if retried
/// unchanged (transient I/O, a sharing violation). False for anything where
/// retrying without changing something first cannot help.</param>
/// <param name="Cause">The underlying exception, if any — for logs and diagnostics only.</param>
public sealed record ZaraError(ErrorCode Code, string Message, bool Retryable = false, Exception? Cause = null)
{
    public override string ToString() => $"{Code}: {Message}";

    public static ZaraError NotFound(string message) => new(ErrorCode.NotFound, message);
    public static ZaraError AccessDenied(string message) => new(ErrorCode.AccessDenied, message);
    public static ZaraError InvalidPath(string message) => new(ErrorCode.InvalidPath, message);
    public static ZaraError Cancelled(string message = "The operation was cancelled.") =>
        new(ErrorCode.Cancelled, message);

    public static ZaraError FromException(ErrorCode code, Exception ex, bool retryable = false) =>
        new(code, ex.Message, retryable, ex);
}
