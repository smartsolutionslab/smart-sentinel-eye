using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Queries;

/// <summary>
/// Cross-cutting audit search (spec 009 FR-008).
///
/// <para>
/// When <see cref="Fab"/> is set, the caller-side fab-guard has
/// already verified group membership and the handler restricts
/// the result to that fab. When it is null, the handler falls
/// back to <see cref="CallerFabs"/> — the set of <c>/fabs/&lt;id&gt;</c>
/// memberships the JWT carries — to scope the result.
/// </para>
/// </summary>
public sealed record SearchAuditQuery(
    string? Fab,
    IReadOnlyList<string> CallerFabs,
    Guid? Actor,
    string? ActorUsername,
    string? EventKind,
    string? ResourceKind,
    string? ResourceIdentifier,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    int PageSize,
    string? Cursor)
    : IQuery<Result<AuditPageDto, SearchAuditError>>;
