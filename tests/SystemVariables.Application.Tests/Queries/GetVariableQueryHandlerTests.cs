using System.Net;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.DTOs;
using SmartSentinelEye.SystemVariables.Application.Queries;
using SmartSentinelEye.SystemVariables.Application.Queries.Handlers;
using SmartSentinelEye.SystemVariables.Application.Tests.Fakes;
using SmartSentinelEye.SystemVariables.Domain.Tests.Variable.Builders;
using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Application.Tests.Queries;

public class GetVariableQueryHandlerTests
{
    [Fact]
    public async Task Returns_VariableNotFound_when_no_variable_with_that_name_exists()
    {
        TestVariableQuerySource source = new([]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("ghost")), CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBeOfType<GetVariableError.VariableNotFound>();
    }

    // Without the version on the read side a caller has nothing to put in
    // If-Match, and the cross-request check degrades to no check (ADR-0113).
    [Fact]
    public async Task The_dto_carries_the_aggregate_version()
    {
        Variable variable = new VariableBuilder()
            .Named("oeeLine2").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(1)).Build();

        GetVariableQueryHandler handler = new(new TestVariableQuerySource([variable]));
        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], variable.Name), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(variable.Version);
    }

    [Fact]
    public async Task Returns_a_mapped_DTO_when_the_variable_exists()
    {
        Variable variable = new VariableBuilder()
            .Named("oeeLine1").OfType(VariableType.Number)
            .WithInitialValue(new VariableValue.NumberValue(82.5)).Build();

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("oeeLine1")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("oeeLine1");
        result.Value.Type.ShouldBe("Number");
        result.Value.State.ShouldBe("Defined");
        result.Value.Value.ShouldBe("82.5");
    }

    [Fact]
    public async Task Maps_Unset_value_to_null_on_the_DTO()
    {
        Variable variable = new VariableBuilder()
            .Named("shift").OfType(VariableType.String).Build();

        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("shift")), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBeNull();
    }

    // ---- spec 014 T028: the refusal paths ----

    [Fact]
    public async Task Another_fabs_variable_is_reported_as_not_found()
    {
        // FR-009: byte-identical to a name that was never used. A 403 would
        // confirm it exists and let an operator enumerate another fab's names
        // one guess at a time.
        Variable foreign = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([foreign]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> notYours = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("oeeLine1")),
            CancellationToken.None);
        Result<VariableDto, GetVariableError> neverExisted = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("ghost")),
            CancellationToken.None);

        notYours.IsFailure.ShouldBeTrue();
        notYours.Error.Code.ShouldBe(neverExisted.Error.Code);
        notYours.Error.Status.ShouldBe(neverExisted.Error.Status);
    }

    [Fact]
    public async Task A_name_held_in_two_of_the_callers_fabs_names_its_candidates()
    {
        Variable munich = new VariableBuilder().WithFab("munich").Named("oeeLine1").Build();
        Variable dresden = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([munich, dresden]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(
                [FabIdentifier.From("munich"), FabIdentifier.From("dresden")],
                VariableName.From("oeeLine1")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        GetVariableError.VariableFabAmbiguous ambiguous =
            result.Error.ShouldBeOfType<GetVariableError.VariableFabAmbiguous>();
        // Naming them leaks nothing: they are all fabs this caller already
        // reads. Without the names the operator cannot act on the message.
        ambiguous.Candidates.ShouldBe(["dresden", "munich"]);
    }

    [Fact]
    public async Task The_dto_carries_the_fab()
    {
        Variable variable = new VariableBuilder().WithFab("dresden").Named("oeeLine1").Build();
        TestVariableQuerySource source = new([variable]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("dresden")], VariableName.From("oeeLine1")),
            CancellationToken.None);

        result.Value.Fab.ShouldBe("dresden");
    }

    // ---- issue #2446: GetByNameAsync already excludes Archived; this handler doesn't ----

    [Fact]
    public async Task Resolves_the_live_variable_when_an_archived_one_holds_the_same_name()
    {
        // FR-005: archiving releases the name within its fab, so a second
        // "reused" in munich is a legal re-definition, not a duplicate.
        VariableBuilder archivedBuilder = new VariableBuilder().WithFab("munich").Named("reused");
        Variable archived = archivedBuilder.Build();
        archived.Archive(archivedBuilder.Operator, archivedBuilder.Clock);

        Variable live = new VariableBuilder().WithFab("munich").Named("reused").Build();

        TestVariableQuerySource source = new([archived, live]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("reused")),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("Defined");
        // The identifier, not only the state, so the test cannot pass by
        // resolving to the wrong (archived) row.
        result.Value.VariableIdentifier.ShouldBe(live.Id.Value);
    }

    [Fact]
    public async Task Reports_an_archived_variable_whose_name_was_never_re_used_as_not_found()
    {
        // The deliberate narrowing (spec 239): a by-name read agrees with
        // GetByNameAsync and no longer answers for an archived-only name.
        VariableBuilder builder = new VariableBuilder().WithFab("munich").Named("gone");
        Variable archived = builder.Build();
        archived.Archive(builder.Operator, builder.Clock);

        TestVariableQuerySource source = new([archived]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery([FabIdentifier.From("munich")], VariableName.From("gone")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        GetVariableError.VariableNotFound notFound =
            result.Error.ShouldBeOfType<GetVariableError.VariableNotFound>();
        notFound.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_ambiguity_refusal_names_each_holding_fab_exactly_once()
    {
        // Assert the collection, not the rendered message: a
        // Message.ShouldContain would pass against the duplicate-fab bug this
        // test exists to catch, a missing distinct on the candidate fabs.
        VariableBuilder archivedBuilder = new VariableBuilder().WithFab("munich").Named("shared");
        Variable archivedMunich = archivedBuilder.Build();
        archivedMunich.Archive(archivedBuilder.Operator, archivedBuilder.Clock);

        Variable liveMunich = new VariableBuilder().WithFab("munich").Named("shared").Build();
        Variable liveDresden = new VariableBuilder().WithFab("dresden").Named("shared").Build();

        TestVariableQuerySource source = new([archivedMunich, liveMunich, liveDresden]);
        GetVariableQueryHandler handler = new(source);

        Result<VariableDto, GetVariableError> result = await handler.HandleAsync(
            new GetVariableQuery(
                [FabIdentifier.From("munich"), FabIdentifier.From("dresden")],
                VariableName.From("shared")),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        GetVariableError.VariableFabAmbiguous ambiguous =
            result.Error.ShouldBeOfType<GetVariableError.VariableFabAmbiguous>();
        ambiguous.Candidates.ShouldBe(["dresden", "munich"]);
    }
}
