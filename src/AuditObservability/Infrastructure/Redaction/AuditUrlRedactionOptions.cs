namespace SmartSentinelEye.AuditObservability.Infrastructure.Redaction;

/// <summary>Whether the URL redaction step applies its rewrite (spec 449); bound from <see cref="SectionName"/>.</summary>
public sealed class AuditUrlRedactionOptions
{
    public const string SectionName = "AuditObservability:UrlRedaction";

    public bool Apply { get; set; }
}
