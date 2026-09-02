using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// <c>TryReadUpsertPrecondition</c> exists because an endpoint that upserts
/// cannot express both intents in one <c>If-Match</c>. Reusing version 0 for
/// "no resource yet" collides with a real version 0 — an aggregate created but
/// never modified sits at exactly 0, since <c>AggregateVersionInterceptor</c>
/// does not bump <c>Added</c> roots — so a replayed create would be honoured
/// as an update against a live resource.
/// </summary>
public class UpsertPreconditionTests
{
    [Fact]
    public void If_None_Match_wildcard_means_the_caller_asserts_it_does_not_exist()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfNoneMatch = "*";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out Option<int> version, out IResult? problem)
            .ShouldBeTrue();
        version.HasValue.ShouldBeFalse();
        problem.ShouldBeNull();
    }

    [Fact]
    public void If_Match_carries_the_version_the_caller_holds()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = "\"7\"";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out Option<int> version, out _).ShouldBeTrue();
        version.HasValue.ShouldBeTrue();
        version.Value.ShouldBe(7);
    }

    // The collision this whole helper exists to avoid: version 0 is a real
    // version, and must not be mistaken for "nothing is there".
    [Fact]
    public void If_Match_zero_is_an_update_at_version_zero_not_a_create()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = "\"0\"";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out Option<int> version, out _).ShouldBeTrue();
        version.HasValue.ShouldBeTrue();
        version.Value.ShouldBe(0);
    }

    [Fact]
    public void Neither_header_is_refused_with_428()
    {
        HttpRequest request = new DefaultHttpContext().Request;

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out _, out IResult? problem).ShouldBeFalse();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status428PreconditionRequired);
        result.ProblemDetails.Title.ShouldBe(ConcurrencyHeaders.MissingErrorCode);
        result.ProblemDetails.Detail!.ShouldContain("If-None-Match");
    }

    [Fact]
    public void Both_headers_together_are_refused_as_ambiguous()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = "\"7\"";
        request.Headers.IfNoneMatch = "*";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out _, out IResult? problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    // A concrete tag on If-None-Match would mean "unless it is at this
    // version", which is not the create assertion this endpoint accepts.
    [Fact]
    public void A_non_wildcard_If_None_Match_is_refused()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfNoneMatch = "\"3\"";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out _, out IResult? problem).ShouldBeFalse();
        ProblemHttpResult result = problem.ShouldBeOfType<ProblemHttpResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.ProblemDetails.Title.ShouldBe(ConcurrencyHeaders.MalformedErrorCode);
    }

    [Fact]
    public void A_wildcard_on_If_Match_is_still_refused_as_an_opt_out()
    {
        HttpRequest request = new DefaultHttpContext().Request;
        request.Headers.IfMatch = "*";

        ConcurrencyHeaders.TryReadUpsertPrecondition(request, out _, out IResult? problem).ShouldBeFalse();
        problem.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }
}
