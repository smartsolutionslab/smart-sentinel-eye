using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

/// <summary>
/// Resolves arbitrary label text against the caller's fabs (spec 148 US1).
/// Unlike <see cref="GetOverlaySnapshotQuery"/>, the text is an input rather
/// than looked up from the reverse index — there is no overlay to be
/// unpublished or unknown, so there is no 404 here (plan.md "The endpoint").
/// </summary>
public sealed record ResolveOverlayTextQuery(
    IReadOnlyList<FabIdentifier> Fabs,
    string Text)
    : IQuery<Result<ResolvedTextPreviewDto, ResolveOverlayTextError>>;
