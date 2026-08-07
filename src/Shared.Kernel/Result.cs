namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Two-generic Result type per ADR-0047. Business failures carry a typed error;
/// programmer errors and infrastructure failures throw exceptions.
/// </summary>
public readonly struct Result<TValue, TError>
    where TValue : notnull
    where TError : notnull
{
    private readonly TValue value;
    private readonly TError error;
    private readonly bool isSuccess;

    private Result(TValue value, TError error, bool isSuccess)
    {
        this.value = value;
        this.error = error;
        this.isSuccess = isSuccess;
    }

    public bool IsSuccess => isSuccess;

    public bool IsFailure => !isSuccess;

    public TValue Value =>
        isSuccess ? value : throw new InvalidOperationException("Result is a failure; no value.");

    public TError Error =>
        isSuccess ? throw new InvalidOperationException("Result is a success; no error.") : error;

    public static Result<TValue, TError> Success(TValue value) =>
        new(value, default, isSuccess: true);

    public static Result<TValue, TError> Failure(TError error) =>
        new(default, error, isSuccess: false);

    /// <summary>
    /// Lets a handler write <c>Success(dto)</c> instead of naming both type
    /// arguments again — the outcome carries only the half it knows about, and
    /// the return type supplies the rest (ADR-0047).
    /// </summary>
    public static implicit operator Result<TValue, TError>(SuccessOutcome<TValue> outcome) =>
        Success(outcome.Value);

    public static implicit operator Result<TValue, TError>(FailureOutcome<TError> outcome) =>
        Failure(outcome.Error);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<TError, TOut> onFailure) =>
        isSuccess ? onSuccess(value) : onFailure(error);
}

/// <summary>
/// Half-built success, waiting for a return type to tell it which
/// <see cref="Result{TValue,TError}"/> it belongs to.
/// </summary>
public readonly struct SuccessOutcome<TValue>
    where TValue : notnull
{
    internal SuccessOutcome(TValue value) => Value = value;

    internal TValue Value { get; }
}

/// <summary>
/// Half-built failure. The type argument is the error <em>base</em>, not the
/// variant: generics are invariant, so a <c>FailureOutcome</c> built from a
/// variant would not convert to the Result the handler returns. That is why
/// each hierarchy has a <c>&lt;Name&gt;Failures</c> class whose factories
/// return the base.
/// </summary>
public readonly struct FailureOutcome<TError>
    where TError : notnull
{
    internal FailureOutcome(TError error) => Error = error;

    internal TError Error { get; }
}

/// <summary>
/// Entry point that lets a call site name only what it is carrying:
/// <c>Success(dto)</c> / <c>Failure(GetRuleFailures.RuleNotFound(name))</c>
/// rather than repeating both type arguments. Imported globally via
/// <c>using static</c> (ADR-0047).
/// </summary>
public static class Result
{
    public static SuccessOutcome<TValue> Success<TValue>(TValue value)
        where TValue : notnull => new(value);

    public static FailureOutcome<TError> Failure<TError>(TError error)
        where TError : notnull => new(error);
}
