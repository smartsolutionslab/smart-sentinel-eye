using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// A caller-supplied <c>Idempotency-Key</c> (ADR-0142), validated at the trust
/// boundary before it reaches a database.
///
/// <para>
/// The charset and length are deliberately narrow. This value arrives from
/// outside, is stored, and is compared — so it is exactly the kind of string
/// that should not be allowed to be unbounded or to carry whitespace and
/// control characters into a primary key. A caller wanting structure can use a
/// GUID, which fits comfortably.
/// </para>
/// </summary>
public sealed class IdempotencyKey : IValueObject<string>, IEquatable<IdempotencyKey>
{
    /// <summary>Long enough for a GUID with separators, short enough to index.</summary>
    public const int MaxLength = 128;

    private IdempotencyKey(string value) => Value = value;

    public string Value { get; }

    public static IdempotencyKey From(string value)
    {
        Ensure.That(value).IsNotNull().IsNotNullOrWhiteSpace();

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new ArgumentException(
                $"An idempotency key may be at most {MaxLength} characters; this one is {trimmed.Length}.",
                nameof(value));
        }

        char[] offending = [.. trimmed.Where(character => !IsPermitted(character))];

        if (offending.Length > 0)
        {
            throw new ArgumentException(
                $"An idempotency key may contain letters, digits, '-', '_' and ':' only; found '{offending[0]}'.",
                nameof(value));
        }

        return new IdempotencyKey(trimmed);
    }

    private static bool IsPermitted(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or ':';

    public bool Equals(IdempotencyKey? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as IdempotencyKey);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;
}
