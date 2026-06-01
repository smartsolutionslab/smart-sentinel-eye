namespace SmartSentinelEye.LayoutComposition.Infrastructure.Broadcasting;

/// <summary>
/// Typed SignalR client interface (server-to-client methods). Concrete
/// names map directly to method names on the JS-side ``HubConnection``.
/// </summary>
public interface ILayoutLifecycleClient
{
    Task LayoutRevisionPublished(LayoutRevisionPublishedHubMessage message);

    Task LayoutRevisionArchived(LayoutRevisionArchivedHubMessage message);

    Task OverlayRevisionPublished(OverlayRevisionPublishedHubMessage message);

    Task OverlayRevisionArchived(OverlayRevisionArchivedHubMessage message);

    Task ResolvedOverlayTextChanged(ResolvedOverlayTextChangedHubMessage message);

    Task OverlayHighlightChanged(OverlayHighlightChangedHubMessage message);
}
