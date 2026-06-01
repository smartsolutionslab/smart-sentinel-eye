namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Wire shape for "a revision became Archived" SignalR frames.
/// </summary>
public sealed record LayoutRevisionArchivedHubMessage(Guid Layout, int RevisionNumber, DateTimeOffset ArchivedAt);
