using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

/// <summary>
/// Stable, URL-safe identifier for a webhook integration (spec 006
/// FR-023). Used as the path segment in
/// <c>POST /events/webhook/{integrationName}</c>. Grammar:
/// <c>^[a-z][a-z0-9-]{0,62}$</c>.
/// </summary>
public sealed record WebhookIntegrationName : StringValueObject
{
    public const int MaximumLength = 63;

    private WebhookIntegrationName(string value) : base(value) { }

    public static WebhookIntegrationName From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .Satisfies(IsValid,
                "must start with a lowercase letter and contain only lowercase letters, digits, or '-'")
            .AndReturn();
        return new WebhookIntegrationName(validated);
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
