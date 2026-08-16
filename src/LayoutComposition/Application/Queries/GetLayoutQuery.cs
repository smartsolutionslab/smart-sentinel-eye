using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 017 FR-005). A layout
/// outside them is reported as not found rather than forbidden: the caller
/// addressed a layout, so "forbidden" would confirm it exists (FR-006).
/// </summary>
public sealed record GetLayoutQuery(IReadOnlyList<FabIdentifier> Fabs, LayoutIdentifier Layout)
    : IQuery<Result<LayoutDto, GetLayoutError>>;
