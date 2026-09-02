namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Explicit-absence type per ADR-0048. NRT is disabled at the solution level;
/// Option&lt;T&gt; is the canonical way to express domain absence.
/// </summary>
public readonly struct Option<T> : IEquatable<Option<T>>
    where T : notnull
{
    private readonly T value;
    private readonly bool hasValue;

    private Option(T value, bool hasValue)
    {
        this.value = value;
        this.hasValue = hasValue;
    }

    public bool HasValue => hasValue;

    public T Value =>
        hasValue ? value : throw new InvalidOperationException("Option has no value.");

    public static Option<T> Some(T value) =>
        value is null
            ? throw new ArgumentNullException(nameof(value))
            : new Option<T>(value, hasValue: true);

    public static Option<T> None => default;

    public TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none) =>
        hasValue ? some(value) : none();

    public Option<TOut> Map<TOut>(Func<T, TOut> mapper)
        where TOut : notnull =>
        hasValue ? Option<TOut>.Some(mapper(value)) : Option<TOut>.None;

    public T GetOrDefault(T fallback) => hasValue ? value : fallback;

    public bool Equals(Option<T> other) =>
        hasValue == other.hasValue && (!hasValue || EqualityComparer<T>.Default.Equals(value, other.value));

    public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

    public override int GetHashCode() =>
        hasValue ? HashCode.Combine(true, value) : HashCode.Combine(false);

    public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

    public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

    public override string ToString() => hasValue ? $"Some({value})" : "None";
}
