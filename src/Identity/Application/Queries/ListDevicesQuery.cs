using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Lists registered <see cref="ClientKind.Device"/> clients (issue #826),
/// optionally filtered to a single fab. Disabled rows are included so the
/// management UI can show the full audit history; callers that only want
/// active devices filter client-side on <c>DisabledAt</c>.
/// </summary>
public sealed record ListDevicesQuery(Option<FabIdentifier> Fab)
    : IQuery<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>;
