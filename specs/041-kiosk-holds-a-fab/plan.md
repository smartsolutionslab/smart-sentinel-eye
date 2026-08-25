# Implementation Plan: The kiosk holds a fab, and holds only what a kiosk needs

**Branch**: `041-kiosk-holds-a-fab` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/041-kiosk-holds-a-fab/spec.md`

## Summary

Point `apps/kiosk-web` at the `kiosk-web` realm client instead of the legacy
`smart-sentinel-eye-kiosk`, so its token carries a fab and only the
kiosk permission set; delete the legacy client; make the e2e check able to tell a
working kiosk from a broken one; and guard the kiosk's scopes against drifting
from the bundle every enrolled device gets.

**Phase 0 found two blockers the spec did not know about, and they change the
size of this.** Both were found by minting the intended client's token and
reading it, and neither is visible to any check that exists:

- **The intended client's access token carries no `sub`**, because this realm's
  own `clientScopes` array replaced Keycloak's built-in `basic` scope and nothing
  else supplies one. `WhepAuthValidator` refuses a token it cannot attribute, so
  **every video connection would be refused**. The wall would render with dark
  tiles.
- **The WHEP gate requires `sse.management`** — hard-coded, and the only gate in
  the product not using `RequireScopeExtensions`' granular-or-grandfathered rule.
  A view-only kiosk cannot pass it. This is not new and not ours:
  **no enrolled physical kiosk device has ever been able to watch video either**,
  because `KeycloakScopeBundles.Kiosk` carries no `sse.management`. It
  contradicts constitution §VIII, which says kiosks hold view-only scopes.

FR-005 anticipated exactly this and said to raise it rather than widen the set.
Both are raised in full (research R3, R4) and both are fixed **without giving the
kiosk one extra permission**: a `sub` mapper is an identity claim, and correcting
a gate to ask for `sse.streams.read` narrows what is demanded rather than
widening what is held.

## Technical Context

**Language/Version**: TypeScript 5 / React 19 (kiosk-web); C# / .NET 10 (StreamDistribution, Architecture.Tests); JSON (Keycloak realm)

**Primary Dependencies**: `react-oidc-context` (ADR-0080), Keycloak 26.5 per fab (ADR-0007/0008), Playwright (ADR-0108), xUnit + Shouldly (ADR-0052)

**Storage**: none — no schema, no migration, no domain state

**Testing**: Playwright e2e against a live `aspire run` stack; xUnit convention test in `tests/Architecture.Tests`; existing kiosk vitest suites must pass **untouched** (FR-011)

**Target Platform**: browser kiosk on a fab wall (`:5174`), dev stack only — there is no production deployment

**Project Type**: web (frontend app + realm configuration + one backend authorization gate)

**Performance Goals**: unchanged. Nothing on the latency path is touched; the change is which token is presented.

**Constraints**: the realm re-imports on container start, so a realm edit needs a Keycloak restart; the realm-description length limit is a known hazard; CI can produce no video, so the WHEP path has **no automated coverage in either direction**

**Scale/Scope**: one client id, one scope string, one client deleted, one protocol mapper added, one `const` corrected, one e2e assertion reshaped, one seeding project, one convention test, three documents corrected

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **VIII. Safe by Default at Trust Boundaries** — *"Kiosks authenticate with device-bound credentials and **view-only scopes**"* | **This feature discharges it.** The browser kiosk holds `sse.management` today, in plain contradiction. Research R4 found the other half: the WHEP gate makes view-only scopes insufficient to *view*, so the principle was unsatisfiable for every kiosk, not just this one. Both halves are corrected. |
| **IV. The Latency Budget Is Sacred** | Not on the path. No leg changes; no measurement is added or removed. The feature unblocks spec 040's two unread figures by letting a kiosk reach a wall, and claims nothing about them. |
| **VII. Observability Is Non-Negotiable** | No new leg, no new sink. Untouched. |
| **III. Bounded Context Isolation** | The one backend edit is inside StreamDistribution's Application layer. No cross-context reference is added. The new convention test reads `Identity.Application`'s public constant, which `Architecture.Tests` already references. |
| **II. DDD with Value Objects** | No domain type is added or changed. A scope string is configuration, not domain state. |
| **V. Spec-Driven Development** | Spec -> plan -> tasks -> implement -> verify -> QA -> PR, gates observed. |
| **Security NFR** — *"Token-bound, short-lived credentials. No long-lived secrets in browsers."* | Unchanged: still a public client with PKCE S256 and no secret. Strictly improved by dropping the management scope. |
| **Testing** — TDD for domain; integration against the real stack | No domain. The assertions are an e2e against the live Aspire stack and a convention test; both are written to fail first (see "What must fail"). |

**No violation to justify.** No new dependency, no new abstraction, no
speculative generality.

**Post-design re-check**: unchanged. The design adds one xUnit file, one
Playwright setup file, one protocol mapper, and edits four existing files. The
`AuthorizeWhepCommandHandler` change replaces a hand-rolled scope check with the
rule the rest of the codebase uses — it removes a divergence rather than adding
one.

## Project Structure

### Documentation (this feature)

```text
specs/041-kiosk-holds-a-fab/
├── plan.md                          # this file
├── spec.md
├── research.md                      # Phase 0 — R1..R11, including both blockers
├── quickstart.md                    # the manual half CI cannot do
├── checklists/requirements.md
├── contracts/
│   └── the-kiosk-identity.md        # what a kiosk holds, and what is asked of it
└── tasks.md                         # /speckit-tasks — not created here
```

**No `data-model.md`.** Nothing persists. A client id and a scope set are
configuration; a latency figure was telemetry (spec 040 skipped it for the same
reason). Writing an empty one would be a document asserting a model that does not
exist — the failure mode this feature exists to correct.

### Source Code (repository root)

```text
apps/kiosk-web/src/app/auth.ts                 # client id + scope + the doc comment
apps/kiosk-web/src/features/revocation/
  useLayoutLifecycle.ts                        # doc comment names the wrong scope

src/AppHost/Realms/smart-sentinel-eye-realm.json
                                               # delete smart-sentinel-eye-kiosk
                                               # add oidc-sub-mapper to kiosk-web

src/StreamDistribution/Application/Commands/
  Handlers/AuthorizeWhepCommandHandler.cs      # the gate (R4)
  AuthorizeWhepErrors.cs                       # its message names the scope

src/Identity/Application/KeycloakAdmin/
  KeycloakScopeBundles.cs                      # doc comment cites a test that does not exist

tests/Architecture.Tests/
  KioskScopeParityTests.cs                     # NEW — FR-009
tests/StreamDistribution.Application.Tests/
  Commands/AuthorizeWhepCommandHandlerTests.cs # the kiosk persona is admitted; anonymous still refused

e2e/
  kiosk-live-updates.spec.ts                   # assertion reshaped (FR-007)
  kiosk-shows-a-wall.spec.ts                   # NEW — US1
  support/seed-published-layout.setup.ts       # NEW — R8
  support/kiosk-session.ts                     # NEW — sign-in + token read (R9)
playwright.config.ts                           # the seed project + kiosk dependency

.claude/agents/frontend-engineer.md            # names the wrong client (FR-010)
```

## Approach

Four increments, in this order. The order matters: **the client switch is
sequenced after the two blockers**, because switching first produces a kiosk that
signs in and shows dark tiles, and a dark tile is exactly the kind of
half-success that gets accepted.

### 1. Make the intended identity usable (blockers first)

Add the `oidc-sub-mapper` to `kiosk-web`. Change
`AuthorizeWhepCommandHandler.RequiredScope` to `sse.streams.read` and accept
`sse.management` through the same grandfather rule
`RequireScopeExtensions.LegacyManagementBundle` already names, so management-web
— which holds `sse.management` and *not* `sse.streams.read` — keeps working.
Extend the handler's tests: a kiosk-persona token is admitted, a token with
neither is refused, an unattributable token is still refused.

Nothing in the kiosk changes yet. At the end of this increment the intended
identity is capable of the kiosk's job; nothing uses it.

### 2. Switch the kiosk and retire the legacy client

`client_id: 'kiosk-web'`, `scope: 'openid'` — **in the same commit**, because
requesting `sse.management` from a client that does not have it returns
`invalid_scope` and no token at all (research R1). Rewrite the doc comment, which
currently explains the wrong choice. Delete `smart-sentinel-eye-kiosk` from the
realm. Correct `.claude/agents/frontend-engineer.md` and
`useLayoutLifecycle.ts`'s scope claim.

### 3. Make the failure impossible to hide

Seed a published layout (`e2e/support/seed-published-layout.setup.ts`, a
Playwright setup project the `kiosk` project depends on). Replace the
three-way regex — which accepts *"could not load layouts"* as a pass — with a
sign-in helper that lands on the picker **with layouts on it**, and add
`kiosk-shows-a-wall.spec.ts`, which opens one and asserts `layout-grid` and at
least one `layout-tile`.

Then assert the token itself, because the wall cannot show what the token does
*not* carry: `groups` present, `sse.management` absent, the `sse.*` set equal to
the bundle.

### 4. Stop the two definitions drifting

`tests/Architecture.Tests/KioskScopeParityTests.cs` compares the realm's
`kiosk-web` default scopes against `KeycloakScopeBundles.Kiosk` as sets, both
directions, reading both live. Correct `KeycloakScopeBundles.cs`'s doc comment,
which cites a `ScopeBundleTests` that has never existed.

## What must fail

Every assertion here is checked by breaking the thing it guards, not by reasoning
that it would fail:

| Break | Expected |
|---|---|
| `auth.ts` back on the legacy client | the kiosk e2e goes red — **SC-004, demonstrated by causing it** |
| a scope added to either side of the parity pair | `KioskScopeParityTests` fails |
| `sse.management` restored on the kiosk | the token assertion fails while every behavioural check still passes |
| the `sub` mapper removed | video stops — **and nothing automated catches it** |

The last row is the honest one. CI produces no video, so the WHEP path has no
automated coverage in either direction, and Phase 5 is where both blockers are
actually observed to be fixed.

## Risks

**The two blockers are only provable by hand.** Everything CI can check will be
green whether or not video works. `quickstart.md` is the procedure; the PR must
say plainly which claims rest on it. Spec 040 shipped a draft PR for exactly this
reason and this feature is what unblocks its two unread figures — so a Phase 5
that gets skipped here would leave *two* features resting on unread evidence.

**The seed project is new machinery in the e2e suite.** If it fails, every kiosk
spec fails with it, and the failure will look like a kiosk defect. It stays as
small as possible and reuses `signInAsOperator` rather than inventing a second
sign-in path.

**Widening the WHEP gate is a security-adjacent edit.** It is a *narrowing* of
what is demanded, and the grandfather clause keeps management working — but it is
the one change here that a reviewer should look at twice, and it is why this
feature does not skip Phase 6.

## Out of scope

- **`management-web` and `smart-sentinel-eye-web`.** The identical trap sits one
  client over: the replacement client is unused and would hit blocker A the
  moment anyone pointed the app at it. Documented in research R10, not touched.
- **Restoring a `basic`-equivalent realm scope** (research R2), the root cause
  behind blocker A. The narrow fix here is one mapper on one client.
- **Per-device kiosk enrolment**, which already grants the right bundle.
- **Spec 040's two latency figures.** This unblocks them; it does not read them.
- **Any production rollout.** There is no production deployment.
