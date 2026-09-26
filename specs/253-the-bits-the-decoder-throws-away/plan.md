# Plan 253: The bits the decoder throws away

**Spec**: [spec.md](spec.md) · **Issue**: #2533 · **Phase**: 2 (Plan)

## Bounded context and layers

This is test code only, in `tests/Integration.Tests/Identity/`. There are no domain, application,
infrastructure or API layers, no entities or value objects, and no messaging. It touches no
cross-context reference, so NetArchTest boundary rules are unaffected. §II (primitives) does not apply,
because this is test code and not a domain model.

## Design

### The corruption, fixed

`CorruptSignature(string jwt)` keeps its signature, its three-segment assertion and its contract: header
and payload are byte-identical, and the signature is invalid. The body changes from "swap the last
character" to "change a decoded byte":

1. Split on `.` and assert 3 segments (unchanged).
2. Decode segment 3 with `System.Buffers.Text.Base64Url.DecodeFromChars` (BCL, .NET 9+). This adds no
   package reference. `Microsoft.IdentityModel`'s `Base64UrlEncoder` is used elsewhere in `tests/`, but
   only in `StreamDistribution.Infrastructure.Tests`, and a BCL call beats a new reference.
3. Flip one bit of the **first** decoded byte (`bytes[0] ^= 0x01`). The first byte is fully significant
   in every encoding, so this is algorithm- and length-independent.
4. Re-encode with `Base64Url.EncodeToString` (unpadded, as JWS requires) and reassemble.
5. **Self-check**: assert that the re-decoded corrupted signature differs from the original bytes. That
   is true by construction, but it turns any future edit that reintroduces a padding-bit-only change into
   a loud failure. Without the check that edit would be a silent ¼-flake.

Why not simply "flip the first character"? It works, but it reasons about the encoding again. Operating
on decoded bytes states the property the fact depends on: *the bytes Keycloak verifies differ*.

### Visibility

`private static` becomes `internal static`, so that the fixture-free test class can call it. The
Integration.Tests assembly already hosts fixture-free classes (`AttributionVerdictTests`,
`AppHost*Tests`), so this is not a new pattern.

### Doc comment

The `CorruptSignature` summary and the fact's `<para>` ("flips the last character of the refresh
token's signature segment") are updated to describe the byte-level flip. One sentence records why the
last character is unusable (padding bits). This is a *why* comment in the ADR-0036 sense: the next
reader would otherwise "simplify" it back.

### New test file

`tests/Integration.Tests/Identity/CorruptSignatureTests.cs` is a plain class with **no**
`[Collection(AspireCollection.Name)]`, so it runs without booting a stack. It builds synthetic tokens
from fixed byte arrays, so every case is deterministic:

- 64-byte signatures whose final byte's low 2 bits are `00`, `01`, `10` and `11` (last char `A`/`Q`/`g`/`w`).
- A 32-byte and a 256-byte all-zero signature (last char `A`), covering HS256 and RS256 shapes.
- Header/payload: any fixed base64url strings. The test asserts they are returned byte-identical.
- Assertion: the corrupted third segment decodes to bytes that differ from the original's (decode in
  the test with `Base64Url.DecodeFromChars`; never compare strings).

The test must not assert only that the *string* changed. The broken helper changes the string too, and
that exact assertion shape is the bug.

## Latency, observability, migrations

None of these apply. There is no runtime change.

## Contention (ADR-0109)

There is no shared contention file. `LockoutSessionSurvivalIntegrationTests.cs` has no other open
changes (last touched `44160c71`).

## Risks

- **Base64url decoding of Keycloak's signature**: Keycloak emits unpadded base64url, which
  `Base64Url.DecodeFromChars` accepts. The observed 86-character signature decodes to 64 bytes
  (spec §2), so this is verified rather than assumed.
