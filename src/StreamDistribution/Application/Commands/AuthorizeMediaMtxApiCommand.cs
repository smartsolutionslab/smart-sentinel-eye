using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Authorizes MediaMTX's own control-API call against the shared secret
/// ADR-0171 adds for it. A sibling of <see cref="AuthorizeWhepCommand"/>
/// rather than a branch inside it: the <c>api</c> action carries no stream
/// path (MediaMTX's hook body sends <c>path: ""</c> for it) and no bearer
/// token, so forcing it through the WHEP command would make
/// <see cref="AuthorizeWhepCommand.Path"/> optional for every other caller to
/// accommodate a request shape that is not theirs.
/// </summary>
public sealed record AuthorizeMediaMtxApiCommand(string? Password)
    : ICommand<Result<bool, AuthorizeMediaMtxApiError>>
{
    /// <summary>
    /// <see cref="Password"/> is the control-API secret itself — it must never
    /// reach a log line through this record's compiler-generated
    /// <c>ToString()</c>, which is what actually calls this method.
    /// </summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{nameof(Password)} = {(Password is null ? "null" : "***")}");
        return true;
    }
}
