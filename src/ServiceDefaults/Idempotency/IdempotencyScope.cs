using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// What a caller-supplied idempotency key is scoped to (ADR-0142, widened by
/// spec 302 / #2424 / #2492). All five parts are load-bearing.
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
///
/// <para>
/// <see cref="Fab"/> (#2492 — one operator, two plants) and
/// <see cref="Fingerprint"/> (#2424 — one key, two resources) are
/// <b>compared</b>, not <b>keyed</b>: the table's primary key stays
/// <c>(key, endpoint, caller)</c>, because a key names <i>one</i> request, and
/// a reuse against a different fab or a different body is refused rather than
/// treated as a second request under the same identity.
/// </para>
/// </summary>
/// <param name="Key">The caller's key, already validated at the boundary.</param>
/// <param name="Endpoint">Stable route identity, e.g. <c>POST /devices/register</c>.</param>
/// <param name="Caller">The authenticated subject the key belongs to.</param>
/// <param name="Fab">
/// The resolved fab identifier's value, never the raw query string, or
/// <see cref="Option{T}.None"/> for a fab-less context (OverlayDesigner).
/// </param>
/// <param name="Fingerprint">A digest of the bound request, so a different resource under the same key is detected.</param>
public sealed record IdempotencyScope(
    IdempotencyKey Key, string Endpoint, string Caller, Option<string> Fab, IdempotencyFingerprint Fingerprint)
{
    public static IdempotencyScope For(
        IdempotencyKey key, string endpoint, string caller, Option<string> fab, IdempotencyFingerprint fingerprint)
    {
        Ensure.That(key).IsNotNull();
        Ensure.That(endpoint).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(caller).IsNotNull().IsNotNullOrWhiteSpace();
        Ensure.That(fingerprint).IsNotNull();
        if (fab.HasValue)
        {
            Ensure.That(fab.Value, nameof(fab)).IsNotNullOrWhiteSpace();
        }

        return new IdempotencyScope(key, endpoint, caller, fab, fingerprint);
    }
}
