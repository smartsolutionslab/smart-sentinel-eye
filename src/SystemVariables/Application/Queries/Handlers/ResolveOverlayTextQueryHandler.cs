using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

/// <summary>
/// <c>GET /system-variables/resolve</c>'s handler (spec 148 US1). Calls
/// <see cref="IVariableSnapshotBuilder.BuildAsync"/> once and uses the
/// result twice: projected into the dictionary shape
/// <see cref="IResolver.Resolve"/> takes, and mapped onto one
/// <see cref="PlaceholderResolutionDto"/> per name — <c>RenderedValue</c>
/// from <c>VariableValue.Render(...)</c>, never <c>ToWireString()</c>
/// (spec 148 US1 scenarios 2-3).
/// </summary>
public sealed class ResolveOverlayTextQueryHandler(IVariableSnapshotBuilder builder, IResolver resolver)
    : IQueryHandler<ResolveOverlayTextQuery, Result<ResolvedTextPreviewDto, ResolveOverlayTextError>>
{
    public async Task<Result<ResolvedTextPreviewDto, ResolveOverlayTextError>> HandleAsync(
        ResolveOverlayTextQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (fabs, text) = query;

        IReadOnlyList<PlaceholderResolution> resolutions = await builder.BuildAsync(fabs, text, cancellationToken);

        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach (PlaceholderResolution resolution in resolutions)
        {
            if (resolution is { Outcome: PlaceholderOutcome.Resolved, Entry: { } entry })
            {
                snapshot[resolution.Name] = entry;
            }
        }

        string resolvedText = resolver.Resolve(text, snapshot);

        List<PlaceholderResolutionDto> placeholders = [.. resolutions.Select(ToDto)];

        return Result<ResolvedTextPreviewDto, ResolveOverlayTextError>.Success(
            new ResolvedTextPreviewDto(resolvedText, placeholders));
    }

    private static PlaceholderResolutionDto ToDto(PlaceholderResolution resolution)
    {
        string? renderedValue = resolution is { Outcome: PlaceholderOutcome.Resolved, Entry: { } entry }
            ? entry.Value.Render(entry.BooleanLabels ?? BooleanLabels.Default)
            : null;

        return new PlaceholderResolutionDto(
            resolution.Name,
            resolution.Outcome.ToString(),
            resolution.Fab?.Value,
            renderedValue);
    }
}
