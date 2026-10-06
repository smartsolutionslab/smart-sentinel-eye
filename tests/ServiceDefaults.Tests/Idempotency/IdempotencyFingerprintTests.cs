using System.Text.Json;
using SmartSentinelEye.ServiceDefaults.Idempotency;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// Spec 302 (#2424/#2492) — <c>IdempotencyFingerprint.Of</c> (plan.md §1): a
/// SHA-256 hex digest over a canonical JSON envelope of the bound request DTO
/// plus any route-bound values, so a mismatched reuse can be detected without
/// storing the request itself.
///
/// <para>
/// <b>This class does not exist yet.</b> Per the architect's 3-commit plan this
/// is deliberate, expected compile-error red — the engineer's commit is what
/// makes it exist. Every fact below is written against the shape plan.md §1
/// specifies, not against any stub.
/// </para>
/// </summary>
public class IdempotencyFingerprintTests
{
    [Fact]
    public void Equal_inputs_produce_an_equal_fingerprint()
    {
        SampleRequest request = new("alpha", 1);

        IdempotencyFingerprint first = IdempotencyFingerprint.Of(request);
        IdempotencyFingerprint second = IdempotencyFingerprint.Of(new SampleRequest("alpha", 1));

        second.ShouldBe(first);
    }

    [Fact]
    public void A_different_body_field_produces_a_different_fingerprint()
    {
        IdempotencyFingerprint first = IdempotencyFingerprint.Of(new SampleRequest("alpha", 1));
        IdempotencyFingerprint second = IdempotencyFingerprint.Of(new SampleRequest("beta", 1));

        second.ShouldNotBe(first);
    }

    [Fact]
    public void A_different_bound_value_produces_a_different_fingerprint()
    {
        SampleRequest request = new("alpha", 1);

        IdempotencyFingerprint first = IdempotencyFingerprint.Of(request, "name-one");
        IdempotencyFingerprint second = IdempotencyFingerprint.Of(request, "name-two");

        second.ShouldNotBe(first);
    }

    /// <summary>
    /// The concatenation-ambiguity case plan.md §1 names explicitly: a naive
    /// join of the bound values would make <c>("ab", "c")</c> equal
    /// <c>("a", "bc")</c>. A JSON array envelope must not collide them.
    /// </summary>
    [Fact]
    public void Concatenation_ambiguous_bound_values_do_not_collide()
    {
        SampleRequest request = new("alpha", 1);

        IdempotencyFingerprint first = IdempotencyFingerprint.Of(request, "ab", "c");
        IdempotencyFingerprint second = IdempotencyFingerprint.Of(request, "a", "bc");

        second.ShouldNotBe(
            first,
            "(\"ab\",\"c\") and (\"a\",\"bc\") must not fingerprint the same, or two different route-bound "
            + "requests would be treated as the same one.");
    }

    [Fact]
    public void The_value_is_sixty_four_lowercase_hex_characters()
    {
        IdempotencyFingerprint fingerprint = IdempotencyFingerprint.Of(new SampleRequest("alpha", 1));

        fingerprint.Value.Length.ShouldBe(IdempotencyFingerprint.Length);
        fingerprint.Value.ShouldBe(fingerprint.Value.ToLowerInvariant());
        fingerprint.Value.ShouldMatch("^[0-9a-f]{64}$");
    }

    /// <summary>
    /// The same DTO type, bound from two wire payloads whose JSON properties
    /// are written in a different order, must fingerprint identically — a
    /// client's property order is not part of "the same request" (plan.md §1,
    /// "normalises whitespace and property order away"). Binding both JSON
    /// strings into the <i>same</i> C# type is what does the normalising: once
    /// bound, re-serialising the DTO always emits its properties in that
    /// type's own declared order, regardless of the order the wire payload
    /// used.
    /// </summary>
    [Fact]
    public void The_same_values_bound_from_two_different_wire_property_orders_still_match()
    {
        SampleRequest inOneOrder = JsonSerializer.Deserialize<SampleRequest>("""{"Name":"alpha","Version":1}""")!;
        SampleRequest inAnotherOrder = JsonSerializer.Deserialize<SampleRequest>("""{"Version":1,"Name":"alpha"}""")!;

        IdempotencyFingerprint first = IdempotencyFingerprint.Of(inOneOrder);
        IdempotencyFingerprint second = IdempotencyFingerprint.Of(inAnotherOrder);

        second.ShouldBe(
            first,
            "the wire payload's property order must not leak into the fingerprint once both are bound to "
            + "the same request type.");
    }

    [Fact]
    public void A_null_request_is_guarded()
    {
        Should.Throw<ArgumentNullException>(() => IdempotencyFingerprint.Of<SampleRequest>(null!));
    }

    private sealed record SampleRequest(string Name, int Version);
}
