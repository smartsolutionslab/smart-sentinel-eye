using System.Globalization;
using SmartSentinelEye.Automation.Application.DTOs;
using SmartSentinelEye.Automation.Application.Queries;
using SmartSentinelEye.Automation.Application.Queries.Handlers;
using SmartSentinelEye.Automation.Application.Tests.Fakes;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using SmartSentinelEye.Shared.Kernel;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Queries;

// Spec 459 (#3154), phase 4a: every RuleDto / RuleActionDto field on the mapping as it
// stands, with values distinct from their same-typed siblings.
public class RuleDtoMappingCharacterisationTests
{
    private static readonly IReadOnlyList<FabIdentifier> Hamburg = [FabIdentifier.From("hamburg")];

    private static readonly DateTimeOffset Created =
        DateTimeOffset.Parse("2026-02-01T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly DateTimeOffset Published = Created.AddHours(3);
    private static readonly DateTimeOffset Archived = Created.AddHours(7);

    private static readonly OperatorIdentifier Author =
        OperatorIdentifier.From(Guid.Parse("0a0a0a0a-0000-7000-8000-00000000000a"));

    private static InMemoryRuleQuerySource Seed(params RuleAggregate[] rules)
    {
        InMemoryRuleRepository repository = new();
        foreach (var rule in rules)
        {
            repository.Add(rule);
        }

        return new InMemoryRuleQuerySource(repository);
    }

    private static RuleBuilder Builder(string name, RuleAction action) =>
        new RuleBuilder()
            .WithFab("hamburg")
            .WithName(name)
            .WithTriggerSource("inference")
            .WithTriggerKind("DefectDetected")
            .WithPredicate("$.payload.score >= 0.9")
            .WithAction(action)
            .WithCreatedBy(Author)
            .WithClock(Created);

    [Fact]
    public async Task Get_maps_every_rule_field_for_an_active_rule()
    {
        var rule = Builder("every-field", RuleAction.SetVariableValue.From("lineState", "$.payload.score"))
            .Build();
        rule.Publish(new FakeClock(Published));
        var source = Seed(rule);

        var result = await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Hamburg, "every-field"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value;
        dto.RuleIdentifier.ShouldBe(rule.Id.Value);
        dto.Version.ShouldBe(rule.Version);
        dto.Fab.ShouldBe("hamburg");
        dto.Name.ShouldBe("every-field");
        dto.TriggerSource.ShouldBe("inference");
        dto.TriggerKind.ShouldBe("DefectDetected");
        dto.Predicate.ShouldBe("$.payload.score >= 0.9");
        dto.State.ShouldBe(RuleState.Active.Value);
        dto.CreatedAt.ShouldBe(Created);
        dto.CreatedBy.ShouldBe(Author.Value);
        dto.PublishedAt.ShouldBe(Published);
        dto.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task List_maps_published_and_archived_timestamps_from_their_own_sources()
    {
        var rule = Builder("lifecycle", RuleAction.SetVariableValue.From("lineState", "1")).Build();
        rule.Publish(new FakeClock(Published));
        rule.Archive(new FakeClock(Archived));
        var source = Seed(rule);

        var result = await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery(Hamburg, RuleState.Archived.Value, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var dto = result.Value.ShouldHaveSingleItem();
        dto.State.ShouldBe(RuleState.Archived.Value);
        dto.CreatedAt.ShouldBe(Created);
        dto.PublishedAt.ShouldBe(Published);
        dto.ArchivedAt.ShouldBe(Archived);
    }

    [Fact]
    public async Task List_maps_a_draft_rule_with_null_published_and_archived_timestamps()
    {
        var source = Seed(Builder("draft", RuleAction.SetVariableValue.From("lineState", "1")).Build());

        var result = await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery(Hamburg, null, null, null), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.State.ShouldBe(RuleState.Draft.Value);
        dto.PublishedAt.ShouldBeNull();
        dto.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_SetVariableValue_action_leaves_every_other_variant_field_null()
    {
        var source = Seed(Builder("set-variable", RuleAction.SetVariableValue.From("lineState", "42")).Build());

        var dto = (await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Hamburg, "set-variable"), CancellationToken.None)).Value.Action;

        dto.ShouldBe(new RuleActionDto(
            RuleActionDto.SetVariableValueKind, "lineState", "42", null, null, null, null, null));
    }

    [Fact]
    public async Task A_HighlightOverlay_action_leaves_every_other_variant_field_null()
    {
        var overlay = Guid.Parse("0b0b0b0b-0000-7000-8000-00000000000b");
        var source = Seed(Builder("highlight", RuleAction.HighlightOverlay.From(overlay, 4_500)).Build());

        var dto = (await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Hamburg, "highlight"), CancellationToken.None)).Value.Action;

        dto.ShouldBe(new RuleActionDto(
            RuleActionDto.HighlightOverlayKind, null, null, overlay, 4_500, null, null, null));
    }

    [Fact]
    public async Task A_SwitchWallScene_Layout_action_carries_wall_literal_and_target_layout_only()
    {
        var wall = Guid.Parse("0c0c0c0c-0000-7000-8000-00000000000c");
        var layout = Guid.Parse("0d0d0d0d-0000-7000-8000-00000000000d");
        var source = Seed(Builder("switch-layout", RuleAction.SwitchWallScene.From(wall, "Layout", layout)).Build());

        var dto = (await new GetRuleQueryHandler(source)
            .HandleAsync(new GetRuleQuery(Hamburg, "switch-layout"), CancellationToken.None)).Value.Action;

        dto.ShouldBe(new RuleActionDto(
            RuleActionDto.SwitchWallSceneKind, null, null, null, null, wall, "Layout", layout));
    }

    [Fact]
    public async Task A_SwitchWallScene_Next_action_carries_wall_and_literal_with_a_null_target_layout()
    {
        var wall = Guid.Parse("0c0c0c0c-0000-7000-8000-00000000000c");
        var source = Seed(Builder("switch-next", RuleAction.SwitchWallScene.From(wall, "Next", null)).Build());

        var dto = (await new ListRulesQueryHandler(source)
            .HandleAsync(new ListRulesQuery(Hamburg, null, null, null), CancellationToken.None)).Value
            .ShouldHaveSingleItem().Action;

        dto.ShouldBe(new RuleActionDto(
            RuleActionDto.SwitchWallSceneKind, null, null, null, null, wall, "Next", null));
    }
}
