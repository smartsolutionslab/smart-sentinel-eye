namespace SmartSentinelEye.AuditObservability.Infrastructure.Redaction;

/// <summary>Phase 4a scaffold (spec 449); bound from <see cref="SectionName"/>.</summary>
public sealed class AuditUrlRedactionOptions
{
    public const string SectionName = "AuditObservability:UrlRedaction";

    public bool Apply { get; set; }
}
