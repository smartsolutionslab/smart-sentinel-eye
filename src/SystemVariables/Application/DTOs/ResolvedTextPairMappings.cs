namespace SmartSentinelEye.SystemVariables.Application.DTOs;

internal static class ResolvedTextPairMappings
{
    public static ResolvedOverlayTextDto ToDto(this (string Template, string Resolved) pair) =>
        new(pair.Template, pair.Resolved);

    public static ResolvedOverlaySnapshotDto ToResolvedOverlaySnapshotDto(
        this IReadOnlyList<(string Template, string Resolved)> pairs,
        Guid overlayIdentifier,
        long version)
    {
        IReadOnlyList<ResolvedOverlayTextDto> texts = [.. pairs.Select(ToDto)];

        return new ResolvedOverlaySnapshotDto(overlayIdentifier, texts, version);
    }
}
