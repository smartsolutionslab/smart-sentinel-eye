using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

/// <summary>
/// <c>GET /system-variables/-/resolve</c>'s handler (spec 148 US1). Calls
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

        var resolutions = await builder.BuildAsync(fabs, text, cancellationToken);

        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach (var resolution in resolutions)
        {
            if (resolution is { Outcome: PlaceholderOutcome.Resolved, Entry: { } entry })
            {
                snapshot[resolution.Name] = entry;
            }
        }

        var resolvedText = resolver.Resolve(text, snapshot);

        return Result<ResolvedTextPreviewDto, ResolveOverlayTextError>.Success(
            resolutions.ToResolvedTextPreviewDto(resolvedText));
    }
}
