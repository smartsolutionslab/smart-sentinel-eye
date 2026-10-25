using SmartSentinelEye.OverlayDesigner.Domain.Overlay;

namespace SmartSentinelEye.OverlayDesigner.Application.DTOs;

internal static class OverlayMappings
{
    public static OverlayDto ToOverlayDto(this Overlay overlay) =>
        new(
            OverlayIdentifier: overlay.Id.Value,
            Version: overlay.Version,
            Name: overlay.Name.Value,
            CreatedAt: overlay.Creation.At,
            CreatedBy: overlay.Creation.By.Value,
            Revisions: overlay.Revisions
                .OrderBy(revision => revision.Number.Value)
                .Select(ToDto)
                .ToList());

    public static OverlayRevisionDto ToDto(this Revision revision) =>
        new(
            RevisionIdentifier: revision.Id.Value,
            RevisionNumber: revision.Number.Value,
            State: revision.State.Value,
            Elements: [.. revision.Elements.Select(ToDto)],
            CreatedAt: revision.Creation.At,
            CreatedBy: revision.Creation.By.Value,
            PublishedAt: revision.PublishedAt?.Value,
            ArchivedAt: revision.ArchivedAt?.Value);

    public static OverlayElementDto ToDto(this OverlayElement element) =>
        new(
            Kind: element.Kind.Value,
            Color: element.Color.Value,
            NormalizedX: element.Position.X,
            NormalizedY: element.Position.Y,
            NormalizedWidth: element.Size.Width,
            NormalizedHeight: element.Size.Height,
            Text: element.Text?.Value,
            FontSizePx: element.Text?.FontSizePx);

    public static PublishedOverlayDto ToPublishedOverlayDto(this Overlay overlay)
    {
        var pub = overlay.Revisions.Single(revision => revision.State == OverlayRevisionState.Published);
        return new PublishedOverlayDto(
            OverlayIdentifier: overlay.Id.Value,
            Name: overlay.Name.Value,
            RevisionNumber: pub.Number.Value,
            Elements: [.. pub.Elements.Select(ToDto)],
            PublishedAt: pub.PublishedAt!.Value);
    }
}
