using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SmartSentinelEye.ServiceDefaults.Tests;

public class ConcurrencyHeadersTests
{
    [Fact]
    public void A_version_is_formatted_as_a_quoted_strong_entity_tag()
    {
        ConcurrencyHeaders.ETag(7).ShouldBe("\"7\"");
    }

    [Fact]
    public void A_quoted_tag_round_trips_the_version_it_was_formatted_from()
    {
        HttpRequest request = RequestWith(ConcurrencyHeaders.ETag(42));

        ConcurrencyHeaders.TryReadExpectedVersion(request, out int version, out IResult problem).ShouldBeTrue();
        version.ShouldBe(42);
        problem.ShouldBeNull();
    }

    [Fact]
    public void An_unquoted_tag_is_accepted()
    {
        HttpRequest request = RequestWith("42");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out int version, out _).ShouldBeTrue();
        version.ShouldBe(42);
    }

    // A freshly created aggregate is at version 0, so this is a real value and
    // not an "unset" sentinel.
    [Fact]
    public void Version_zero_is_a_valid_tag()
    {
        HttpRequest request = RequestWith("\"0\"");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out int version, out _).ShouldBeTrue();
        version.ShouldBe(0);
    }

    [Fact]
    public void Surrounding_whitespace_is_tolerated()
    {
        HttpRequest request = RequestWith("  \"9\"  ");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out int version, out _).ShouldBeTrue();
        version.ShouldBe(9);
    }

    [Fact]
    public void A_missing_header_is_rejected_with_428_rather_than_defaulting()
    {
        HttpRequest request = new DefaultHttpContext().Request;

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status428PreconditionRequired);
        result.ProblemDetails.Title.ShouldBe(ConcurrencyHeaders.MissingErrorCode);
    }

    [Fact]
    public void An_empty_header_is_treated_as_missing()
    {
        HttpRequest request = RequestWith("   ");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    // "*" is legal HTTP meaning "any current representation". Accepting it
    // would let a caller opt out of the concurrency check entirely, which is
    // the hole this whole mechanism exists to close.
    [Fact]
    public void A_wildcard_is_rejected_because_it_would_bypass_the_check()
    {
        HttpRequest request = RequestWith("*");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.ProblemDetails.Title.ShouldBe(ConcurrencyHeaders.MalformedErrorCode);
    }

    [Fact]
    public void A_weak_tag_is_rejected_because_If_Match_requires_strong_comparison()
    {
        HttpRequest request = RequestWith("W/\"4\"");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void A_list_of_tags_in_one_value_is_rejected_as_ambiguous()
    {
        HttpRequest request = RequestWith("\"4\", \"5\"");

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Repeated_headers_are_rejected_as_ambiguous()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = new Microsoft.Extensions.Primitives.StringValues(["\"4\"", "\"5\""]);

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("abc")]
    [InlineData("\"-1\"")]
    [InlineData("\"1.5\"")]
    [InlineData("\"\"")]
    public void A_value_that_is_not_a_whole_version_number_is_rejected(string headerValue)
    {
        HttpRequest request = RequestWith(headerValue);

        ConcurrencyHeaders.TryReadExpectedVersion(request, out _, out IResult problem).ShouldBeFalse();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.ProblemDetails.Title.ShouldBe(ConcurrencyHeaders.MalformedErrorCode);
    }

    private static HttpRequest RequestWith(string ifMatch)
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = ifMatch;

        return request;
    }
}
