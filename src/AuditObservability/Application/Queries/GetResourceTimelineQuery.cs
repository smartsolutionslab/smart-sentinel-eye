using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

/// <summary>
/// Per-resource timeline (spec 009 FR-009). <see cref="Fab"/> is
/// mandatory at this endpoint — the caller-side fab guard verifies
/// membership before the handler runs. Returns rows ascending by
/// <c>occurred_at</c> so the timeline reads chronologically.
/// </summary>
public sealed record GetResourceTimelineQuery(
    string ResourceKind,
    ResourceIdentifier ResourceIdentifier,
    FabIdentifier Fab,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    int PageSize,
    string? Cursor)
    : IQuery<Result<AuditPageDto, GetResourceTimelineError>>;
