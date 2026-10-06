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
/// request" should mean: it normalises whitespace and the DTO's own declared
/// member order away, because System.Text.Json serialises those in
/// declaration order regardless of what order the caller sent them in.
/// </para>
///
/// <para>
/// <b>What it does not normalise.</b> A <see cref="JsonElement"/>-typed
/// member (e.g. <c>IngestManualEventRequest.Payload</c>) is re-serialised in
/// whatever key order the original caller sent, not canonicalised; and a
/// <c>decimal</c> member fingerprints by its original scale, so <c>0.5</c>
/// and <c>0.50</c> differ. A client that re-serialises such a value between
/// retries can get a false mismatch — fails safe (a spurious 422, never a
/// wrong replay), but worth knowing before assuming two "identical" retries
/// always fingerprint the same.
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
/// value; no body is stored (ADR-0142 §Decision.3 holds). A DTO shape change
/// (e.g. a new optional member added later) re-fingerprints every key still
/// in flight at deploy time — every such key answers a mismatch once rather
/// than replaying, an accepted tradeoff rather than a defect.
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
