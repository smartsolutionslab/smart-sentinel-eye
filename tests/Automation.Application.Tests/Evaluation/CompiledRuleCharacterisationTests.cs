using System.Globalization;
using SmartSentinelEye.Automation.Application.Ael;
using SmartSentinelEye.Automation.Application.Evaluation;
using SmartSentinelEye.Automation.Domain.Rule;
using SmartSentinelEye.Automation.Domain.Tests.Rule;
using RuleAggregate = SmartSentinelEye.Automation.Domain.Rule.Rule;

namespace SmartSentinelEye.Automation.Application.Tests.Evaluation;

/// <summary>
/// Characterisation (spec 347, ADR-0144), captured green on unmodified
/// <c>develop</c> before the private <c>CompiledRule</c> constructor takes the
/// <c>Rule</c> it is compiled from instead of eight loose parameters. Eight
/// parameters of five distinct types, three of them strings-in-disguise, is
/// exactly the shape in which a transposition compiles and misbehaves; no
/// existing test reads a compiled rule's properties back against the rule it
/// came from, and none has two malformed expressions at once.
/// </summary>
public class CompiledRuleCharacterisationTests
{
    private static readonly DateTimeOffset CreatedMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static RuleAggregate RuleWith(
        RuleAction action,
        string predicate = "$.payload.cycleTime <= 30") =>
        new RuleBuilder()
            .WithFab("dresden")
            .WithName("compiled-rule")
            .WithTriggerSource("opc")
            .WithTriggerKind("TagChanged")
            .WithPredicate(predicate)
            .WithAction(action)
            .WithClock(CreatedMoment)
            .Build();

    [Fact]
    public void A_set_variable_rule_is_carried_over_field_by_field_with_both_expressions_parsed()
    {
        var action = RuleAction.SetVariableValue.From("oeeLine1", "100 - $.payload.cycleTime * 2");
        var rule = RuleWith(action);

        var compiled = CompiledRule.From(rule);

        compiled.Identifier.ShouldBe(rule.Id);
        compiled.Fab.ShouldBe(FabIdentifier.From("dresden"));
        compiled.TriggerSource.ShouldBe(TriggerSource.From("opc"));
        compiled.TriggerKind.ShouldBe(TriggerKind.From("TagChanged"));
        compiled.CreatedAt.ShouldBe(CreatedMoment);
        compiled.Action.ShouldBeSameAs(action);

        var predicate = compiled.CompiledPredicate.ShouldBeOfType<AelExpression.Binary>();
        predicate.Operator.ShouldBe(BinaryOperator.LessThanOrEqual);
        predicate.Left.ShouldBeOfType<AelExpression.FieldAccess>().Segments.ShouldBe(["payload", "cycleTime"]);
        predicate.Right.ShouldBeOfType<AelExpression.Literal>().Value.ShouldBe(new AelValue.IntValue(30));

        var value = compiled.CompiledValueExpression.ShouldBeOfType<AelExpression.Binary>();
        value.Operator.ShouldBe(BinaryOperator.Subtract);
        value.Left.ShouldBeOfType<AelExpression.Literal>().Value.ShouldBe(new AelValue.IntValue(100));
        var product = value.Right.ShouldBeOfType<AelExpression.Binary>();
        product.Operator.ShouldBe(BinaryOperator.Multiply);
        product.Left.ShouldBeOfType<AelExpression.FieldAccess>().Segments.ShouldBe(["payload", "cycleTime"]);
        product.Right.ShouldBeOfType<AelExpression.Literal>().Value.ShouldBe(new AelValue.IntValue(2));
    }

    [Fact]
    public void A_highlight_rule_is_carried_over_with_no_value_expression()
    {
        var action = RuleAction.HighlightOverlay.From(Guid.Parse("00000000-0000-7000-8000-0000000000a1"), 5_000);
        var rule = RuleWith(action);

        var compiled = CompiledRule.From(rule);

        compiled.Identifier.ShouldBe(rule.Id);
        compiled.Fab.ShouldBe(FabIdentifier.From("dresden"));
        compiled.TriggerSource.ShouldBe(TriggerSource.From("opc"));
        compiled.TriggerKind.ShouldBe(TriggerKind.From("TagChanged"));
        compiled.CreatedAt.ShouldBe(CreatedMoment);
        compiled.Action.ShouldBeSameAs(action);
        compiled.CompiledPredicate.ShouldBeOfType<AelExpression.Binary>();
        compiled.CompiledValueExpression.ShouldBeNull();
    }

    [Fact]
    public void A_switch_wall_scene_rule_is_carried_over_with_no_value_expression()
    {
        var action = RuleAction.SwitchWallScene.From(Guid.Parse("00000000-0000-7000-8000-0000000000b2"), "Next", null);
        var rule = RuleWith(action);

        var compiled = CompiledRule.From(rule);

        compiled.Identifier.ShouldBe(rule.Id);
        compiled.Action.ShouldBeSameAs(action);
        compiled.CompiledValueExpression.ShouldBeNull();
    }

    [Fact]
    public void A_rule_with_a_malformed_predicate_and_a_malformed_value_expression_fails_on_the_predicate()
    {
        var rule = RuleWith(RuleAction.SetVariableValue.From("oeeLine1", "(2"), predicate: "1 +");

        var exception = Should.Throw<AelParseException>(() => CompiledRule.From(rule));

        exception.Position.ShouldBe(3);
        exception.Message.ShouldBe("AEL parse error at position 3: expected literal, field access, or '('; got ''");
    }

    [Fact]
    public void A_rule_with_a_malformed_value_expression_alone_fails_on_the_value_expression()
    {
        var rule = RuleWith(RuleAction.SetVariableValue.From("oeeLine1", "(2"));

        var exception = Should.Throw<AelParseException>(() => CompiledRule.From(rule));

        exception.Position.ShouldBe(2);
        exception.Message.ShouldBe("AEL parse error at position 2: expected ')'");
    }

    [Fact]
    public void A_highlight_rule_with_a_malformed_predicate_fails_on_the_predicate()
    {
        var rule = RuleWith(
            RuleAction.HighlightOverlay.From(Guid.Parse("00000000-0000-7000-8000-0000000000a1"), 5_000),
            predicate: "$.payload.cycleTime <=");

        var exception = Should.Throw<AelParseException>(() => CompiledRule.From(rule));

        exception.Position.ShouldBe(22);
        exception.Message.ShouldBe("AEL parse error at position 22: expected literal, field access, or '('; got ''");
    }

    [Fact]
    public void A_null_rule_is_refused_by_the_guard()
    {
        var exception = Should.Throw<ArgumentNullException>(() => CompiledRule.From(null!));

        exception.ParamName.ShouldBe("rule");
    }
}
