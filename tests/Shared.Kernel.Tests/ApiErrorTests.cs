using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Shared.Kernel.Tests;

public class ApiErrorTests
{
    private sealed record TestError(string Code, string Message, HttpStatusCode Status)
        : ApiError(Code, Message, Status);

    [Fact]
    public void Concrete_subtype_exposes_Code_Message_and_Status()
    {
        TestError error = new("X_FAIL", "boom", HttpStatusCode.Conflict);

        error.Code.ShouldBe("X_FAIL");
        error.Message.ShouldBe("boom");
        error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Two_errors_with_the_same_fields_are_equal()
    {
        TestError first = new("X_FAIL", "boom", HttpStatusCode.Conflict);
        TestError second = new("X_FAIL", "boom", HttpStatusCode.Conflict);

        first.ShouldBe(second);
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }
}
