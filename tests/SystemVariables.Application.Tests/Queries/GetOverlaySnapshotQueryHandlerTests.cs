using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class GetOverlaySnapshotQueryHandlerTests
{
    [Fact]
    public async Task Returns_OverlayNotInReverseIndex_when_the_overlay_has_no_published_revision()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver());

        Guid overlay = Guid.CreateVersion7();
        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery(overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetOverlaySnapshotError.OverlayNotInReverseIndex>();
    }

    [Fact]
    public async Task Returns_the_resolved_label_and_current_version_when_the_overlay_is_indexed()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        repo.Add(new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build());

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");
        index.NextVersionFor(overlay); // bump to 1 to simulate a prior push

        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery(overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OverlayIdentifier.ShouldBe(overlay);
        result.Value.ResolvedText.ShouldBe("OEE: 82.5%");
        result.Value.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Skips_archived_and_unset_variables_so_they_render_as_literal_placeholders()
    {
        InMemoryReverseIndex index = new();
        InMemoryVariableRepository repo = new();

        // 'shift' is defined but unset → renders as literal.
        repo.Add(new VariableBuilder().Named("shift").OfType(VariableType.String).Build());

        Guid overlay = Guid.CreateVersion7();
        index.UpsertOverlayReferences(overlay, "{{shift}} - {{unknown}}");

        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result =
            await handler.HandleAsync(new GetOverlaySnapshotQuery(overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResolvedText.ShouldBe("{{shift}} - {{unknown}}");
    }
}
