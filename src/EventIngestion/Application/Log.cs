using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Application;

/// <summary>
/// Source-generated log methods for the EventIngestion application layer
/// (ADR-0050). <c>[LoggerMessage]</c> compiles each template into a
/// strongly-typed, allocation-free call that skips all work when the
/// level is disabled — this matters on the ingest hot path
/// (<see cref="EventHandlers.EventIngestedDomainEventHandler"/> fires per
/// ingested event), where the previous <c>logger.LogDebug(template, …)</c>
/// boxed the value-object arguments and parsed the template on every call.
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Idempotent re-delivery of {Identifier} for fab {Fab}; no-op.")]
    public static partial void IdempotentReDelivery(ILogger logger, EventIdentifier identifier, FabIdentifier fab);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Published FabEventIngestedV1 for {Identifier} ({Source}/{Device}).")]
    public static partial void PublishedIntegrationEvent(
        ILogger logger, EventIdentifier identifier, Source source, DeviceIdentifier device);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Registered webhook integration '{Name}' ({Identifier}).")]
    public static partial void WebhookIntegrationRegistered(
        ILogger logger, WebhookIntegrationName name, WebhookIntegrationIdentifier identifier);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Revoked webhook integration '{Name}' ({Identifier}).")]
    public static partial void WebhookIntegrationRevoked(
        ILogger logger, WebhookIntegrationName name, WebhookIntegrationIdentifier identifier);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Ignoring WebhookIntegrationRotatedV1 with invalid name '{Name}'.")]
    public static partial void InvalidRotationName(ILogger logger, Exception exception, string name);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Webhook integration '{Name}' not present; rotation event ignored.")]
    public static partial void RotationTargetMissing(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Flipped webhook integration '{Name}' to JWT validation backed by Keycloak client '{ClientId}'.")]
    public static partial void WebhookIntegrationFlippedToJwt(ILogger logger, string name, string clientId);
}
