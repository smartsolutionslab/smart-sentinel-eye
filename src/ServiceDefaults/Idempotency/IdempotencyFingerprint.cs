using System.Security.Cryptography;
using System.Text.Json;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// Spec 302 (#2424/#2492) — what a key is bound to, beyond its
/// <see cref="IdempotencyScope.Endpoint"/>: a SHA-256 hex digest over a
/// canonical JSON envelope of the bound request DTO plus any route-bound
/// values (e.g. a webhook rotation's route <c>{name}</c> and its upsert
/// precondition). Two requests with the same fingerprint are the same
/// request; anything else is a different one.
///
/// <para>
/// <b>Why the bound DTO and not the raw body.</b> Minimal APIs have already
/// consumed the body stream by the time a handler runs, so re-serialising the
/// <i>bound</i> DTO is what is available — and it is also what "the same
/// request" should mean: it normalises whitespace and property order away.
/// </para>
///
/// <para>
/// <b>Why a JSON envelope for the bound values, not concatenation.</b> An
/// array is unambiguous; naive concatenation would make
/// <c>("ab", "c")</c> equal <c>("a", "bc")</c>.
/// </para>
///
/// <para>
/// Serialised with a private, frozen <see cref="JsonSerializerOptions"/> —
/// never the app's own HTTP options — so an app-wide option change does not
/// silently re-fingerprint live keys. Nothing is reversible from the stored
/// value; no body is stored (ADR-0142 §Decision.3 holds).
/// </para>
/// </summary>
public sealed class IdempotencyFingerprint : IEquatable<IdempotencyFingerprint>
{
    /// <summary>SHA-256 hex digest length, lowercase.</summary>
    public const int Length = 64;

    private static readonly JsonSerializerOptions Options = new();

    private IdempotencyFingerprint(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>
    /// Fingerprints <paramref name="request"/> together with any
    /// <paramref name="boundValues"/> — route values or preconditions that
    /// select the operation's target but are not part of the body.
    /// </summary>
    public static IdempotencyFingerprint Of<TRequest>(TRequest request, params ReadOnlySpan<string> boundValues)
        where TRequest : class
    {
        Ensure.That(request).IsNotNull();

        Envelope<TRequest> envelope = new(request, [.. boundValues]);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        byte[] hash = SHA256.HashData(json);

        return new IdempotencyFingerprint(Convert.ToHexStringLower(hash));
    }

    public bool Equals(IdempotencyFingerprint? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as IdempotencyFingerprint);

    public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Value;

    public static bool operator ==(IdempotencyFingerprint? left, IdempotencyFingerprint? right) =>
        Equals(left, right);

    public static bool operator !=(IdempotencyFingerprint? left, IdempotencyFingerprint? right) =>
        !Equals(left, right);

    private sealed record Envelope<TRequest>(TRequest Request, string[] BoundValues);
}
