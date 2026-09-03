using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// What a caller-supplied idempotency key is scoped to (ADR-0142). All three
/// parts are load-bearing.
///
/// <para>
/// <b><see cref="Caller"/> is a security boundary, not bookkeeping.</b> A key is
/// a string the caller invents, so two callers will collide — "1", "test",
/// "retry" — sooner rather than later. Keyed on the string alone, the second
/// caller's request would replay the first caller's answer: on
/// <c>POST /devices/register</c> that is another tenant's device identifier
/// <i>and</i> its client secret, read back from Keycloak and handed over. Scoping
/// to the authenticated subject makes a collision impossible to exploit and
/// costs one column.
/// </para>
///
/// <para>
/// <see cref="Endpoint"/> keeps one caller's key from crossing operations, so
/// reusing a key on <c>/kiosks/enroll</c> after <c>/devices/register</c> is a
/// fresh request rather than a replay of the wrong shape.
/// </para>
/// </summary>
/// <param name="Key">The caller's key, already validated at the boundary.</param>
/// <param name="Endpoint">Stable route identity, e.g. <c>POST /devices/register</c>.</param>
/// <param name="Caller">The authenticated subject the key belongs to.</param>
public sealed record IdempotencyScope(IdempotencyKey Key, string Endpoint, string Caller)
{
    public static IdempotencyScope For(IdempotencyKey key, string endpoint, string caller)
    {
        Ensure.That(key).IsNotNull();
        Ensure.That(endpoint).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(caller).IsNotNull().IsNotNullOrWhiteSpace();

        return new IdempotencyScope(key, endpoint, caller);
    }
}
