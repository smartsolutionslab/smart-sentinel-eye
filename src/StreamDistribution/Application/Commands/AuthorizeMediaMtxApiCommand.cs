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
    : ICommand<Result<bool, AuthorizeMediaMtxApiError>>;
