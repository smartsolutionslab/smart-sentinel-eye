using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using SmartSentinelEye.ServiceDefaults.Idempotency;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// Characterisation (spec 368, ADR-0144), captured green on unmodified code before
/// <c>IdempotencyHeaders.TryRead</c> is split. It is the trust boundary in front of the
/// at-most-once claim (ADR-0142), and had no direct test. Expected details are hand-written
/// literals, including <c>IdempotencyKey.From</c>'s <c>ArgumentException</c> text with its
/// <c>(Parameter 'value')</c> suffix.
///
/// <para>
/// <b>Judgement call J3.</b> "Present with zero values" is not constructible through a
/// <see cref="HeaderDictionary"/>: assigning <see cref="StringValues.Empty"/> drops the key, so the
/// request reads as absent. <see cref="A_header_set_to_no_values_is_absent_because_the_dictionary_drops_it"/>
/// records that. The <c>values.Count == 0</c> clause in production stays as it is.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: dropping the <c>values.Count &gt; 1</c> branch turns
/// <see cref="Two_headers_are_a_400_that_counts_them"/> red.
/// </para>
/// </summary>
public class IdempotencyHeadersTests
{
    private static HttpRequest RequestWith(StringValues? values)
    {
        var request = new DefaultHttpContext().Request;
        if (values is { } present)
        {
            request.Headers[IdempotencyHeaders.HeaderName] = present;
        }

        return request;
    }

    private static void ShouldBeMalformed(IResult? problem, string detail)
    {
        var http = problem.ShouldBeOfType<ProblemHttpResult>();
        http.StatusCode.ShouldBe(400);
        http.ProblemDetails.Title.ShouldBe("IDEMPOTENCY_KEY_MALFORMED");
        http.ProblemDetails.Detail.ShouldBe(detail);
    }

    [Fact]
    public void An_absent_header_is_not_an_error_and_yields_no_key()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith(null), out var key, out var problem);

        read.ShouldBeTrue();
        key.HasValue.ShouldBeFalse();
        problem.ShouldBeNull();
    }

    [Fact]
    public void A_header_set_to_no_values_is_absent_because_the_dictionary_drops_it()
    {
        var request = RequestWith(StringValues.Empty);
        request.Headers.ContainsKey(IdempotencyHeaders.HeaderName).ShouldBeFalse();

        var read = IdempotencyHeaders.TryRead(request, out var key, out var problem);

        read.ShouldBeTrue();
        key.HasValue.ShouldBeFalse();
        problem.ShouldBeNull();
    }

    [Fact]
    public void Two_headers_are_a_400_that_counts_them()
    {
        var read = IdempotencyHeaders.TryRead(
            RequestWith(new StringValues(["key-1", "key-2"])), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(problem, "Send at most one Idempotency-Key header; this request carried 2.");
    }

    [Fact]
    public void An_empty_header_is_a_400_that_says_it_was_empty()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith(string.Empty), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(problem, "Idempotency-Key was present but empty.");
    }

    [Fact]
    public void A_whitespace_header_is_a_400_that_says_it_was_empty()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith("   "), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(problem, "Idempotency-Key was present but empty.");
    }

    [Fact]
    public void A_single_null_value_is_a_400_that_says_it_was_empty()
    {
        string?[] nullValue = [null];

        var read = IdempotencyHeaders.TryRead(
            RequestWith(new StringValues(nullValue!)), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(problem, "Idempotency-Key was present but empty.");
    }

    [Fact]
    public void A_key_one_character_over_the_limit_is_a_400_with_the_value_objects_message()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith(new string('a', 129)), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(
            problem,
            "An idempotency key may be at most 128 characters; this one is 129. (Parameter 'value')");
    }

    [Fact]
    public void A_key_exactly_at_the_limit_is_accepted()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith(new string('a', 128)), out var key, out var problem);

        read.ShouldBeTrue();
        key.Value.Value.ShouldBe(new string('a', 128));
        problem.ShouldBeNull();
    }

    [Fact]
    public void A_key_with_a_character_outside_the_charset_is_a_400_with_the_value_objects_message()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith("key!"), out var key, out var problem);

        read.ShouldBeFalse();
        key.HasValue.ShouldBeFalse();
        ShouldBeMalformed(
            problem,
            "An idempotency key may contain letters, digits, '-', '_' and ':' only; found '!'. (Parameter 'value')");
    }

    [Fact]
    public void A_valid_key_with_surrounding_spaces_is_accepted_trimmed()
    {
        var read = IdempotencyHeaders.TryRead(RequestWith("  key-1  "), out var key, out var problem);

        read.ShouldBeTrue();
        key.Value.Value.ShouldBe("key-1");
        problem.ShouldBeNull();
    }
}
