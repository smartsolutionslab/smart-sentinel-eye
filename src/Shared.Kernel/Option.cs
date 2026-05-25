namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Explicit-absence type per ADR-0048. NRT is disabled at the solution level;
/// Option&lt;T&gt; is the canonical way to express domain absence.
/// </summary>
public readonly struct Option<T> : IEquatable<Option<T>>
    where T : notnull
{
    private readonly T _value;
    private readonly bool _hasValue;

    private Option(T value, bool hasValue)
    {
        _value = value;
        _hasValue = hasValue;
    }

    public bool HasValue => _hasValue;

    public T Value =>
        _hasValue ? _value : throw new InvalidOperationException("Option has no value.");

    public static Option<T> Some(T value) =>
        value is null
            ? throw new ArgumentNullException(nameof(value))
            : new Option<T>(value, hasValue: true);

    public static Option<T> None => default;

    public TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) =>
        _hasValue ? some(_value) : none();

    public Option<TOut> Map<TOut>(Func<T, TOut> mapper)
        where TOut : notnull =>
        _hasValue ? Option<TOut>.Some(mapper(_value)) : Option<TOut>.None;

    public T GetOrDefault(T fallback) => _hasValue ? _value : fallback;

    public bool Equals(Option<T> other) =>
        _hasValue == other._hasValue && (!_hasValue || EqualityComparer<T>.Default.Equals(_value, other._value));

    public override bool Equals(object obj) => obj is Option<T> other && Equals(other);

    public override int GetHashCode() =>
        _hasValue ? HashCode.Combine(true, _value) : HashCode.Combine(false);

    public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

    public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

    public override string ToString() => _hasValue ? $"Some({_value})" : "None";
}
