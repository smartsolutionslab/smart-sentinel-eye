using SmartSentinelEye.SystemVariables.Domain.Variable;

namespace SmartSentinelEye.SystemVariables.Domain.Tests.Variable;

public class BooleanLabelsTests
{
    [Fact]
    public void Default_carries_Yes_and_No()
    {
        BooleanLabels.Default.TruthyLabel.ShouldBe("Yes");
        BooleanLabels.Default.FalsyLabel.ShouldBe("No");
    }

    [Fact]
    public void From_accepts_non_empty_strings()
    {
        BooleanLabels labels = BooleanLabels.From("On", "Off");
        labels.TruthyLabel.ShouldBe("On");
        labels.FalsyLabel.ShouldBe("Off");
    }

    [Theory]
    [InlineData("", "Off")]
    [InlineData("On", "")]
    [InlineData("  ", "Off")]
    public void Rejects_empty_or_whitespace_labels(string truthy, string falsy)
    {
        Action act = () => BooleanLabels.From(truthy, falsy);
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Rejects_labels_longer_than_64_chars()
    {
        string tooLong = new('x', 65);
        Action act = () => BooleanLabels.From(tooLong, "No");
        act.ShouldThrow<ArgumentException>();
    }
}
