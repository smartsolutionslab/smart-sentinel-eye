using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Domain.RegisteredClient.Events;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

/// <summary>
/// Translates the in-process <see cref="ClientRegisteredDomainEvent"/>
/// into the matching V1 integration event based on
/// <see cref="ClientKind"/>:
///   - <c>Device</c>  → <see cref="DeviceRegisteredV1"/>.
///   - <c>Kiosk</c>   → <see cref="KioskEnrolledV1"/>.
///   - <c>WebhookIntegration</c> → no fan-out here; the
///     <see cref="Commands.Handlers.RotateWebhookClientCommandHandler"/>
///     publishes <c>WebhookIntegrationRotatedV1</c> directly.
///
/// <para>
/// Device V1 carries the <c>(deviceType, deviceIdentifier)</c>
/// pair derived from the Keycloak client_id string
/// (<c>&lt;deviceType&gt;-&lt;deviceIdentifier&gt;</c>).
/// </para>
/// </summary>
public sealed class ClientRegisteredDomainEventHandler(
    IEventBus events,
    ILogger<ClientRegisteredDomainEventHandler> logger)
    : IDomainEventHandler<ClientRegisteredDomainEvent>
{
    public async Task Handle(ClientRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (domainEvent.Kind == ClientKind.Device)
        {
            (string deviceType, string deviceIdentifier) = SplitDeviceClientId(domainEvent.ClientId.Value);
            await events.PublishAsync(
                new DeviceRegisteredV1(
                    RegisteredClientIdentifier: domainEvent.Client.Value,
                    ClientId: domainEvent.ClientId.Value,
                    DeviceType: deviceType,
                    DeviceIdentifier: deviceIdentifier,
                    Fab: domainEvent.Fab.Value,
                    RegisteredAt: domainEvent.RegisteredAt,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.RegisteredAt, domainEvent.Fab.Value, null)),
                cancellationToken).ConfigureAwait(false);
            Log.PublishedDeviceRegisteredV1(logger, domainEvent.ClientId);
            return;
        }

        if (domainEvent.Kind == ClientKind.Kiosk)
        {
            await events.PublishAsync(
                new KioskEnrolledV1(
                    RegisteredClientIdentifier: domainEvent.Client.Value,
                    ClientId: domainEvent.ClientId.Value,
                    Fab: domainEvent.Fab.Value,
                    EnrolledAt: domainEvent.RegisteredAt,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.RegisteredAt, domainEvent.Fab.Value, null)),
                cancellationToken).ConfigureAwait(false);
            Log.PublishedKioskEnrolledV1(logger, domainEvent.ClientId);
        }

        // WebhookIntegration: no fan-out here — the rotate-command-
        // handler publishes WebhookIntegrationRotatedV1 directly
        // because it has the integration name in scope.
    }

    private static (string DeviceType, string DeviceIdentifier) SplitDeviceClientId(string clientId)
    {
        int sep = clientId.IndexOf('-', StringComparison.Ordinal);
        if (sep <= 0 || sep == clientId.Length - 1)
        {
            return (clientId, string.Empty);
        }
        return (clientId[..sep], clientId[(sep + 1)..]);
    }
}
