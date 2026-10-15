using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Characterisation (spec 368, ADR-0144), captured green on unmodified code before
/// <c>ConcurrencyHeaders.TryReadUpsertPrecondition</c> is split. <see cref="UpsertPreconditionTests"/>
/// pins which branch is taken; this pins the exact problem each branch answers (status, title and
/// detail) and which header value is read, so a refactor cannot change a refusal's wording or
/// precedence unseen. Expected details are hand-written literals, never the production strings.
///
/// <para>
/// Counterfactual seen to fail: accepting a non-wildcard <c>If-None-Match</c> turns
/// <see cref="A_non_wildcard_if_none_match_is_a_400_that_says_it_must_be_a_wildcard"/> red.
/// </para>
/// </summary>
public class UpsertPreconditionProblemCharacterisationTests
{
    private static HttpRequest RequestWith(string? ifMatch = null, StringValues? ifNoneMatch = null)
    {
        var request = new DefaultHttpContext().Request;
        if (ifMatch is not null)
        {
            request.Headers.IfMatch = ifMatch;
        }

        if (ifNoneMatch is { } values)
        {
            request.Headers.IfNoneMatch = values;
        }

        return request;
    }

    private static void ShouldBeProblem(IResult? problem, int status, string title, string detail)
    {
        var http = problem.ShouldBeOfType<ProblemHttpResult>();
        http.StatusCode.ShouldBe(status);
        http.ProblemDetails.Title.ShouldBe(title);
        http.ProblemDetails.Detail.ShouldBe(detail);
    }

    [Fact]
    public void Both_headers_are_a_400_that_asks_for_one_of_them()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifMatch: "\"3\"", ifNoneMatch: "*"), out var expected, out var problem);

        read.ShouldBeFalse();
        expected.HasValue.ShouldBeFalse();
        ShouldBeProblem(problem, 400, "IF_MATCH_MALFORMED", "Send either If-Match or If-None-Match, not both.");
    }

    [Fact]
    public void Neither_header_is_a_428_that_names_both_ways_to_be_conditional()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(RequestWith(), out var expected, out var problem);

        read.ShouldBeFalse();
        expected.HasValue.ShouldBeFalse();
        ShouldBeProblem(
            problem,
            428,
            "IF_MATCH_REQUIRED",
            "This request must be conditional. Send If-Match with the version the resource was read at, "
            + "or If-None-Match: * if you are creating it.");
    }

    [Fact]
    public void A_non_wildcard_if_none_match_is_a_400_that_says_it_must_be_a_wildcard()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifNoneMatch: "x"), out var expected, out var problem);

        read.ShouldBeFalse();
        expected.HasValue.ShouldBeFalse();
        ShouldBeProblem(
            problem,
            400,
            "IF_MATCH_MALFORMED",
            "If-None-Match must be '*' here; it asserts the resource does not exist yet.");
    }

    [Fact]
    public void A_wildcard_if_none_match_is_trimmed_and_asserts_the_resource_does_not_exist()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifNoneMatch: " * "), out var expected, out var problem);

        read.ShouldBeTrue();
        expected.HasValue.ShouldBeFalse();
        problem.ShouldBeNull();
    }

    [Fact]
    public void Only_the_first_if_none_match_value_is_read_so_a_leading_wildcard_wins()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifNoneMatch: new StringValues(["*", "x"])), out var expected, out var problem);

        read.ShouldBeTrue();
        expected.HasValue.ShouldBeFalse();
        problem.ShouldBeNull();
    }

    [Fact]
    public void Only_the_first_if_none_match_value_is_read_so_a_trailing_wildcard_does_not_save_it()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifNoneMatch: new StringValues(["x", "*"])), out _, out var problem);

        read.ShouldBeFalse();
        ShouldBeProblem(
            problem,
            400,
            "IF_MATCH_MALFORMED",
            "If-None-Match must be '*' here; it asserts the resource does not exist yet.");
    }

    [Fact]
    public void An_if_match_refusal_is_passed_through_with_its_own_detail()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifMatch: "W/\"3\""), out var expected, out var problem);

        read.ShouldBeFalse();
        expected.HasValue.ShouldBeFalse();
        ShouldBeProblem(
            problem,
            400,
            "IF_MATCH_MALFORMED",
            "If-Match requires a strong entity tag; weak tags are not accepted.");
    }

    [Fact]
    public void A_strong_if_match_is_the_expected_version_with_no_problem()
    {
        var read = ConcurrencyHeaders.TryReadUpsertPrecondition(
            RequestWith(ifMatch: "\"3\""), out var expected, out var problem);

        read.ShouldBeTrue();
        expected.ShouldBe(Option<int>.Some(3));
        problem.ShouldBeNull();
    }
}
