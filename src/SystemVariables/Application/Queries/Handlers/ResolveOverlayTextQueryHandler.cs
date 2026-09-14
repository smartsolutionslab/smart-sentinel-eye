using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.Queries.Handlers;

/// <summary>
/// <b>Phase 4a scaffold (spec 148).</b> Exists so
/// <c>ResolveOverlayTextQueryHandlerTests</c> compiles and is observed red
/// before T004/T005 implement it. Per plan.md "The extraction", this handler
/// calls <see cref="IVariableSnapshotBuilder.BuildAsync"/> once and uses the
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

        _ = builder;
        _ = resolver;
        await Task.CompletedTask;

        throw new NotImplementedException(
            $"spec 148 T004/T005/T006 (plan.md \"The endpoint\"): resolve '{text}' against "
            + $"{fabs.Count} fab(s) via {nameof(IVariableSnapshotBuilder)}.{nameof(IVariableSnapshotBuilder.BuildAsync)}, "
            + $"then map each {nameof(PlaceholderResolution)} onto a {nameof(PlaceholderResolutionDto)}.");
    }
}
