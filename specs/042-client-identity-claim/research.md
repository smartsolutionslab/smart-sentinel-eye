# Phase 0 Research: The configuration stops discarding what it says

**Feature**: 042 | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

Everything below was minted, not read off the file. Two throwaway Keycloak 26.5
containers were used: one with the realm as it stands, one with the candidate
change. A credential was issued for **every** identity in each.

That method is the point. The first draft of this spec was written from the file
and was wrong about its central number.

---

## R1 — What is actually true, measured one identity at a time

**The spec's first draft said six of eight identities could not name their
holder. One cannot.**

| Identity | Flow | `sub` today | Supplied by |
|---|---|---|---|
| `smart-sentinel-eye-web` | user | yes | `sse.management`'s mapper |
| `kiosk-web` | user | yes | its own client mapper (spec 041) |
| **`management-web`** | user | **no** | — |
| `identity-admin` | client credentials | yes | the grant |
| `migration-runner` | client credentials | yes | the grant |
| `stream-distribution-attribution` | client credentials | yes | the grant |
| `scenario-simulator` | client credentials | yes | the grant |
| `event-ingestion` | client credentials | yes | the grant |

**Decision**: rewrite the spec around the mechanism rather than the count. Done
before planning continued; the error and its cause are kept in the spec's
Assumptions rather than deleted.

**Rationale**: the count was inferred from which clients carry an
`oidc-sub-mapper`. That is the wrong question, because a mapper is not the only
way `sub` arrives — see R2. The right question needed a token.

---

## R2 — Why the background workers were never affected (FR-011)

**Measured.** Every `client_credentials` token carries `sub`, with no `basic`
scope, no mapper, and nothing in the configuration mentioning it: Keycloak sets
the subject from the client's **service-account user**, which exists for exactly
that purpose.

For **user-backed** flows there is no such user of its own, and `sub` reaches the
access token only through a mapper — normally the built-in `basic` scope's, which
this realm does not have (it supplies its own `clientScopes` array, replacing the
built-ins rather than adding to them).

**So the rule is: user flows need a mapper, service accounts do not.** Nothing in
the repository says this, which is why the file appeared to say otherwise and why
FR-011 requires it to be written down. It also means a background worker that
ever acted on a person's behalf would lose the claim silently.

---

## R3 — Clients created at runtime are unaffected too

`EnrollKioskCommandHandler` and `RegisterDeviceCommandHandler` create Keycloak
clients through the Admin API with `DefaultClientScopes: KeycloakScopeBundles.Kiosk`
/ `.Device`. Those clients are **not** in the realm file, so a scope added to
every client in it does not reach them.

**Measured**: a client created through the Admin API with exactly the
representation `EnrollKioskCommandHandler` sends — service account, no standard
flow, the six kiosk scopes and nothing else — mints a token carrying `sub`.
Keycloak assigned it precisely those six default scopes and no others.

**This matters beyond this feature.** The planning input asked whether enrolled
physical kiosks therefore still cannot pass the WHEP gate, which would have made
spec 041's fix incomplete for real devices. **They can.** Recorded because the
opposite was nearly filed as a finding.

**Decision**: do not add the new scope to `KeycloakScopeBundles`. It would be
inert for every holder — they already have `sub` from the grant — and it would
put a non-permission into a bundle that `KioskScopeParityTests` compares against
a permission list.

---

## R4 — The shape of the fix, and that it works

**Decision**: one realm client scope, `sse-identity`, carrying a single
`oidc-sub-mapper`, with `include.in.token.scope: false`, added to the default
scopes of all eight clients in the file.

**The naming is deliberate and it now expresses a rule the realm already
follows**: `sse-groups` (hyphen) carries a claim and grants nothing;
`sse.cameras.read` (dot) grants something. `sse-identity` joins the first group.
Before this, that distinction existed but was never stated, and the one scope
carrying identity claims was a *permission*.

**Measured on the candidate realm** — a token per identity:

- **All eight carry `sub`**, including `management-web`, which did not.
- **`sse-identity` never appears in the `scope` claim**, since it grants nothing
  — and the mapper still fires, which `sse-groups` suggested and this confirms.
- **Permissions are unchanged** (SC-007): `smart-sentinel-eye-web` still
  `sse.management sse.audit.read`; `kiosk-web` still its six; `management-web`
  still its twenty.
- **`groups` still arrives**, so fab scoping is untouched.

**Alternatives considered**:

- *A client-level mapper on each of the eight* — eight copies of one fact, which
  FR-005 forbids and which is how the current three-way split arose.
- *Defining real `basic`/`profile`/`email`/`roles` scopes* — restores three
  groups of claims nothing reads. Speculative generality.
- *Realm-level `defaultDefaultClientScopes`* — applies to clients created
  **after** import, not to those in the file, so it would fix nobody here.

---

## R5 — `sse.management` gives up both its mappers

**Decision**: remove `sub-claim` **and** `preferred-username-claim` from the
`sse.management` client scope.

**Rationale**: it is a permission. A permission that also decides whether you can
be identified is the exact conflation that produced this feature — and it is
load-bearing today, because `smart-sentinel-eye-web` names its holder *only*
because it happens to hold administrative authority. Narrowing that permission
would silently un-name it, and narrowing exactly that kind of permission is what
the previous feature just did to the kiosk. FR-006 forbids it directly.

**The one behavioural change in this feature**: `preferred_username` disappears
from `smart-sentinel-eye-web`'s token. **Measured** as absent afterwards.
Verified beforehand that nothing consumes it — the only mention in `src/` is
`WhepAuthValidator` setting `NameClaimType = "preferred_username"`, whose
resulting `Name` no code reads. Removing it rather than migrating it follows
FR-009: nothing beyond the holder's identifier.

**Alternative considered**: leave `preferred-username-claim` where it is. Rejected
— it leaves an identity mapper on a permission scope, which is the shape being
fixed, for a claim with no reader.

---

## R6 — `kiosk-web` gives up its private copy

Spec 041 added an `oidc-sub-mapper` directly to the `kiosk-web` client as a
narrow fix for a screen that could not show video. **Decision**: remove it; the
shared scope supplies it. **Measured**: `kiosk-web` still carries `sub` with its
own mapper gone.

FR-006 and SC-006: one definition, zero private copies.

---

## R7 — The four inert names come off

**Decision**: delete `basic`, `profile`, `email` and `roles` from all eight
`defaultClientScopes` lists rather than defining scopes to match them.

**Measured**: the candidate realm imports with **zero** `doesn't exist. Ignoring`
warnings, down from thirty-two, and no other import warning of any kind.

**Verified safe**: nothing in `src/` reads a role or email claim — no
`realm_access`, no `resource_access`, no `ClaimTypes.Role`, no `ClaimTypes.Email`.
The realm's `defaultRoles: ["user"]` and the users' `realmRoles` feed no policy;
authorization is entirely scope-based through `RequireScopeExtensions`. Those
claims are absent from every token today anyway, which is the point — removing
names that resolve to nothing changes nothing except what the file says.

---

## R8 — Where the guard lives, and what it does not cover

**Decision**: extend `tests/Architecture.Tests/`, alongside
`KioskScopeParityTests` (spec 041), which already walks to the repository root,
parses the realm with `System.Text.Json`, and references `Identity.Application`.

Two assertions:

1. **Every client in the file holds `sse-identity`** (FR-007).
2. **Every scope any client names exists in the realm's own `clientScopes`**
   (FR-008) — a set relationship, so a typo fails rather than being discarded at
   start-up.

**What this does NOT cover, stated because two features this month turned on a
document claiming more than it checked:**

- **Clients created at runtime.** They are not in the file. R3 shows they are
  fine, for a reason the assertion cannot see.
- **A mapper present but misconfigured** — wrong type, `access.token.claim`
  false. The assertion reads names, not behaviour.
- **Whether a token actually carries `sub`.** Only minting one shows that, and
  the only automated place that mints against a real Keycloak is the Aspire
  fixture.

The last gap is the one worth closing beyond the file, which is R9.

---

## R9 — Nothing asserts an attributed change end to end

**Every** existing integration test that involves an `OperatorIdentifier`
fabricates one in the test (`OperatorIdentifier.From(Guid.CreateVersion7())`) and
hands it to a handler directly. None goes through a minted token, so none would
notice a client that cannot be attributed — which is precisely how
`management-web` has sat broken and unremarked.

**Decision**: add one integration test through the Aspire fixture that mints a
token, performs an attributed write over HTTP, and asserts it succeeds and is
recorded against the operator. That is SC-005, and it is the only assertion here
that would catch a mapper which exists but does not fire.

**Note for the tasks**: `AspireFixture.Auth.cs` mints with
`ClientId = "smart-sentinel-eye-web"` and `"openid sse.management"`. After R5 that
client keeps `sub` — from `sse-identity` rather than from the permission — so the
fixture needs no change. Confirming that is itself worth a task, because if it
were wrong every integration test would fail at once and the cause would look
like anything but this.
