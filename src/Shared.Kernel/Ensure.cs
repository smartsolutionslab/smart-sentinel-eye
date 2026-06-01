using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Fluent validation chain per ADR-0059. The string overload validates
/// value-object invariants (throws ArgumentException on the first failed
/// predicate; concrete VO factories catch and translate to Result.Failure
/// when needed). The generic overload is the project-standard argument
/// guard (ADR-0105) — it replaces <c>ArgumentNullException.ThrowIfNull</c>
/// and bare <c>throw new ArgumentException</c> preconditions.
/// </summary>
public static class Ensure
{
    public static EnsuredString That(string value, string parameterName = "value") =>
        new(value, parameterName);

    /// <summary>
    /// Begins a guard chain for a reference-type argument; the parameter
    /// name is captured from the call site. Pair with
    /// <see cref="EnsuredObject{T}.IsNotNull"/> in place of
    /// <c>ArgumentNullException.ThrowIfNull</c>. The <c>string</c> overload
    /// above is more specific, so string arguments still flow to it.
    /// </summary>
    public static EnsuredObject<T> That<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string parameterName = "")
        where T : class =>
        new(value, parameterName);

    /// <summary>
    /// Begins a guard chain for a <see cref="Guid"/> argument; pair with
    /// <see cref="EnsuredGuid.IsNotEmpty"/> in place of a
    /// <c>value == Guid.Empty ? throw …</c> precondition (ADR-0105).
    /// A dedicated overload is needed because value types do not bind to
    /// the reference-type <c>That&lt;T&gt;</c> above.
    /// </summary>
    public static EnsuredGuid That(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string parameterName = "") =>
        new(value, parameterName);

    /// <summary>
    /// Begins a guard chain for an <see cref="int"/> argument; pair with
    /// <see cref="EnsuredValue{T}.AtLeast"/> / <see cref="EnsuredValue{T}.InRange"/>
    /// / <see cref="EnsuredValue{T}.Satisfies"/> in place of a numeric-range
    /// <c>throw new ArgumentException</c> precondition (ADR-0105).
    /// </summary>
    public static EnsuredValue<int> That(
        int value,
        [CallerArgumentExpression(nameof(value))] string parameterName = "") =>
        new(value, parameterName);
}

public readonly struct EnsuredString
{
    private readonly string _value;
    private readonly string _parameter;

    internal EnsuredString(string value, string parameter)
    {
        _value = value;
        _parameter = parameter;
    }

    public EnsuredString IsNotNull()
    {
        if (_value is null)
        {
            throw new ArgumentNullException(_parameter);
        }
        return this;
    }

    public EnsuredString IsNotNullOrWhiteSpace()
    {
        if (string.IsNullOrWhiteSpace(_value))
        {
            throw new ArgumentException($"{_parameter} must not be null or whitespace.", _parameter);
        }
        return this;
    }

    public EnsuredString HasMinLength(int minimumLength)
    {
        if (_value.Length < minimumLength)
        {
            throw new ArgumentException(
                $"{_parameter} must be at least {minimumLength} character(s).", _parameter);
        }
        return this;
    }

    public EnsuredString HasMaxLength(int maximumLength)
    {
        if (_value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{_parameter} must be no more than {maximumLength} character(s).", _parameter);
        }
        return this;
    }

    public EnsuredString StartsWith(string prefix, StringComparison comparison)
    {
        if (!_value.StartsWith(prefix, comparison))
        {
            throw new ArgumentException(
                $"{_parameter} must start with '{prefix}'.", _parameter);
        }
        return this;
    }

    public EnsuredString Matches(Regex pattern, string message)
    {
        if (!pattern.IsMatch(_value))
        {
            throw new ArgumentException($"{_parameter}: {message}", _parameter);
        }
        return this;
    }

    public EnsuredString Satisfies(Func<string, bool> predicate, string message)
    {
        if (!predicate(_value))
        {
            throw new ArgumentException($"{_parameter}: {message}", _parameter);
        }
        return this;
    }

    public string AndReturn() => _value;
}

/// <summary>
/// Guard chain for a reference-type argument (ADR-0105). <see cref="IsNotNull"/>
/// throws <see cref="ArgumentNullException"/> — matching the exception
/// <c>ArgumentNullException.ThrowIfNull</c> raised — while value/predicate
/// failures use <see cref="ArgumentException"/>, consistent with the string chain.
/// </summary>
public readonly struct EnsuredObject<T>
    where T : class
{
    private readonly T _value;
    private readonly string _parameter;

    internal EnsuredObject(T value, string parameter)
    {
        _value = value;
        _parameter = parameter;
    }

    public EnsuredObject<T> IsNotNull()
    {
        if (_value is null)
        {
            throw new ArgumentNullException(_parameter);
        }
        return this;
    }

    public T AndReturn() => _value;
}

/// <summary>
/// Guard chain for a <see cref="Guid"/> argument (ADR-0105).
/// <see cref="IsNotEmpty"/> throws <see cref="ArgumentException"/> on
/// <see cref="Guid.Empty"/> — the value-type analogue of
/// <see cref="EnsuredObject{T}.IsNotNull"/>.
/// </summary>
public readonly struct EnsuredGuid
{
    private readonly Guid _value;
    private readonly string _parameter;

    internal EnsuredGuid(Guid value, string parameter)
    {
        _value = value;
        _parameter = parameter;
    }

    public EnsuredGuid IsNotEmpty()
    {
        if (_value == Guid.Empty)
        {
            throw new ArgumentException($"{_parameter} must not be empty.", _parameter);
        }
        return this;
    }

    public Guid AndReturn() => _value;
}

/// <summary>
/// Guard chain for a comparable value-type argument (ADR-0105). Failures
/// throw <see cref="ArgumentException"/>, consistent with the string and
/// object chains.
/// </summary>
public readonly struct EnsuredValue<T>
    where T : struct, IComparable<T>
{
    private readonly T _value;
    private readonly string _parameter;

    internal EnsuredValue(T value, string parameter)
    {
        _value = value;
        _parameter = parameter;
    }

    public EnsuredValue<T> AtLeast(T minimum)
    {
        if (_value.CompareTo(minimum) < 0)
        {
            throw new ArgumentException($"{_parameter} must be >= {minimum}; got {_value}.", _parameter);
        }
        return this;
    }

    public EnsuredValue<T> InRange(T minimum, T maximum)
    {
        if (_value.CompareTo(minimum) < 0 || _value.CompareTo(maximum) > 0)
        {
            throw new ArgumentException(
                $"{_parameter} must be in [{minimum}, {maximum}]; got {_value}.", _parameter);
        }
        return this;
    }

    public EnsuredValue<T> Satisfies(Func<T, bool> predicate, string message)
    {
        Ensure.That(predicate).IsNotNull();
        if (!predicate(_value))
        {
            throw new ArgumentException($"{_parameter}: {message}", _parameter);
        }
        return this;
    }

    public T AndReturn() => _value;
}
