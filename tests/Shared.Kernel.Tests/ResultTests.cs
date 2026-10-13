using System.Net;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class ResultTests
{
    private sealed record SampleError(string Code, string Message, HttpStatusCode Status)
        : ApiError(Code, Message, Status);

    private static readonly SampleError AnError =
        new("SAMPLE_FAIL", "Something went wrong", HttpStatusCode.BadRequest);

    [Fact]
    public void Success_exposes_the_value_and_no_error()
    {
        var result = Result<int, SampleError>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Accessing_Error_on_a_Success_throws()
    {
        var result = Result<int, SampleError>.Success(42);

        Should.Throw<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void Failure_exposes_the_error_and_no_value()
    {
        var result = Result<int, SampleError>.Failure(AnError);

        result.IsFailure.ShouldBeTrue();
        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(AnError);
    }

    [Fact]
    public void Accessing_Value_on_a_Failure_throws()
    {
        var result = Result<int, SampleError>.Failure(AnError);

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Match_routes_to_the_success_branch()
    {
        var result = Result<int, SampleError>.Success(7);

        var outcome = result.Match(v => $"ok:{v}", e => $"err:{e.Code}");

        outcome.ShouldBe("ok:7");
    }

    [Fact]
    public void Match_routes_to_the_failure_branch()
    {
        var result = Result<int, SampleError>.Failure(AnError);

        var outcome = result.Match(v => $"ok:{v}", e => $"err:{e.Code}");

        outcome.ShouldBe("err:SAMPLE_FAIL");
    }
}
