using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.SystemVariables.Domain.Variable;

/// <summary>
/// Stable, human-readable name of a system variable
/// (spec 005 FR-001). Grammar: <c>^[A-Za-z][A-Za-z0-9_]{0,63}$</c> —
/// must start with a letter, alphanumeric or underscore thereafter,
/// 1-64 chars total, case-sensitive (MES/SCADA convention).
/// </summary>
public sealed record VariableName : StringValueObject
{
    public const int MaximumLength = 64;

    private VariableName(string value) : base(value) { }

    public static VariableName From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValidIdentifier,
                "must start with a letter and contain only letters, digits, and underscores")
            .AndReturn();
        return new VariableName(validated);
    }

    private static bool IsValidIdentifier(string s)
    {
        if (s.Length == 0 || s.Length > MaximumLength) return false;
        if (!char.IsLetter(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }
}
