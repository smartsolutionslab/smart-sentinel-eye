using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Lists enrolled <see cref="ClientKind.Kiosk"/> clients (issue #827),
/// optionally filtered to a single fab. Disabled rows are included so the
/// management UI can show the full enrollment history.
/// </summary>
public sealed record ListKiosksQuery(Option<FabIdentifier> Fab)
    : IQuery<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>;
