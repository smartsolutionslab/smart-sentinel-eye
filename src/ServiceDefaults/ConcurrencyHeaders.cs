using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Carries an aggregate's optimistic-concurrency version across the HTTP
/// boundary (ADR-0113 Layer 1): reads it out of <c>If-Match</c> on a
/// mutating request, and formats it as an <c>ETag</c> on a read.
///
/// <para>
/// Both directions live here because they have to agree on one format. A
/// header is used rather than a request-body field because 14 of the 28
/// mutating endpoints take no body at all — publish, archive, branch,
/// revert and three DELETEs.
/// </para>
///
/// <para>
/// Follows the <see cref="BoundaryParse"/> convention: a
/// <c>bool</c> plus an out <see cref="IResult"/> problem, so endpoints
/// return early without each one repeating the same
/// <c>Results.Problem(...)</c> block.
/// </para>
/// </summary>
public static class ConcurrencyHeaders
{
    /// <summary>Returned when a mutating request carries no usable <c>If-Match</c>.</summary>
    public const string MissingErrorCode = "IF_MATCH_REQUIRED";

    /// <summary>Returned when <c>If-Match</c> is present but is not a single strong version tag.</summary>
    public const string MalformedErrorCode = "IF_MATCH_MALFORMED";

    private const string Wildcard = "*";
    private const string WeakPrefix = "W/";

    /// <summary>
    /// Formats an aggregate version as a strong entity tag, e.g. <c>"7"</c>.
    /// </summary>
    public static string ETag(int version) => string.Concat("\"", version.ToString(CultureInfo.InvariantCulture), "\"");

    /// <summary>
    /// Reads the expected aggregate version from <c>If-Match</c>.
    ///
    /// <para>
    /// A mutating request without the header is rejected with
    /// <c>428 Precondition Required</c> rather than defaulting to
    /// "no concurrency control" — a silent fallback would reopen exactly the
    /// lost-update hole ADR-0113 closes.
    /// </para>
    /// </summary>
    public static bool TryReadExpectedVersion(HttpRequest request, out int expectedVersion, out IResult problem)
    {
        Ensure.That(request).IsNotNull();

        expectedVersion = default;
        problem = null;

        StringValues header = request.Headers.IfMatch;

        if (header.Count == 0)
        {
            problem = Missing();

            return false;
        }

        if (header.Count > 1)
        {
            problem = Malformed("If-Match must carry exactly one version tag.");

            return false;
        }

        string raw = header[0]?.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            problem = Missing();

            return false;
        }

        return TryParseTag(raw, out expectedVersion, out problem);
    }

    private static bool TryParseTag(string raw, out int version, out IResult problem)
    {
        version = default;
        problem = null;

        // "*" is valid HTTP — "any current representation" — but accepting it
        // would let a caller opt out of the concurrency check entirely.
        if (string.Equals(raw, Wildcard, StringComparison.Ordinal))
        {
            problem = Malformed("A wildcard If-Match is not accepted; send the version the resource was read at.");

            return false;
        }

        // RFC 7232 requires strong comparison for If-Match, so W/"..." is invalid here.
        if (raw.StartsWith(WeakPrefix, StringComparison.Ordinal))
        {
            problem = Malformed("If-Match requires a strong entity tag; weak tags are not accepted.");

            return false;
        }

        if (raw.Contains(',', StringComparison.Ordinal))
        {
            problem = Malformed("If-Match must carry exactly one version tag.");

            return false;
        }

        if (!int.TryParse(Unquote(raw), NumberStyles.None, CultureInfo.InvariantCulture, out version))
        {
            problem = Malformed($"If-Match value '{raw}' is not a version tag.");

            return false;
        }

        return true;
    }

    private static string Unquote(string raw) =>
        raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;

    private static IResult Missing() => Results.Problem(
        title: MissingErrorCode,
        detail: "This request must be conditional. Send If-Match with the version the resource was read at.",
        statusCode: StatusCodes.Status428PreconditionRequired);

    private static IResult Malformed(string detail) => Results.Problem(
        title: MalformedErrorCode,
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest);
}
