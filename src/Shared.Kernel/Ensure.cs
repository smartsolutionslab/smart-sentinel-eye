using System.Text.RegularExpressions;

namespace SmartSentinelEye.Shared.Kernel;

/// <summary>
/// Fluent validation chain for value objects per ADR-0059. Throws
/// ArgumentException on the first failed predicate; concrete VO factories
/// catch and translate to Result.Failure when needed.
/// </summary>
public static class Ensure
{
    public static EnsuredString That(string value, string parameterName = "value") =>
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
