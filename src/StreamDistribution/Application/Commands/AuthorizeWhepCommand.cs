using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Authorizes a WHEP open. MediaMTX POSTs this on every WHEP handshake
/// (FR-007); the handler validates the forwarded bearer token, checks the
/// <c>sse.management</c> scope, and rejects when the target stream is
/// Offline.
///
/// <para>
/// <c>Action</c> carries the operation MediaMTX said it was asking about.
/// <see cref="Option{T}.None"/> means the field was absent or named an operation
/// this product does not model. It has no default value on purpose:
/// <c>Option&lt;T&gt;</c> is a readonly struct whose <c>None</c> is
/// <c>default</c>, so a defaulted parameter would silently read "unknown" at
/// every call site that forgot to pass one.
/// </para>
/// </summary>
public sealed record AuthorizeWhepCommand(
    MediaMtxPath Path,
    string BearerToken,
    Option<MediaMtxAction> Action)
    : ICommand<Result<MediaMtxPath, AuthorizeWhepError>>;
