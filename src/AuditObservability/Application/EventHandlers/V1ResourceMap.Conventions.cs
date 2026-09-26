using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.EventIngestion;
using SmartSentinelEye.Shared.Contracts.Identity;
using SmartSentinelEye.Shared.Contracts.LayoutComposition;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
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
            Dictionary<Type, V1MappingEntry> map = [];

            // Identity contracts split across two resource kinds depending on
            // which client persona the event covers (devices vs kiosks vs
            // webhook integrations). WebhookIntegrationRevokedV1 is published
            // by EventIngestion, not Identity, but pivots on the same
            // WebhookIntegration resource as the rotation event above
            // (spec 264, #2206), so the convention's namespace-to-resource
            // default (EventIngestion → Event) would be wrong for it too.
            Add<DeviceRegisteredV1>(map, DomainResourceKind.Device, registered => registered.ClientId);
            Add<KioskEnrolledV1>(map, DomainResourceKind.Kiosk, enrolled => enrolled.ClientId);
            Add<WebhookIntegrationRotatedV1>(map, DomainResourceKind.WebhookIntegration, rotated => rotated.IntegrationName);
            Add<WebhookIntegrationRevokedV1>(map, DomainResourceKind.WebhookIntegration, revoked => revoked.IntegrationName);

            // Spec 005: emitted from SystemVariables but pivots on the overlay
            // whose resolved text changed, not on a variable.
            Add<ResolvedOverlayTextChangedV1>(map, DomainResourceKind.Overlay, changed => changed.Overlay);

            // AuditChunkArchivedV1 (spec 009 itself) pivots on the chunk id.
            Add<AuditChunkArchivedV1>(map, DomainResourceKind.Event, archived => archived.ChunkIdentifier);

            // Published into LayoutComposition (spec 020's Automation rule
            // targets a layout's overlay), but the subject is the overlay,
            // not the layout the namespace convention would pick.
            Add<OverlayHighlightRequestedV1>(map, DomainResourceKind.Overlay, requested => requested.OverlayIdentifier);

            // The contract's only Guid is the triggering event's id, not the
            // variable's; SystemVariables addresses every variable by name.
            Add<SystemVariableValueRequestedV1>(map, DomainResourceKind.Variable, requested => requested.Name);

            return map;
        }

        private static void Add<TEvent>(
            Dictionary<Type, V1MappingEntry> map,
            DomainResourceKind kind,
            Func<TEvent, object?> pick)
            where TEvent : IIntegrationEvent
            => map[typeof(TEvent)] = new V1MappingEntry(
                kind,
                instance => Identify(pick((TEvent)instance)));

        private static ResourceIdentifier? Identify(object? raw) =>
            raw is null ? null : ResourceIdentifier.From(raw.ToString()!);
    }
}
