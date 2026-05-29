using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using DomainResourceKind = SmartSentinelEye.AuditObservability.Domain.AuditEvent.ResourceKind;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

public sealed partial class V1ResourceMap
{
    /// <summary>
    /// Hand-tweak hooks for the convention scanner in
    /// <see cref="V1ResourceMap.BuildDefault"/>. Keep this file as
    /// the only place that knows about per-V1 specifics; the
    /// scanner reads everything else from convention.
    /// </summary>
    internal static class Conventions
    {
        /// <summary>
        /// Namespace tail → canonical <see cref="ResourceKind"/>.
        /// Add a new entry whenever a context's V1 namespace doesn't
        /// match the resource vocabulary by name alone.
        /// </summary>
        internal static IReadOnlyDictionary<string, DomainResourceKind> NamespaceToResource { get; }
            = new Dictionary<string, DomainResourceKind>(StringComparer.Ordinal)
            {
                ["CameraCatalog"] = DomainResourceKind.Camera,
                ["StreamDistribution"] = DomainResourceKind.Stream,
                ["LayoutComposition"] = DomainResourceKind.Layout,
                ["OverlayDesigner"] = DomainResourceKind.Overlay,
                ["SystemVariables"] = DomainResourceKind.Variable,
                ["Automation"] = DomainResourceKind.Rule,
                ["EventIngestion"] = DomainResourceKind.Event,
                ["Identity"] = DomainResourceKind.Device,
                ["AuditObservability"] = DomainResourceKind.Event,
            };

        /// <summary>
        /// V1s whose resource shape doesn't match the convention.
        /// Resolved before <see cref="NamespaceToResource"/>.
        /// </summary>
        internal static IReadOnlyDictionary<Type, V1MappingEntry> HandTweaks { get; }
            = BuildHandTweaks();

        /// <summary>
        /// V1s deliberately left without a resource pivot. The
        /// architecture test
        /// <c>V1ResourceMap_covers_every_IIntegrationEvent</c>
        /// (spec 009 T070) treats this list as known-good gaps.
        /// </summary>
        internal static IReadOnlyCollection<string> OptOuts { get; } = [];

        private static Dictionary<Type, V1MappingEntry> BuildHandTweaks()
        {
            Dictionary<Type, V1MappingEntry> map = new();

            // Identity contracts split across two resource kinds depending on
            // which client persona the event covers (devices vs kiosks vs
            // webhook integrations).
            Type? deviceRegistered = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.Identity.DeviceRegisteredV1, SmartSentinelEye.Shared.Contracts");
            if (deviceRegistered is not null)
            {
                map[deviceRegistered] = new V1MappingEntry(
                    DomainResourceKind.Device,
                    PickByProperty(deviceRegistered, "ClientId"));
            }

            Type? kioskEnrolled = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.Identity.KioskEnrolledV1, SmartSentinelEye.Shared.Contracts");
            if (kioskEnrolled is not null)
            {
                map[kioskEnrolled] = new V1MappingEntry(
                    DomainResourceKind.Kiosk,
                    PickByProperty(kioskEnrolled, "ClientId"));
            }

            Type? webhookRotated = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.Identity.WebhookIntegrationRotatedV1, SmartSentinelEye.Shared.Contracts");
            if (webhookRotated is not null)
            {
                map[webhookRotated] = new V1MappingEntry(
                    DomainResourceKind.WebhookIntegration,
                    PickByProperty(webhookRotated, "IntegrationName"));
            }

            // Spec 006 webhook contracts are emitted from EventIngestion but
            // pivot on a webhook integration name.
            Type? webhookRegistered = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRegisteredV1, SmartSentinelEye.Shared.Contracts");
            if (webhookRegistered is not null)
            {
                map[webhookRegistered] = new V1MappingEntry(
                    DomainResourceKind.Webhook,
                    PickByProperty(webhookRegistered, "Name"));
            }

            Type? webhookRevoked = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.EventIngestion.WebhookIntegrationRevokedV1, SmartSentinelEye.Shared.Contracts");
            if (webhookRevoked is not null)
            {
                map[webhookRevoked] = new V1MappingEntry(
                    DomainResourceKind.Webhook,
                    PickByProperty(webhookRevoked, "Name"));
            }

            // AuditChunkArchivedV1 (spec 009 itself) pivots on the chunk id.
            Type? chunkArchived = Type.GetType(
                "SmartSentinelEye.Shared.Contracts.AuditObservability.AuditChunkArchivedV1, SmartSentinelEye.Shared.Contracts");
            if (chunkArchived is not null)
            {
                map[chunkArchived] = new V1MappingEntry(
                    DomainResourceKind.Event,
                    PickByProperty(chunkArchived, "ChunkIdentifier"));
            }

            return map;
        }

        private static Func<object, ResourceIdentifier?> PickByProperty(Type type, string propertyName)
        {
            System.Reflection.PropertyInfo? prop = type.GetProperty(propertyName);
            if (prop is null) return _ => null;
            return instance =>
            {
                object? raw = prop.GetValue(instance);
                return raw is null ? null : ResourceIdentifier.From(raw.ToString()!);
            };
        }
    }
}
