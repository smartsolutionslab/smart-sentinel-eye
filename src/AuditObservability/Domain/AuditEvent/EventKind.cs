using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// The <c>*V1</c> contract's CLR type name (e.g.
/// <c>"CameraRegisteredV1"</c>, <c>"RuleArchivedV1"</c>). The
/// shape mirrors a C# identifier so the audit query API can
/// safely use it in URL paths + `eventKind` filters without
/// extra escaping.
/// </summary>
public sealed record EventKind : StringValueObject
{
    public const int MaximumLength = 100;

    private EventKind(string value) : base(value) { }

    public static EventKind From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must start with a letter and contain only letters or digits")
            .AndReturn();
        return new EventKind(validated);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsLetter(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!char.IsLetterOrDigit(s[i])) return false;
        }
        return true;
    }
}
