# Spec 253 — The bits the decoder throws away

**Issue**: #2533 · **Branch**: `investigate-2533-refresh-token-signature` · **Phase**: 1 (Specify)
**Date**: 2026-09-25 · **Base**: `a54b11d0` (`origin/develop`, fetched 2026-09-25)
**Context**: Identity integration-test code only — `tests/Integration.Tests/Identity/`. No `src/`,
`apps/`, realm, AppHost, contract or CI-workflow change.
**Engineer**: `backend-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `backend-reviewer`
**Lane**: autonomous (ADR-0144)
**Phase 4a colour**: **red** (§5). The corruption helper's output changes, and the old output can be
observed failing deterministically.
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane; 4a/4b split), ADR-0139 (§Testing: new behaviour
observed red first), ADR-0036 (smallest change), ADR-0103 (Aspire fixture for the integration fact),
ADR-0052/0053 (xUnit + Shouldly, sentence-style names).
**Constitution**: §IV: **N/A**. No leg of the event→overlay path is touched. §Testing: behaviour-changing,
red first.
**New ADR needed**: **No.** No architectural decision is made. A broken test helper is repaired.
**Security escalation**: **No.** See §3. Keycloak verifies the refresh token's HMAC. The test
never corrupted the signature it thought it corrupted.

---

## 1. The premise, re-checked

| # | Issue claim | Status |
|---|---|---|
| 1 | `A_malformed_refresh_token_is_refused_without_moving_the_failure_counter` got `200` where it expected `400` | **Holds**, intermittently. About 1 run in 4, not every run (§2). |
| 2 | `CorruptSignature` flips the last char of the third segment and leaves header and payload byte-identical | **Holds.** `LockoutSessionSurvivalIntegrationTests.cs:407-418`: `'A'` becomes `'B'`, and every other character becomes `'A'`. |
| 3 | "a single flipped character in a cryptographic signature should fail verification with overwhelming probability" | **False for the last character.** The final base64url character of a signature carries unused padding bits (§2). |
| 4 | Possible cause: Keycloak does not verify the signature, or validates refresh tokens opaquely by session lookup | **Neither.** Keycloak refuses every token whose *decoded* signature bytes differ (19/19, §2). |

## 2. Root cause (observed, not inferred)

Realm refresh tokens are `{"alg":"HS512",...}`. The signature is 64 bytes, which base64url-encodes to 86
characters (86 × 6 = 516 bits for 512 bits of data). **The last character carries 2 significant bits
and 4 padding bits that decoders discard.** A canonical encoder emits one of `A`, `Q`, `g` or `w` there,
each about ¼ of the time.

- Last char `A` (≈ ¼): `A` → `B` changes only padding bits. **The decoded signature bytes are
  identical**, the HMAC verifies, and Keycloak correctly returns `200`.
- Last char `Q` / `g` / `w` (≈ ¾): the flip to `A` changes a significant bit, so the refresh is
  refused with `400 invalid_grant "Invalid refresh token"`.

The test therefore fails about 25% of the time on a correctly behaving server. That fits a test that
passed its own delivery CI (#2509) and then failed on PR #2507's run.

**Evidence.** A standalone container ran the same image as the stack (`quay.io/keycloak/keycloak:26.6.4`,
`AppHost.cs:146`), importing the **unmodified** `src/AppHost/Realms/smart-sentinel-eye-realm.json`. It
used client `management-web` and a throwaway `lockout-session-probe-*` user. There were 24 trials, each
minting a fresh pair and applying `CorruptSignature`'s exact transform:

| Last char | Trials | Decoded bytes identical | Refresh result |
|---|---|---|---|
| `A` → `B` | 5 | 5 / 5 | **200** × 5 |
| `Q`/`g`/`w` → `A` | 19 | 0 / 19 | 400 `invalid_grant` × 19 |

Controls: 4/4 first-character flips were refused (`400 invalid_grant`), and the untouched token was
accepted (`200`). After 19 refused corrupt refreshes the probe's attack-detection record read
`numFailures: 0`, so the fact's *second* claim (the counter does not move) holds once the corruption is
real.

**Not done: the in-harness isolation runs.** A concurrent AppHost from another worktree
(`D:\Github\sse-2526`, issue #2526) held the machine for the whole investigation. One machine runs one
Aspire stack, so this investigation did not boot a second one. The container probe above is the stronger
instrument because it separates the two cases per trial. Phase 5 still owes the in-harness runs (T004).

## 3. Why this is not a security finding

- Keycloak **does** verify the refresh token's HMAC-SHA512 (19/19 byte-level corruptions refused). It is
  not validating by session lookup alone.
- Accepting a signature whose *padding bits* differ is lenient base64url decoding. RFC 4648 §3.5 lets a
  decoder reject non-canonical encodings but does not require it. It is **not forgery**: producing such
  a variant requires already holding the valid token, and it names the same session, subject and expiry.
- The only practical consequence is **textual malleability**: each HS512 token has 16 accepted spellings
  of its last character. That matters only to a component that keys on the token *string* (a string
  denylist, a string-hash replay cache). The realm does not enable `revokeRefreshToken`. Reuse
  detection in Keycloak, where enabled, is session-state based. Nothing in this repo reads refresh
  tokens as strings: `refresh_token` appears in `tests/` only in this file.
  **Recorded as an observation for a human reader, not filed as an issue.**

## 4. User story

**US1 (P1): a corrupted refresh token is always corrupt.** As the maintainer of the Identity integration
suite, I want the malformed-refresh-token fact to present Keycloak with a signature whose *decoded
bytes* differ from the minted one. Then a `200` means a real verification failure and never a
¼-chance artefact of base64url padding.

### Acceptance scenarios

```gherkin
Scenario: HS512-shaped signature ending in 'A' (the observed failure)
  Given a three-segment token whose 64-byte signature encodes with last character 'A'
  When CorruptSignature is applied
  Then the decoded signature bytes differ from the original's
  And the header and payload segments are byte-identical to the original's

Scenario: every canonical last character of a 64-byte signature
  Given signatures whose last character is each of 'A', 'Q', 'g', 'w'
  When CorruptSignature is applied
  Then in every case the decoded signature bytes differ

Scenario: other signature lengths (algorithm-independent)
  Given a 32-byte (HS256-shaped) and a 256-byte (RS256-shaped) signature whose last character is 'A'
  When CorruptSignature is applied
  Then the decoded signature bytes differ

Scenario (bad request): not a three-segment token
  Given a string with fewer or more than three '.'-separated segments
  When CorruptSignature is applied
  Then it fails loudly, as today (segment-count assertion unchanged)

Scenario (end to end): the integration fact against the real realm
  Given the Aspire stack and a freshly minted refresh token for a throwaway probe
  When the corrupted token is posted as grant_type=refresh_token
  Then the response is 400 with error "invalid_grant"
  And the probe's attack-detection numFailures is unchanged
```

There is no auth or conflict scenario: the change adds no endpoint and no state. The auth-relevant
behaviour, refusing a bad signature, is the integration fact itself.

### Independent end-to-end test procedure

1. `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~CorruptSignature"`, fixture-free
   and deterministic, must be green.
2. With no other AppHost running, run
   `--filter "FullyQualifiedName~A_malformed_refresh_token_is_refused_without_moving_the_failure_counter"`
   in isolation **8 times**. Every run must be green. A still-broken helper would pass all 8 by chance
   only (¾)^8 ≈ 10% of the time, which is why step 1 is the deterministic proof and step 2 the end-to-end
   confirmation.

## 5. Phase 4a colour: red

The helper's output changes. Red is observable **deterministically** without a stack: the "last char
`A`" scenarios fail against the current implementation with "decoded bytes identical". The other
scenarios pass against the current implementation, and that is expected: they pin that the fix does not
regress them.

## 6. Scope

**In**: `CorruptSignature` in `LockoutSessionSurvivalIntegrationTests.cs` (visibility and body), its doc
comment, and one new fixture-free test file beside it.

**Out**: do not do any of the following.
- Any assertion in `A_malformed_refresh_token_is_refused_without_moving_the_failure_counter`, or in any
  other fact. The fact's expectations are correct; only its input was wrong.
- The realm file, Keycloak version and client settings. The server behaves correctly (§3).
- Filing the malformation observation (§3) as an issue. That is a human call.
- Sharing the helper with other test files. It has one caller (ADR-0036).
