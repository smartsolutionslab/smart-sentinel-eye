using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// String identifier of the aggregate the audited <c>*V1</c>
/// touched (spec 009 FR-004 / FR-009). Carries a Guid v7 string
/// for most V1s; carries a business name where the V1 uses one
/// (e.g. <c>RuleArchivedV1.Name</c>).
/// </summary>
public sealed record ResourceIdentifier : StringValueObject
{
    public const int MaximumLength = 255;

    private ResourceIdentifier(string value) : base(value) { }

    public static ResourceIdentifier From(string value)
    {
        string validated = Ensure.That(value, nameof(value))
            .IsNotNullOrWhiteSpace()
            .HasMaxLength(MaximumLength)
            .AndReturn();
        return new ResourceIdentifier(validated);
    }
}
