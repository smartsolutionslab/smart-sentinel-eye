using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public sealed record ListWebhookIntegrationsQuery(bool IncludeRevoked)
    : IQuery<Result<IReadOnlyList<WebhookIntegrationDto>, ListWebhookIntegrationsError>>;

public abstract record ListWebhookIntegrationsError(string Code, string Message, System.Net.HttpStatusCode Status)
    : ApiError(Code, Message, Status);
