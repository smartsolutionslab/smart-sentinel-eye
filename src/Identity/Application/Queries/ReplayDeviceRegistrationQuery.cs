using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Queries;

/// <summary>
/// Rebuilds the answer <c>POST /devices/register</c> already gave, for a caller
/// replaying its own idempotency key (ADR-0142).
///
/// <para>
/// <paramref name="DeviceType"/> and <paramref name="DeviceIdentifier"/> come
/// from the retry's own body rather than from storage, and that is deliberate: a
/// transparent retry <i>is</i> the same request, so it carries them. Recovering
/// them instead by splitting the client id would depend on a format that already
/// contains the separator — <c>plc-t040-01a0…</c> — and would be a parser
/// written to avoid asking the request that has the values in hand.
/// </para>
/// </summary>
/// <param name="Client">The registration recorded against the key.</param>
/// <param name="DeviceType">From the replayed request body.</param>
/// <param name="DeviceIdentifier">From the replayed request body.</param>
public sealed record ReplayDeviceRegistrationQuery(
    RegisteredClientIdentifier Client,
    string DeviceType,
    string DeviceIdentifier);
