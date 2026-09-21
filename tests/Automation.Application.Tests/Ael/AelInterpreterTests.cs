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

    // ---- #2427: a JSON number outside decimal's range is unaddressable, not fatal ----

    [Fact]
    public void A_number_beyond_decimals_range_is_not_addressable()
    {
        AelValue r = Eval("$.payload.v", """{"payload": {"v": 1e30}}""");
        r.ShouldBe(AelValue.NullValue.Instance);
    }

    [Theory]
    [InlineData("1e30")]
    [InlineData("-1e30")]
    [InlineData("1e400")]
    [InlineData("79228162514264337593543950336")] // decimal.MaxValue + 1
    [InlineData(AelFixtures.NumberWith400Nines)]
    public void Every_number_outside_decimals_range_is_not_addressable(string value)
    {
        AelValue r = Eval("$.payload.v", "{\"payload\": {\"v\": " + value + "}}");
        r.ShouldBe(AelValue.NullValue.Instance);
    }

    // The guard against over-rejection: a fix that returned NullValue for
    // every non-long number would still pass the two facts above.
    [Fact]
    public void A_number_at_decimals_maximum_is_still_a_decimal()
    {
        AelValue r = Eval("$.payload.v", """{"payload": {"v": 79228162514264337593543950335}}""");
        r.ShouldBeOfType<AelValue.DecimalValue>().Value.ShouldBe(79228162514264337593543950335m);
    }

    // Pins the TryGetInt64 -> TryGetDecimal ordering: a number past long's
    // range but still inside decimal's must not fall through to NullValue.
    [Fact]
    public void A_number_beyond_long_but_inside_decimal_is_a_decimal()
    {
        AelValue r = Eval("$.payload.v", """{"payload": {"v": 9223372036854775808}}""");
        r.ShouldBeOfType<AelValue.DecimalValue>().Value.ShouldBe(9223372036854775808m);
    }

    [Fact]
    public void A_number_beyond_decimals_range_compares_as_a_missing_field_does()
    {
        AelValue oversized = Eval("$.payload.v == 42", """{"payload": {"v": 1e30}}""");
        AelValue missing = Eval("$.payload.v == 42", """{"payload": {}}""");

        oversized.ShouldBeOfType<AelValue.BoolValue>().Value.ShouldBeFalse();
        oversized.ShouldBe(missing);
    }
}
