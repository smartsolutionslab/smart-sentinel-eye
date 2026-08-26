# Implementation Plan: The configuration stops discarding what it says

**Branch**: `042-client-identity-claim` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/042-client-identity-claim/spec.md`

## Summary

Add one shared realm client scope that supplies the holder's subject; take that
responsibility away from a permission and from a client's private copy; delete
the four scope names on every client that resolve to nothing; and add checks so
neither failure can recur silently.

**Phase 0 corrected the spec's central claim before any of this was designed.**
The draft said six of eight identities could not name their holder; measured one
token at a time, **one** cannot. Background workers get the subject from the way
they sign in — a property of the grant, not of any file — so a mapper was never
relevant to them. The spec was rewritten around the mechanism instead of the
count, and the error is kept in its Assumptions.

What remains is smaller than the draft implied and still worth doing:

- **Thirty-two entries are discarded on every start**, and the file goes on
  listing them. That silence has hidden two defects in two weeks.
- **One identity cannot be attributed** — the unused replacement for the operator
  console, which would refuse all seventeen kinds of attributed change the moment
  anyone adopted it.
- **Three unrelated mechanisms supply one fact**, two of them accidents.
- **Nothing checks any of it.**

## Technical Context

**Language/Version**: JSON (Keycloak realm import); C# / .NET 10 (Architecture.Tests, Integration.Tests)

**Primary Dependencies**: Keycloak 26.5 per fab (ADR-0007/0008), imported by Aspire at start-up

**Storage**: none — no schema, no migration, no domain state

**Testing**: xUnit + Shouldly (ADR-0052); convention checks in `tests/Architecture.Tests`; one new end-to-end assertion through the Aspire fixture (ADR-0103)

**Target Platform**: the development stack. There is no production deployment.

**Project Type**: configuration, plus the checks that keep it honest

**Performance Goals**: unchanged. Nothing on the latency path is touched.

**Constraints**: the realm imports only into an empty database, so verifying a change needs the Keycloak container **and its data volume** removed — a restart keeps the old realm and the stack looks healthy either way; client `description` over 255 characters kills the import

**Scale/Scope**: one scope added, two mappers removed, one client mapper removed, thirty-two list entries removed across eight clients, two convention assertions, one integration test

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **VIII. Safe by Default at Trust Boundaries** — authorization mediated, kiosks view-only | **Strengthened.** Today one client can be attributed *only because* it holds administrative authority; narrowing that permission would silently make its actions unattributable. This separates being identifiable from being privileged, which is what §VIII assumes and the realm does not currently implement. No permission changes (FR-010, SC-007). |
| **Audit** — *"All admin and config writes appear in the audit log"* (Security NFR) | **This is the principle at stake.** A write that cannot be attributed is refused rather than logged wrongly, so today the NFR is upheld by refusing to work. R9 found that **nothing asserts an attributed write end to end**; this adds the first. |
| **IV. The Latency Budget Is Sacred** | Not on the path. No leg changes, no measurement added or removed. |
| **VII. Observability Is Non-Negotiable** | No new leg, no new sink. Untouched. |
| **III. Bounded Context Isolation** | No cross-context reference added. The convention test reads a configuration file and `Identity.Application`'s public constants, both of which `Architecture.Tests` already references. |
| **II. DDD with Value Objects** | No domain type added or changed. A scope is configuration. |
| **V. Spec-Driven Development** | Spec → plan → tasks → implement → verify → QA → PR, gates observed. The spec was rewritten mid-Phase-0 rather than planned around a claim that had failed. |
| **IX. No speculative generality** (Karpathy, ADR-0036) | Directly applied: the discarded scopes are **removed**, not recreated. Three of the four carry claims nothing reads, and rebuilding them would be inventing needs. |

**No violation to justify.** No new dependency, no new abstraction, one new
configuration object replacing three ad-hoc ones.

**Post-design re-check**: unchanged. The design deletes more than it adds — two
mappers, one client mapper and thirty-two list entries out; one scope and three
assertions in.

## Project Structure

### Documentation (this feature)

```text
specs/042-client-identity-claim/
├── plan.md                       # this file
├── spec.md                       # rewritten mid-Phase-0; see its Assumptions
├── research.md                   # R1..R9, including the correction
├── quickstart.md                 # the per-identity verification
├── checklists/requirements.md
├── contracts/
│   └── what-a-credential-carries.md
└── tasks.md                      # /speckit-tasks — not created here
```

**No `data-model.md`.** Nothing persists; a scope and a claim are configuration.
Specs 040 and 041 skipped it for the same reason. Writing an empty one would
assert a model that does not exist — this feature's own subject.

### Source Code (repository root)

```text
src/AppHost/Realms/smart-sentinel-eye-realm.json
    + sse-identity client scope (one oidc-sub-mapper, include.in.token.scope false)
    - sse.management's sub-claim and preferred-username-claim mappers
    - kiosk-web's client-level sub mapper (spec 041's narrow fix)
    ± all eight defaultClientScopes: sse-identity in, four inert names out

tests/Architecture.Tests/
    RealmIdentityTests.cs          # NEW — every client can name itself;
                                   #       every scope named actually exists
tests/Integration.Tests/
    <context>/…AttributionIntegrationTests.cs
                                   # NEW — a minted token makes an attributed
                                   #       write, and it lands attributed
```

## Approach

Four increments. The order is chosen so that nothing depends on an unverified
claim.

### 1. The shared definition

Add `sse-identity`, carrying one subject mapper and no permission, with
`include.in.token.scope: false` so it never appears among what a caller may do.
The hyphen is not decoration: `sse-groups` already carries a claim and grants
nothing, while `sse.*` grants. The realm has followed that rule without stating
it, and the only scope carrying identity claims was a permission.

Assign it to all eight clients.

### 2. Take the responsibility off the things that hold it by accident

Remove both mappers from `sse.management`, and the private mapper from
`kiosk-web`. After this, no permission decides whether its holder can be named,
and no client carries its own copy.

**One behaviour changes**: `preferred_username` leaves
`smart-sentinel-eye-web`'s token. Nothing reads it (verified), and FR-009 says
nothing beyond the identifier.

### 3. Stop the file saying what it does not do

Delete `basic`, `profile`, `email` and `roles` from all eight lists. They resolve
to nothing today; the change is to what the file claims, not to what the system
does.

### 4. Make both failures loud

A convention check that every client holds the identity scope and that every
scope any client names exists. Then the one thing a file cannot show: an
integration test that **mints a token, makes an attributed write, and finds it
attributed**. R9 found nothing does this today — every existing test fabricates
its operator — which is exactly how a client that cannot be attributed went
unnoticed.

## What must fail

| Break this | Expected |
|---|---|
| Remove `sse-identity` from a client | the convention check fails |
| Give a client a scope the realm does not define | the convention check fails — today it is discarded with a warning |
| Remove the subject mapper from `sse-identity` | the **integration** test fails; the convention check does **not** — it reads names, not behaviour |
| Import the realm | zero discarded entries, down from thirty-two |

The third row is the honest one: the cheap check and the expensive one catch
different things, and neither alone is enough.

## Risks

**Every identity in the realm changes at once.** If the shared scope were wrong,
everything would fail together — which is why R4 measured a token per identity
against the candidate realm before this plan was written, rather than after.

**A realm edit is invisible without deleting the volume.** The container is
persistent with a data volume; restarting keeps the old realm and the stack looks
healthy. Verification that skips this step verifies the previous realm.

**`preferred_username` disappears.** Verified unread, but it is the one thing here
that changes what a token contains rather than where it comes from, and a reviewer
should see it named rather than discover it.

## Out of scope

- **Pointing the operator console at its replacement identity.** This makes it
  possible; it does not do it.
- **Adding the scope to the runtime-created client bundles.** R3 measured those
  clients as already carrying the subject; the scope would be inert for them.
- **Restoring `profile`, `email` or `roles` in any form.**
- **Anything either app does**, and the two findings filed separately alongside
  this one.
