using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Authorizes a WHEP open. MediaMTX POSTs this on every WHEP handshake
/// (FR-007); the handler validates the forwarded bearer token, checks the
/// <c>sse.management</c> scope, and rejects when the target stream is
/// Offline.
/// </summary>
public sealed record AuthorizeWhepCommand(MediaMtxPath Path, string BearerToken)
    : ICommand<Result<MediaMtxPath, AuthorizeWhepError>>;
