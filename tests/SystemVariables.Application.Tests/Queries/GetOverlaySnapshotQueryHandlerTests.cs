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
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

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
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

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
            await handler.HandleAsync(new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ResolvedText.ShouldBe("{{shift}} - {{unknown}}");
    }

    // ---- spec 014 T037 (as amended by ADR-0115): the viewer's fab ----

    [Fact]
    public async Task An_overlay_resolves_the_viewers_fab_not_another()
    {
        // The same overlay, rendered for two different plants. This is the
        // whole point of ADR-0115: one template, per-fab values.
        Guid overlay = Guid.CreateVersion7();
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());
        repo.Add(new VariableBuilder().WithFab("dresden").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(7)).Build());

        InMemoryReverseIndex index = new();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> munich = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("munich")], overlay), CancellationToken.None);
        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> dresden = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("dresden")], overlay), CancellationToken.None);

        munich.Value.ResolvedText.ShouldBe("OEE: 41%");
        // The assertion that matters: asserting munich alone would pass just as
        // well if resolution were still global.
        dresden.Value.ResolvedText.ShouldBe("OEE: 7%");
    }

    [Fact]
    public async Task A_variable_absent_from_the_viewers_fab_renders_the_literal_placeholder()
    {
        // Identical to a name that exists nowhere, per the contract.
        Guid overlay = Guid.CreateVersion7();
        InMemoryVariableRepository repo = new();
        repo.Add(new VariableBuilder().WithFab("munich").Named("oeeLine1")
            .OfType(VariableType.Number).WithInitialValue(new VariableValue.NumberValue(41)).Build());

        InMemoryReverseIndex index = new();
        index.UpsertOverlayReferences(overlay, "OEE: {{oeeLine1}}%");
        GetOverlaySnapshotQueryHandler handler = new(index, repo, new Resolver());

        Result<ResolvedOverlaySnapshotDto, GetOverlaySnapshotError> result = await handler.HandleAsync(
            new GetOverlaySnapshotQuery([FabIdentifier.From("dresden")], overlay), CancellationToken.None);

        result.Value.ResolvedText.ShouldBe("OEE: {{oeeLine1}}%");
    }
}
