using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// AuditObservability's own copy of the fab identifier (spec
/// 009). We don't share VOs across contexts per ADR-0044, so this
/// mirrors the same shape every other context's <c>FabIdentifier</c>
/// uses. Optional on an audit row because some cross-cutting V1s
/// (e.g. <see cref="Shared.Contracts.AuditObservability.AuditChunkArchivedV1"/>
/// with no <c>FabId</c>) are not fab-scoped.
/// </summary>
public sealed record FabIdentifier : StringValueObject
{
    public const int MinimumLength = 2;
    public const int MaximumLength = 32;

    private FabIdentifier(string value) : base(value) { }

    public static FabIdentifier From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMinLength(MinimumLength)
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must be lowercase letters, digits, or '-' and start with a letter")
            .AndReturn();
        return new FabIdentifier(validated);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsAsciiLetterLower(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-') return false;
        }
        return true;
    }
}
