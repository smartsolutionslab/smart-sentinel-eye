using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

/// <summary>
/// Lists Layout chains. With <c>State == Published</c> the result is
/// the kiosk picker (one entry per chain that has a Published revision,
/// projected as <see cref="PublishedLayoutDto"/>). With no filter the
/// management UI gets every chain projected as <see cref="LayoutDto"/>.
/// The two shapes are returned via the union-style envelope rather than
/// overloaded queries.
/// </summary>
/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (spec 017 FR-005). A list spans
/// all of them when none is named — the deliberate asymmetry with the write
/// path, which must choose. A listing that refused a multi-fab operator would
/// be unusable for exactly the people it exists for.
/// </summary>
public sealed record ListLayoutsQuery(IReadOnlyList<FabIdentifier> Fabs, LayoutRevisionState? State)
    : IQuery<Result<ListLayoutsResult, ListLayoutsError>>;

/// <summary>
/// Discriminated envelope so the API endpoint can map the two shapes
/// without a type-cast at the call site.
/// </summary>
public sealed record ListLayoutsResult(
    IReadOnlyList<LayoutDto> Chains,
    IReadOnlyList<PublishedLayoutDto> Published);
