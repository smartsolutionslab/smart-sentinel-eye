using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.Event;

/// <summary>
/// Per-source identifier for the originating device (spec 006
/// FR-004). PLC station name, camera id, kiosk camera id, or
/// webhook integration name. Grammar:
/// <c>^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$</c> (no whitespace).
/// </summary>
public sealed record DeviceIdentifier : StringValueObject
{
    public const int MaximumLength = 64;

    private DeviceIdentifier(string value) : base(value) { }

    public static DeviceIdentifier From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must be alphanumeric or '.', '_', '-' and start with a letter or digit")
            .AndReturn();
        return new DeviceIdentifier(validated);
    }

    private static bool IsValid(string s)
    {
        if (!char.IsLetterOrDigit(s[0])) return false;
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-') return false;
        }
        return true;
    }
}
