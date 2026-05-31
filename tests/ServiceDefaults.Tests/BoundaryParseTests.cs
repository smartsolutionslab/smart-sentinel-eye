using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SmartSentinelEye.ServiceDefaults.Tests;

public class BoundaryParseTests
{
    [Fact]
    public void Yields_the_parsed_value_and_no_problem_on_success()
    {
        bool succeeded = BoundaryParse.TryParse(
            () => "parsed", "CODE", out string value, out IResult problem);

        succeeded.ShouldBeTrue();
        value.ShouldBe("parsed");
        problem.ShouldBeNull();
    }

    [Fact]
    public void Maps_an_ArgumentException_to_a_400_problem_titled_with_the_error_code()
    {
        bool succeeded = BoundaryParse.TryParse<string>(
            () => throw new ArgumentException("revisionNumber must be positive."),
            "LAYOUT_INVALID_INPUT",
            out string value,
            out IResult problem);

        succeeded.ShouldBeFalse();
        value.ShouldBeNull();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.ProblemDetails.Title.ShouldBe("LAYOUT_INVALID_INPUT");
        result.ProblemDetails.Detail.ShouldBe("revisionNumber must be positive.");
    }

    [Fact]
    public void Does_not_swallow_exceptions_other_than_ArgumentException()
    {
        Should.Throw<InvalidOperationException>(() =>
            BoundaryParse.TryParse<string>(
                () => throw new InvalidOperationException("boom"), "CODE", out _, out _));
    }
}
