namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "an overlay revision became Archived" SignalR frames.
/// </summary>
public sealed record OverlayRevisionArchivedHubMessage(Guid Overlay, int RevisionNumber, DateTimeOffset ArchivedAt);
