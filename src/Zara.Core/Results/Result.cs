namespace Zara.Core.Results;

/// <summary>
/// A value that is either a successful <typeparamref name="T"/> or a
/// <see cref="ZaraError"/> — used at boundaries where failure is an ordinary,
/// expected outcome (a missing file, a denied path) rather than an exceptional
/// one, so callers are forced to handle it instead of it surfacing as an
/// uncaught exception three layers up. Exceptions are still used for truly
/// unexpected failures; this type is for the failures ARCHITECTURE.md's
/// interfaces (§23) return as ordinary data.
/// </summary>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly ZaraError? _error;

    private Result(T value)
    {
        _value = value;
        _error = null;
    }

    private Result(ZaraError error)
    {
        _value = default;
        _error = error;
    }

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    /// <summary>The success value. Throws if this result is a failure — check
    /// <see cref="IsSuccess"/> or use <see cref="TryGetValue"/>/<see cref="Match"/> first.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value of a failed Result: {_error}");

    /// <summary>The failure. Throws if this result is a success.</summary>
    public ZaraError Error => _error ?? throw new InvalidOperationException(
        "Cannot access Error of a successful Result.");

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(ZaraError error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(ZaraError error) => Failure(error);

    public bool TryGetValue(out T value)
    {
        value = IsSuccess ? _value! : default!;
        return IsSuccess;
    }

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<ZaraError, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(_error!);

    /// <summary>Transforms the success value, passing a failure through unchanged.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Success(map(_value!)) : Result<TOut>.Failure(_error!);

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>The non-generic form, for operations that succeed or fail with no value to return.</summary>
public readonly struct Result
{
    private readonly ZaraError? _error;

    private Result(ZaraError? error) => _error = error;

    public bool IsSuccess => _error is null;
    public bool IsFailure => _error is not null;

    public ZaraError Error => _error ?? throw new InvalidOperationException(
        "Cannot access Error of a successful Result.");

    public static Result Success() => new(null);
    public static Result Failure(ZaraError error) => new(error);

    public static implicit operator Result(ZaraError error) => Failure(error);

    public TOut Match<TOut>(Func<TOut> onSuccess, Func<ZaraError, TOut> onFailure) =>
        IsSuccess ? onSuccess() : onFailure(_error!);

    public override string ToString() => IsSuccess ? "Success" : $"Failure({_error})";
}
