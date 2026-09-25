using System.Buffers.Text;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Spec 253 (issue #2533) — <see cref="LockoutSessionSurvivalIntegrationTests.CorruptSignature"/>
/// used to flip the JWT signature segment's <b>last character</b>, and for a
/// canonical base64url encoding that character carries only padding bits
/// about a quarter of the time. A 64-byte HS512 signature base64url-encodes
/// to 86 characters, and the last one always spells 'A', 'Q', 'g' or 'w'
/// (each ≈¼ of encodings); only the 'A' → 'B' flip the old helper applied
/// touches padding-only bits, so it left the <i>decoded</i> signature
/// byte-identical about a quarter of the time — a token Keycloak correctly
/// accepted, not a defect (spec §2).
///
/// <para>
/// Every fact here decodes the corrupted signature segment and compares
/// <b>bytes</b>, never the base64url string — comparing strings is exactly
/// the shape of assertion that let the original bug pass code review.
/// </para>
///
/// <para>
/// Fixture-free by design: no <c>[Collection(AspireCollection.Name)]</c>, no
/// running realm. The property under test is arithmetic over fixed byte
/// arrays and the running Aspire stack has no opinion on it.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class CorruptSignatureTests
{
    // Arbitrary, fixed base64url strings — never decoded, only checked for
    // byte-identical passthrough. Real JWT segments are not needed: nothing
    // here parses header or payload as JSON.
    private const string Header = "eyJhbGciOiJIUzUxMiJ9";
    private const string Payload = "eyJzdWIiOiJwcm9iZSJ9";

    /// <summary>
    /// The case spec §2 identifies as the observed failure: a 64-byte
    /// (HS512-shaped) signature whose canonical encoding ends in 'A'. Red
    /// against the unmodified helper — it flips 'A' to 'B', which changes
    /// only the four padding bits a decoder discards, so the decoded bytes
    /// below come back unchanged.
    /// </summary>
    [Fact]
    public void A_64_byte_signature_ending_in_A_is_corrupted_at_the_decoded_byte_level()
    {
        byte[] original = AllZeroSignature(length: 64, lastByte: 0);
        string token = Token(original);
        token[^1].ShouldBe(
            'A', "this fixture must produce the last-character-'A' encoding spec §2 identifies as "
            + "the observed failure, or the case below is not the one it claims to be.");

        AssertSignatureIsCorruptedAtByteLevel(token, original, context: "64-byte signature ending 'A'");
    }

    /// <summary>
    /// The other three canonical last characters of a 64-byte signature
    /// (spec §2's table). These already pass against the unmodified helper —
    /// pinned here so the fix in T002 cannot regress them.
    /// </summary>
    [Theory]
    [InlineData(1, 'Q')]
    [InlineData(2, 'g')]
    [InlineData(3, 'w')]
    public void A_64_byte_signature_ending_in_Q_g_or_w_is_already_corrupted_at_the_decoded_byte_level(
        int lastByteValue, char expectedLastCharacter)
    {
        byte[] original = AllZeroSignature(length: 64, lastByte: (byte)lastByteValue);
        string token = Token(original);
        token[^1].ShouldBe(
            expectedLastCharacter, "this fixture must produce the last character the theory case names, "
            + "or the case below is not exercising the encoding it claims to.");

        AssertSignatureIsCorruptedAtByteLevel(
            token, original, context: $"64-byte signature ending '{expectedLastCharacter}'");
    }

    /// <summary>
    /// Spec §4's algorithm-independence scenario: a 32-byte (HS256-shaped)
    /// and a 256-byte (RS256-shaped) signature, each ending in 'A'. Red
    /// against the unmodified helper for the same reason as the 64-byte
    /// case — the bug is about which bits the last character carries, not
    /// about HS512 specifically.
    /// </summary>
    [Theory]
    [InlineData(32)]
    [InlineData(256)]
    public void Other_signature_lengths_ending_in_A_are_corrupted_at_the_decoded_byte_level(int signatureLength)
    {
        byte[] original = AllZeroSignature(signatureLength, lastByte: 0);
        string token = Token(original);
        token[^1].ShouldBe(
            'A', $"this {signatureLength}-byte fixture must produce a last-character-'A' encoding, "
            + "or the case below is not the one it claims to be.");

        AssertSignatureIsCorruptedAtByteLevel(
            token, original, context: $"{signatureLength}-byte signature ending 'A'");
    }

    /// <summary>
    /// The header and payload segments — the parts naming the session and
    /// subject — must come back untouched. Passes against the unmodified
    /// helper already; pinned so the fix cannot widen the corruption beyond
    /// the signature segment.
    /// </summary>
    [Fact]
    public void Header_and_payload_segments_are_returned_byte_identical()
    {
        byte[] original = AllZeroSignature(length: 64, lastByte: 0);
        string token = Token(original);

        string corrupted = LockoutSessionSurvivalIntegrationTests.CorruptSignature(token);
        string[] segments = corrupted.Split('.');

        segments[0].ShouldBe(Header, "the header segment must be untouched by a signature-only corruption.");
        segments[1].ShouldBe(Payload, "the payload segment must be untouched by a signature-only corruption.");
    }

    /// <summary>
    /// The segment-count assertion is unchanged scope (spec §6): a string
    /// that is not a three-segment JWT must still fail loudly rather than
    /// silently corrupting something that was never a valid token shape.
    /// </summary>
    [Theory]
    [InlineData("only-two.segments")]
    [InlineData("a.b.c.d")]
    [InlineData("no-dots-at-all")]
    public void A_token_that_is_not_three_segments_fails_loudly(string malformed)
    {
        Should.Throw<ShouldAssertException>(() => LockoutSessionSurvivalIntegrationTests.CorruptSignature(malformed));
    }

    /// <summary>
    /// Applies <see cref="LockoutSessionSurvivalIntegrationTests.CorruptSignature"/>,
    /// then decodes the resulting signature segment and asserts it differs
    /// from <paramref name="originalSignature"/> at the byte level.
    ///
    /// <para>
    /// A padding-bits-only flip (the bug) does not always decode to
    /// identical bytes under .NET's own canonical <see cref="Base64Url"/>
    /// decoder — it can instead fail to decode at all, because the decoder
    /// rejects a base64url quantum whose unused low bits are non-zero. Both
    /// outcomes are the same underlying defect (the corrupted signature is
    /// not a genuinely different, validly-encoded signature), so both are
    /// reported as one assertion failure rather than an uncaught
    /// <see cref="FormatException"/> leaking a raw stack trace.
    /// </para>
    /// </summary>
    private static void AssertSignatureIsCorruptedAtByteLevel(string originalToken, byte[] originalSignature, string context)
    {
        string corrupted = LockoutSessionSurvivalIntegrationTests.CorruptSignature(originalToken);
        string corruptedSegment = corrupted.Split('.')[2];

        byte[]? corruptedBytes;
        try
        {
            corruptedBytes = Base64Url.DecodeFromChars(corruptedSegment);
        }
        catch (FormatException exception)
        {
            throw new ShouldAssertException(
                $"{context}: the corrupted signature segment did not even decode as base64url "
                + $"({exception.Message}) — a corrupted refresh token's signature must decode to "
                + "genuinely different, validly-encoded bytes, not fail to decode at all.");
        }

        corruptedBytes.SequenceEqual(originalSignature).ShouldBeFalse(
            $"{context}: a corrupted refresh token's signature must decode to different bytes than "
            + "the original — flipping only the last character's padding bits leaves the decoded "
            + "signature unchanged, and Keycloak verifies decoded bytes, never the base64url spelling.");
    }

    private static byte[] AllZeroSignature(int length, byte lastByte)
    {
        byte[] bytes = new byte[length];
        bytes[^1] = lastByte;
        return bytes;
    }

    private static string Token(byte[] signature) =>
        $"{Header}.{Payload}.{Base64Url.EncodeToString(signature)}";
}
