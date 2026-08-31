# Implementation Plan: A wall stays up past its own session ceiling

**Branch**: `052-wall-past-its-ceiling` | **Date**: 2026-08-31 | **Spec**: [spec.md](./spec.md)

**Input**: [spec.md](./spec.md) · **Research**: [research.md](./research.md) · **Data model**: [data-model.md](./data-model.md) · **Contract**: [contracts/wall-display-grant.md](./contracts/wall-display-grant.md) · **Quickstart**: [quickstart.md](./quickstart.md)

---

## Summary

A wall drops to a sign-in prompt roughly twice a day because the sign-in session
ends on a clock. The privilege that fixes it also lets the holder mint
credentials that never expire — and **every account this system creates already
holds it**, which is why the previous attempt was withdrawn.

**The approach**: contain first, then use. Every account the system creates has
the privilege removed as it is created, using authority the identity service
already has. Only then does a **separate wall client** offer the scope — and
because scopes belong to clients, that client is also where a wall display's
authority is narrowed, so "a screen may only show a wall" becomes a property of
the configuration rather than a sentence in a document.

**US4 — twenty screens and a real power cut — is not built.** It exists so the
record cannot absorb it. Said once here; the verification note says it again.

---

## Technical Context

**Language/Version**: C# / .NET 10 (Identity context), TypeScript 5.x / React 19 (`kiosk-web`)

**Primary Dependencies**: existing — Keycloak admin API via `HttpKeycloakAdminClient`, `oidc-client-ts`. **Nothing new.**

**Storage**: none. The realm file is configuration; no schema changes, no migration.

**Testing**: xUnit for the Identity changes; Vitest for the client/scope selection; Playwright against the live stack; architecture tests for the realm's shape

**Target Platform**: Aspire dev + CI. **No production deployment exists** (ADR-0130)

**Project Type**: backend + frontend + realm configuration

**Performance Goals**: not a latency feature; no leg of §IV is touched

**Constraints**: no new admin authority may be granted; the realm file is edited line by line, never reserialised

**Scale/Scope**: 20 screens per wall is the target; **four is the most ever exercised**, in spec 051

---

## Constitution Check

*GATE: passed before Phase 0, re-checked after Phase 1.*

| Principle | Assessment |
|---|---|
| **§Availability** | This is the second half of the target. It **does not close it** — twenty screens and a real power cut remain unmeasured, and the record must say so where it reports the successes. |
| **§Security** — token-bound, short-lived credentials; no long-lived secrets in browsers | **This is the tension, and it is the reason for US1.** A never-expiring grant is admitted deliberately, confined to accounts that show cameras, narrowed to read-only by construction, and the containment lands *before* the widening. No secret is placed in a browser: the grant belongs to the screen and is revocable on its own. |
| **§IV latency budget** | Untouched. |
| **DDD, value objects, no cross-context references** | Identity only; no context boundary crossed. |
| **ADR-0065 coverage gates** | **These apply** — Identity's Application layer is touched, so the ≥80% Application threshold is live. Unlike specs 050 and 051, a coverage number *is* legitimate evidence here. |
| **ADR-0030 / ADR-0087** | Followed. |

**No ADR contradicts this feature** — checked and recorded (research §R0),
including the near-miss where ADR-0113 forbids automatic retry of *concurrency
conflicts*. **No amendment gate applies.** A new ADR at Phase 5 should
**supersede ADR-0132** rather than leave two records of the same idea.

---

## Project Structure

```text
src/Identity/
  Application/Kiosks/          strip on enrolment; the startup sweep
  Infrastructure/KeycloakAdmin/ the two admin calls (read direct mappings, delete them)
src/AppHost/Realms/
  smart-sentinel-eye-realm.json  the kiosk-wall client — edited line by line
apps/kiosk-web/src/app/
  auth.ts                      mode → client id + requested scopes
  identityFailure.ts           not_allowed becomes a refusal
tests/Architecture.Tests/       the realm's shape
e2e/                            the wall outlives its session; the grant's authority
```

---

## Approach, and the four decisions

### 1. Contain, and do it with authority already held

Strip `default-roles-<realm>` from each account **as the system creates it**, and
sweep existing kiosk accounts at startup. Measured against the real service
account: allowed, leaves the account holding nothing, leaves the kiosk still able
to obtain a token, **idempotent**.

**The call shape is not obvious**: read the *direct* mappings, then delete that
same list. Anything else returns 404, which reads like a permission failure and
is not.

**If the strip fails, the enrolment fails.** An enrolment that reports success
while leaving a privilege holder behind is the outcome worth avoiding; the strip
is idempotent, so a retry is safe.

### 2. A second client, because scopes live on clients

One deployment flag selects both the client and the requested scopes, so there
is no half-configuration:

| Mode | client | requests | carries |
|---|---|---|---|
| default | `kiosk-web` | `openid` | today's scopes, including `sse.events.write` |
| wall | `kiosk-wall` | `openid offline_access` | the five **read** scopes and no write |

**This is what makes US3 true by construction.** Spec 050 wrote that a wall
account could change nothing while its grant carried a write scope. Here the
authority is simply not in the grant.

### 3. `not_allowed` becomes a refusal (spec 051's rule)

A wall-mode screen with an operator account is refused with `not_allowed`. That
code is not in spec 051's refused set, so such a screen would sit on
"Reconnecting" and retry **forever**, telling a passer-by it will clear.

This feature makes that code reachable, so this feature fixes it.

### 4. The ceiling is proved by the grant's type, not by waiting

**Primary**: decode the refresh token and assert it is an offline grant with **no
expiry**. Exact, fast, cannot pass by accident.

**Secondary, gated**: shorten the ceiling on a test realm. Spec 050 did this and
**broke the e2e seeds** — long operator sessions expiring mid-run — so it is run
deliberately, never by default, and every place that reports it says it
demonstrates the mechanism and **not** the production configuration.

---

## Done, per story — before any code

| Story | Verifiable criterion |
|---|---|
| **US1** | Enrol a kiosk against a running provider, then **ask the provider** what that account effectively holds. The privilege is absent. Same question of an operator: absent. Of a wall display: present. |
| **US2** | A screen signed in as a wall display holds a refresh token whose **type is offline and which carries no expiry** — decoded, not counted. And it comes back from a restart that outlasts an ordinary session. |
| **US3** | The scopes are read **out of the token the wall account receives**, and every one is exercised. `sse.events.write` is absent, and a write attempt is refused. |
| **US4** | **Not built.** No criterion, deliberately. |

---

## What the checks will and will not prove

| Claim | Proved by | **Not** proved by |
|---|---|---|
| An enrolled kiosk holds no privilege | asking the running provider | an architecture test reading the realm file |
| Only wall displays hold it | enumerating accounts **in the provider** | the file listing four |
| The grant outlives a session | decoding the token's type | asserting a token exists |
| A wall display cannot write | the scope's **absence** from the issued token | three endpoints returning 403 |
| Operators gained nothing | signing an operator in and reading their token | not having edited their account |
| A wall survives ten hours | **nothing** | a shortened ceiling, which shows the mechanism |
| **Twenty screens** | **nothing** | four, once |
| **A real power cut** | **nothing** | a reload |
| An account made by hand is contained | **nothing — it is not** | this feature, which excludes it (FR-002a) |

**The specific trap, named because this repository has fallen into it**: US1's
claim is about a *running provider*, and the cheap check reads a *file*. Spec
050's architecture guard passed for the entire feature while the claim it stood
for was false. Every US1 check here asks the provider; the file-reading guard is
kept only for what it can honestly cover — the shape of the realm as declared.

---

## Risks

1. **The wall client is missing a scope the wall actually needs.** No call to
   event ingestion appears in the kiosk source, but "signs in" and "renders a
   wall" are different claims. The end-to-end test must open a wall, not just
   authenticate.
2. **The sweep's definition of "a kiosk account" drifts** from what enrolment
   creates, so it silently covers less than it appears to. It must be derived
   from the same naming enrolment uses, not a pattern typed twice.
3. **A shortened-ceiling run is read as a ten-hour one.** It is gated and
   labelled in three places, and spec 050 shows three is barely enough.
4. **The strip is applied to something that is not a kiosk.** It removes every
   direct realm mapping; on a human account that would be destructive. It must be
   reachable only for accounts enrolment created.
5. **Reading this feature as closing §Availability.**

---

## Out of scope, filed rather than implied

An account created by hand in the provider's console (FR-002a, filed);
per-device identity; enrolment, rotation and revocation as operator workflows;
the provider's session-timing defaults; `management-web`.
