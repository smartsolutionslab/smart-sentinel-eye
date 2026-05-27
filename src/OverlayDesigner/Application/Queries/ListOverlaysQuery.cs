using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

/// <summary>
/// Lists Overlay chains. With <c>State == Published</c> the result is
/// the management-web picker for binding an overlay onto a layout
/// (one entry per chain that has a Published revision, projected as
/// <see cref="PublishedOverlayDto"/>). With no filter the admin UI gets
/// every chain projected as <see cref="OverlayDto"/>. Mirrors the
/// LayoutComposition envelope shape.
/// </summary>
public sealed record ListOverlaysQuery(OverlayRevisionState? State)
    : IQuery<Result<ListOverlaysResult, ListOverlaysError>>;

public sealed record ListOverlaysResult(
    IReadOnlyList<OverlayDto> Chains,
    IReadOnlyList<PublishedOverlayDto> Published);
