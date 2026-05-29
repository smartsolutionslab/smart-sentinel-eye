namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event raised by Automation (spec 007) requesting
/// LayoutComposition (spec 003/005) to push an
/// <c>OverlayHighlightChanged</c> SignalR frame to every kiosk
/// rendering the affected overlay. The kiosk applies the
/// <c>ssE-overlay-highlight</c> CSS class for
/// <paramref name="DurationMs"/> milliseconds, then auto-reverts.
///
/// <para>
/// Two highlight events on the same overlay with overlapping
/// durations are OR'd by the kiosk (the highlight class survives
/// until the later expiry). <paramref name="CausingEventIdentifier"/>
/// is the <c>FabEventIngestedV1.EventIdentifier</c> that triggered
/// the rule, carried for audit / replay correlation.
/// </para>
/// </summary>
public sealed record OverlayHighlightRequestedV1(
    Guid OverlayIdentifier,
    int DurationMs,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent;
