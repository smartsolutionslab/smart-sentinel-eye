namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Two-generic Result type per ADR-0047. Business failures carry a typed error;
/// programmer errors and infrastructure failures throw exceptions.
/// </summary>
public readonly struct Result<TValue, TError>
    where TValue : notnull
    where TError : notnull
{
    private readonly TValue _value;
    private readonly TError _error;
    private readonly bool _isSuccess;

    private Result(TValue value, TError error, bool isSuccess)
    {
        _value = value;
        _error = error;
        _isSuccess = isSuccess;
    }

    public bool IsSuccess => _isSuccess;

    public bool IsFailure => !_isSuccess;

    public TValue Value =>
        _isSuccess ? _value : throw new InvalidOperationException("Result is a failure; no value.");

    public TError Error =>
        _isSuccess ? throw new InvalidOperationException("Result is a success; no error.") : _error;

    public static Result<TValue, TError> Success(TValue value) =>
        new(value, default!, isSuccess: true);

    public static Result<TValue, TError> Failure(TError error) =>
        new(default!, error, isSuccess: false);

    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<TError, TOut> onFailure) =>
        _isSuccess ? onSuccess(_value) : onFailure(_error);
}
