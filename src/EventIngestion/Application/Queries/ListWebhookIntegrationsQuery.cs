using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

/// <summary>
/// <c>Fabs</c> is the fabs the caller holds (#1545). An integration in another
/// plant does not appear — otherwise one plant could read another's integration
/// names, and read the version it needs to revoke them with.
/// </summary>
public sealed record ListWebhookIntegrationsQuery(
    IReadOnlyList<FabIdentifier> Fabs, bool IncludeRevoked)
    : IQuery<Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError>>;

public abstract record ListWebhookIntegrationsError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
