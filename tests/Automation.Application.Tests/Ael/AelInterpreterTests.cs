using SmartSentinelEye.Automation.Application.Ael;

namespace SmartSentinelEye.Automation.Application.Tests.Ael;

public class AelInterpreterTests
{
    private static AelValue Eval(string source, string contextJson)
    {
        AelExpression expression = AelParser.Parse(source);
        return AelInterpreter.Evaluate(expression, AelFixtures.ContextFor(contextJson));
    }

    [Fact]
    public void Int_arithmetic_stays_in_ints_when_both_operands_are_int()
    {
        AelValue r = Eval("1 + 2 * 3", "{}");
        AelValue.IntValue iv = r.ShouldBeOfType<AelValue.IntValue>();
        iv.Value.ShouldBe(7);
    }

    [Fact]
    public void Mixed_int_and_decimal_promotes_to_decimal()
    {
        AelValue r = Eval("1 + 2.5", "{}");
        AelValue.DecimalValue dv = r.ShouldBeOfType<AelValue.DecimalValue>();
        dv.Value.ShouldBe(3.5m);
    }

    [Fact]
    public void Field_access_on_envelope_returns_string()
    {
        AelValue r = Eval("$.kind", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.StringValue>().Value.ShouldBe("PlcCycleStart");
    }

    [Fact]
    public void Field_access_on_payload_returns_int()
    {
        AelValue r = Eval("$.payload.cycleTime", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.IntValue>().Value.ShouldBe(27);
    }

    [Fact]
    public void Field_access_on_missing_key_returns_NullValue()
    {
        AelValue r = Eval("$.payload.missing", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.NullValue>();
    }

    [Fact]
    public void Equality_on_string_field_works()
    {
        AelValue r = Eval("$.kind == \"PlcCycleStart\"", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Comparison_on_numeric_field_works()
    {
        AelValue r = Eval("$.payload.cycleTime <= 30", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Plc_predicate_fixture_matches_PlcCycleStart_with_cycleTime_27()
    {
        AelValue r = Eval(AelFixtures.SimplePlcPredicate, AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Oee_value_expression_yields_decimal_46()
    {
        AelValue r = Eval(AelFixtures.OeeValueExpression, AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.IntValue>().Value.ShouldBe(46);
    }

    [Fact]
    public void Contains_returns_true_when_substring_is_present()
    {
        AelValue r = Eval(AelFixtures.ContainsExpression, AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Nested_boolean_with_suppressed_flag_evaluates_to_false()
    {
        AelValue r = Eval(AelFixtures.NestedBoolean, AelFixtures.SuppressedContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeFalse();
    }

    [Fact]
    public void Logical_and_short_circuits_on_false_left()
    {
        // Right side would error (string ' contains' int) but short-circuit avoids it.
        AelValue r = Eval("false && $.payload.cycleTime contains 1", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeFalse();
    }

    [Fact]
    public void Logical_or_short_circuits_on_true_left()
    {
        AelValue r = Eval("true || $.payload.cycleTime contains 1", AelFixtures.PlcCycleStartContext);
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeTrue();
    }

    [Fact]
    public void Division_by_zero_throws_at_eval_time()
    {
        Action act = () => Eval("1 / 0", "{}");
        act.ShouldThrow<InvalidOperationException>();
    }

    [Fact]
    public void Unary_minus_negates_int()
    {
        AelValue r = Eval("-7", "{}");
        r.ShouldBeOfType<AelValue.IntValue>().Value.ShouldBe(-7);
    }

    [Fact]
    public void Unary_bang_negates_bool()
    {
        AelValue r = Eval("!true", "{}");
        r.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeFalse();
    }
}
