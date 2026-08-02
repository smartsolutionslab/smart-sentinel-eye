using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Lists registered <see cref="ClientKind.WebhookIntegration"/> clients,
/// optionally filtered to a single fab.
///
/// <para>
/// Exists because rotation requires an <c>If-Match</c> (ADR-0113) and this
/// is the only durable source for the version to put in it. Without it the
/// rotation response would be the sole carrier, so an admin who no longer
/// had that response could not rotate again — which is precisely when they
/// need to, a credential having leaked.
/// </para>
/// </summary>
public sealed record ListWebhookClientsQuery(Option<FabIdentifier> Fab)
    : IQuery<Result<IReadOnlyList<RegisteredClientSummaryDto>, ListClientsError>>;
