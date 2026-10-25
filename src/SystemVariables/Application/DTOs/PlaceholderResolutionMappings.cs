using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.DTOs;

internal static class PlaceholderResolutionMappings
{
    public static PlaceholderResolutionDto ToDto(this PlaceholderResolution resolution)
    {
        var renderedValue = resolution is { Outcome: PlaceholderOutcome.Resolved, Entry: { } entry }
            ? entry.Value.Render(entry.BooleanLabels ?? BooleanLabels.Default)
            : null;

        return new PlaceholderResolutionDto(
            resolution.Name,
            resolution.Outcome.ToString(),
            resolution.Fab?.Value,
            renderedValue);
    }

    public static ResolvedTextPreviewDto ToResolvedTextPreviewDto(
        this IEnumerable<PlaceholderResolution> resolutions,
        string resolvedText)
    {
        List<PlaceholderResolutionDto> placeholders = [.. resolutions.Select(ToDto)];

        return new ResolvedTextPreviewDto(resolvedText, placeholders);
    }
}
