using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// Reads the caller's <c>Idempotency-Key</c> off a request (ADR-0142).
///
/// <para>
/// Follows the <see cref="BoundaryParse"/> convention — a <c>bool</c> plus an
/// out <see cref="IResult"/> problem — so endpoints return early without each
/// repeating the same validation block. Sits beside
/// <see cref="ConcurrencyHeaders"/>, which does the same job for
/// <c>If-Match</c>.
/// </para>
///
/// <para>
/// <b>Absent is not an error.</b> ADR-0142 makes the key opt-in: no header means
/// the endpoint behaves exactly as it did before, including the 409 spec 008
/// FR-010 specifies for a genuine duplicate. That is what keeps the
/// constitution amendment narrow — a caller has to ask for replay before a
/// secret can cross the wire twice.
/// </para>
/// </summary>
public static class IdempotencyHeaders
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Returned when the header is present but not a usable key.</summary>
    public const string MalformedErrorCode = "IDEMPOTENCY_KEY_MALFORMED";

    /// <summary>Returned when an earlier attempt on the same key has not finished.</summary>
    public const string InProgressErrorCode = "IDEMPOTENT_REQUEST_IN_PROGRESS";

    /// <summary>
    /// Yields the caller's key, or <see cref="Option{T}.None"/> when the header
    /// is absent. Returns <c>false</c> only when a header was sent and could not
    /// be used — silently ignoring a malformed key would give the caller the
    /// at-most-once guarantee it asked for and not deliver it.
    /// </summary>
    public static bool TryRead(
        HttpRequest request,
        out Option<IdempotencyKey> key,
        [NotNullWhen(false)] out IResult? problem)
    {
        Ensure.That(request).IsNotNull();

        key = Option<IdempotencyKey>.None;
        problem = null;

        if (!request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues values)
            || values.Count == 0)
        {
            return true;
        }

        if (values.Count > 1)
        {
            problem = Malformed($"Send at most one {HeaderName} header; this request carried {values.Count}.");

            return false;
        }

        string? raw = values[0];

        if (string.IsNullOrWhiteSpace(raw))
        {
            problem = Malformed($"{HeaderName} was present but empty.");

            return false;
        }

        // Spelled out rather than routed through BoundaryParse.TryParse. That
        // helper is generic and unconstrained, so its MaybeNullWhen(false) does
        // not narrow the out parameter for a reference type — the call site
        // needs a null-forgiving operator to compile, and ADR-0141 spent a
        // release removing those. The catch below is the same four lines the
        // helper would have run, and it costs no `!`.
        IdempotencyKey parsed;
        try
        {
            parsed = IdempotencyKey.From(raw);
        }
        catch (ArgumentException exception)
        {
            problem = Malformed(exception.Message);

            return false;
        }

        key = Option<IdempotencyKey>.Some(parsed);

        return true;
    }

    private static IResult Malformed(string detail) =>
        Results.Problem(
            title: MalformedErrorCode,
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest);
}
