using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Failure cases for replaying a registration against an idempotency key
/// (ADR-0142).
/// </summary>
public abstract record ReplayRegistrationError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status);

/// <summary>
/// The key names a registration this context no longer has.
///
/// <para>
/// Reachable when a device was registered under a key and later removed. A
/// replay cannot invent the answer, and must not fall through to registering
/// again — that would turn a retry into a second, silent creation, which is the
/// outcome the whole mechanism exists to prevent.
/// </para>
/// </summary>
public sealed record ReplayedRegistrationMissing(Guid Client)
    : ReplayRegistrationError(
        "REPLAYED_REGISTRATION_MISSING",
        $"The registration '{Client}' recorded against this Idempotency-Key no longer exists.",
        HttpStatusCode.Conflict);

/// <summary>
/// The registration exists here but Keycloak no longer holds its client, so the
/// secret cannot be read back and the answer cannot be rebuilt.
/// </summary>
public sealed record ReplayedClientMissingInKeycloak(string ClientId)
    : ReplayRegistrationError(
        "REPLAYED_CLIENT_MISSING",
        $"Keycloak no longer holds client '{ClientId}', so its secret cannot be replayed.",
        HttpStatusCode.Conflict);

/// <summary>
/// Spec 454 (#3096): the client this key was answered for has changed since, so that answer can
/// no longer be replayed. The detail names no client, version, fab or secret.
/// </summary>
public sealed record ReplayedKeySuperseded(Guid Client)
    : ReplayRegistrationError(
        "IDEMPOTENCY_KEY_SUPERSEDED",
        "The client this Idempotency-Key was answered for has changed since, so that answer can no longer be replayed. Send a new key for a new request.",
        HttpStatusCode.Conflict);

/// <summary>
/// Spec 464 (#3123): the client this key was answered for has been disabled, so its secret is no
/// longer replayed. The detail names no client, fab or secret.
/// </summary>
public sealed record ReplayedClientDisabled(Guid Client)
    : ReplayRegistrationError(
        "REPLAYED_CLIENT_DISABLED",
        "The client this Idempotency-Key was answered for has been disabled, so its credentials can no longer be replayed.",
        HttpStatusCode.Conflict);
