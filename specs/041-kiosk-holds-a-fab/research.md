# Phase 0 Research: The kiosk holds a fab, and holds only what a kiosk needs

**Feature**: 041 | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

Everything below was measured, not reasoned about. The realm JSON was imported
into a throwaway Keycloak 26.5 container (the version AppHost pins) and tokens
were minted for every client involved. Where a finding contradicts a document in
the repo, the document is named.

---

## The headline: this is not a configuration change

Two things stand between the kiosk and a working wall that the spec did not know
about. Both were found by minting the intended client's token and reading it,
and **neither would be caught by any check that exists** — CI cannot produce
video, so the path they break is the one nothing exercises.

They are recorded as **R3** and **R4**. FR-005 asked for exactly this and said
what to do about it.

---

## R1 — Which client, and what the kiosk asks for

**Decision**: `client_id: 'kiosk-web'`, `scope: 'openid'`.

**Measured.** Password grant against the imported realm (direct access grants
temporarily enabled on a scratch copy purely to mint a token; scope resolution is
identical for the authorization-code flow):

| Client | Requested scope | Result |
|---|---|---|
| `kiosk-web` | `openid` | token; `groups: ["/fabs/munich"]`; six granular `sse.*` scopes |
| `kiosk-web` | `openid sse.management` | **`invalid_scope` — no token at all** |
| `smart-sentinel-eye-kiosk` (legacy) | `openid sse.management` | token; **no `groups`**; `sub`; `preferred_username` |

**So FR-006 is not tidiness.** Keycloak validates requested scopes against the
client's default + optional scopes. `kiosk-web` has no `sse.management`, so
leaving the scope string as it is would fail sign-in outright — a *worse* failure
than today's, and one that would be reached before any of this feature's other
work could be observed. The client and the scope string must change in the same
commit.

`sse-groups` carries `include.in.token.scope: false`, so it never appears in the
`scope` claim and cannot usefully be requested — it must be a *default* client
scope, which on `kiosk-web` it is. The fab claim arrives through its
`oidc-group-membership-mapper`.

**Alternatives considered**: requesting the six granular scopes explicitly —
unnecessary (default client scopes are always applied) and it would create a
second place where the kiosk's permission list is written down, which is the
exact drift FR-009 exists to prevent.

---

## R2 — The realm has no `basic`, `profile`, `email` or `roles` scope

**Measured.** On import, Keycloak logs, once per client:

```text
WARN [org.keycloak.models.utils.RepresentationToModel] Referenced client scope 'basic' doesn't exist. Ignoring
WARN [org.keycloak.models.utils.RepresentationToModel] Referenced client scope 'profile' doesn't exist. Ignoring
WARN [org.keycloak.models.utils.RepresentationToModel] Referenced client scope 'email' doesn't exist. Ignoring
WARN [org.keycloak.models.utils.RepresentationToModel] Referenced client scope 'roles' doesn't exist. Ignoring
```

A realm JSON that supplies its own `clientScopes` array replaces the built-in
set rather than adding to it. Every client in `smart-sentinel-eye-realm.json`
lists `basic, profile, email, roles` first and **all four are silently dropped**.

This matters far beyond cosmetics: a client's effective scope set is only its
`sse.*` entries, so **any claim normally supplied by `basic` or `profile` has to
come from a realm scope or a client-level mapper**. That is the mechanism behind
R3.

`apps/kiosk-web/src/app/auth.ts` half-knew this — *"The realm does not expose a
requestable `profile` scope"* — without following it to its consequence.

**Decision**: record it; do not fix the realm's missing built-ins here. Restoring
a `basic`-equivalent scope is the better long-term answer and touches every
client in the realm. Filed as a follow-up, not done in this feature.

---

## R3 — BLOCKER A: the intended client's access token carries no `sub`

**Measured.** Access-token payloads, same user, same moment:

```jsonc
// smart-sentinel-eye-kiosk (legacy, in use today)
{ "azp": "smart-sentinel-eye-kiosk", "sub": "2a8ff88f-...", "scope": "openid sse.management",
  "preferred_username": "operator" }          // <- no groups: the defect being fixed

// kiosk-web (intended)
{ "azp": "kiosk-web", "scope": "openid sse.overlays.read sse.events.write sse.variables.read
  sse.cameras.read sse.layouts.read sse.streams.read", "groups": ["/fabs/munich"] }
//   ^ no "sub", no "preferred_username"
```

`sub` reaches the legacy token through `sse.management`'s `oidc-sub-mapper` —
one of only two scopes in the realm carrying any mapper besides `sse-groups`.
`kiosk-web` holds neither, and `basic` does not exist (R2).

**Where it breaks**: `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`

```csharp
string? subject = principal.FindFirst("sub")?.Value;
if (subject is null)
{
    return Option<WhepAuthSubject>.None;      // -> MediaMTX gets 401 -> no video
}
```

Every WHEP connection would be refused. The wall would render and every tile
would stay dark.

**The ID token is unaffected** — it carries `sub` (measured), so
`oidc-client-ts` validates the sign-in normally. Only the bearer sent to services
is missing it, which is why this cannot be seen at sign-in.

**Decision**: add an `oidc-sub-mapper` protocol mapper to the `kiosk-web` client
in the realm JSON.

**Rationale**: `sub` is an identity claim, not a permission. Adding it widens
nothing the kiosk may *do*, so US2 and FR-005 are untouched. Scoping it to the
client keeps the blast radius at one client.

**Alternatives considered**:

- *Add the mapper to the `sse-groups` scope* — one edit either way, but
  `sse-groups` is shared with `management-web`, `scenario-simulator` and
  `stream-distribution-attribution`; changing a shared scope to fix one client is
  how the next surprise gets built.
- *Define a `basic`-equivalent realm scope* — the right long-term fix (R2), too
  wide for this feature.
- *Relax `WhepAuthValidator`* — no. It is right to refuse a token it cannot
  attribute.

---

## R4 — BLOCKER B: the WHEP gate demands the management scope

**Read from the code**:
`src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs`

```csharp
private const string RequiredScope = "sse.management";
...
if (!subject.Value.Scopes.Contains(RequiredScope, StringComparer.Ordinal))
{
    return Failure(AuthorizeWhepFailures.Forbidden());
}
```

**Watching video currently requires administrative authority.** This is the
inverse of every other endpoint in the product: those use
`RequireScopeExtensions`, which accepts the *granular* scope and treats
`sse.management` only as a **grandfathered** fallback. This one call site does
the opposite, by hand.

**The consequence is bigger than this feature.** `KeycloakScopeBundles.Kiosk` —
the bundle Identity grants **every physical kiosk device it enrols** — is:

```csharp
["sse.cameras.read", "sse.streams.read", "sse.layouts.read",
 "sse.overlays.read", "sse.variables.read", "sse.events.write"]
```

No `sse.management`. **So no enrolled kiosk device can watch video either, and
never could.** The canonical kiosk persona cannot perform the kiosk's primary
function. Nothing noticed because no enrolled device has ever connected in CI or
in the dev stack.

It also contradicts the constitution directly — §VIII: *"**Kiosks** authenticate
with device-bound credentials and **view-only scopes**."* A gate that requires a
write-everything scope in order to view makes view-only scopes insufficient to
view.

**Decision**: require `sse.streams.read`, accepting `sse.management` through the
same grandfather rule the rest of the codebase already applies. Raise it as a
finding in the PR and file it as its own issue.

**Rationale, against FR-005**: FR-005 forbids widening *the kiosk's permission
set* to make a call succeed. This widens nothing the kiosk holds — the kiosk's
set stays exactly `KeycloakScopeBundles.Kiosk`. It corrects a **gate** that asks
for the wrong scope. The alternative reading — "leave it, file it, ship a kiosk
with dark tiles" — fails SC-006 (*nothing the kiosk does is refused after the
narrowing*) and FR-011 (*same video*), so the spec does not permit deferring it.

`smart-sentinel-eye-web`, which management-web signs in with, carries
`sse.management` and no `sse.streams.read`, so the grandfather clause is what
keeps management's camera view working. It cannot be dropped.

**Alternatives considered**:

- *Add `sse.management` to `kiosk-web`* — defeats the entire second half of the
  feature. This is precisely the quiet widening FR-005 names.
- *Leave the gate and file the issue* — fails SC-006, and ships a kiosk that
  looks fixed. The defect this feature exists to correct is a thing that looked
  fixed.

---

## R5 — Everything the kiosk calls, and whether the narrowed set covers it

Enumerated from the source, not from memory. `kiosk-web`'s set is
`sse-groups, sse.cameras.read, sse.streams.read, sse.layouts.read,
sse.overlays.read, sse.variables.read, sse.events.write`.

| Call | Origin | Requires | Covered |
|---|---|---|---|
| `GET /layout-composition/layouts?state=Published` | `PickerPage` | `sse.layouts.read` | yes |
| `GET /layout-composition/layouts/{id}` | `CellPage` | `sse.layouts.read` | yes |
| SignalR `/hubs/layouts` | `useLayoutLifecycle` | `sse.layouts.read` (hub-level `[Authorize]`) | yes |
| `GET /overlay-designer/overlays/{id}` | `CellPage` tile | `sse.overlays.read` | yes |
| `GET /system-variables/overlays/{id}/snapshot` | `CellPage` tile | `sse.variables.read` | yes |
| `GET /stream-distribution/streams/{camera}` | `CameraViewer` | `sse.streams.read` | yes |
| `POST /stream-distribution/streams/kiosk-latency` | spec 040 | `sse.streams.read` | yes |
| WHEP `POST` -> MediaMTX -> `POST /streams/authorize` | `WhepClient` | **`sse.management`** + `sub` | **no — R3, R4** |

**The kiosk writes nothing.** `sse.events.write` sits in the bundle unused by the
browser kiosk; it stays, because FR-004 requires the *same set* as an enrolled
device and a browser-only variant would be a second notion of what a kiosk may
do.

---

## R6 — A second failure this fixes, which nobody has seen

`LayoutLifecycleHub.OnConnectedAsync` joins one SignalR group per fab in the
caller's `groups` claim, and resolved-text and highlight pushes are addressed to
those groups.

**The kiosk holds no fab, so it joins no group, so it receives none of them.**
Live overlay text and per-tile highlights have never reached a browser kiosk.
Invisible until now because the kiosk never got past the picker to a tile that
could show one.

Fixed by the same claim. Worth stating so the Phase 5 observation looks for it.

`apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts` also states *"The
hub requires `sse.management` scope"* — it requires `sse.layouts.read`. FR-010.

---

## R7 — Where the drift guard lives, and a guard that was never written

**`ScopeBundleTests` does not exist.** `KeycloakScopeBundles.cs` says:

> *"The `ScopeBundleTests` assertion (spec 008 PR F) verifies these strings match
> the catalogue."*

There is no such file anywhere in the repository. A doc comment asserting a guard
nobody wrote — the same class of error as spec 040's, found the same way, by
looking instead of believing.

**Decision**: `tests/Architecture.Tests/KioskScopeParityTests.cs`.

**Rationale**: Architecture.Tests already reads repository files through a
root-walk (`LatencyLegRecordTests` reads the constitution; `FabOrderingConventionTests`
reads source), and it already project-references `Identity.Application`, so it can
compare the realm JSON against `KeycloakScopeBundles.Kiosk` as **live values on
both sides** rather than as two copied strings. `System.Text.Json` is in the BCL;
no new package.

Assert as a **set comparison** (FR-004, SC-003), not a spot check, in both
directions — a scope added to either side must fail.

The false doc comment is corrected in the same change (FR-010).

**Alternatives considered**: writing the missing `ScopeBundleTests` in
`Identity.Application.Tests` — that project has no reason to know where the realm
JSON lives, and the assertion is a cross-cutting convention, which is what
Architecture.Tests is for.

---

## R8 — How the e2e check reaches a wall

**Reaching a wall needs a published layout, and CI has none.** `camera-sim` and
`scenario-simulator` both sit inside `if (isRunMode && !isE2ETests)`, so an e2e
stack boots with an empty catalogue. `e2e/layouts.spec.ts` does publish a layout,
but the kiosk spec depending on another spec's side effect is exactly the kind of
implicit coupling that produces the next silent pass.

**Decision**: a Playwright **setup project**. `e2e/support/seed-published-layout.setup.ts`
drives management-web (`:5173`) with the existing `signInAsOperator`, registers a
camera, authors a 1x1 layout and publishes it. The `kiosk` project declares
`dependencies: ['seed']`.

Playwright's default `testMatch` only picks up `*.spec.ts` / `*.test.ts`, so a
`.setup.ts` file is invisible to the `chromium` and `kiosk` projects and runs only
in the `seed` project that names it explicitly. No `testIgnore` churn.

**A wall renders without video.** `CellPage` renders `data-testid="layout-grid"`
and one `data-testid="layout-tile"` per populated cell regardless of stream state;
`CameraViewer` shows its own connecting/failed state inside the tile. So the
assertion works in CI even though CI can produce no frames — and R3/R4 remain
unobservable there, which is why they get a Phase 5 step.

**Alternatives considered**:

- *Seed through the API with a password-grant token* — faster, but publishing
  needs an `If-Match` round-trip and the seed would then encode contract details
  that the UI path gets for free.
- *Order the specs and rely on `layouts.spec.ts`* — rejected above.

---

## R9 — Asserting the claims, not just the behaviour

**Decision**: read the access token out of the app's own `sessionStorage` entry
(`oidc.user:<authority>:kiosk-web`), decode the payload, and assert:

- `groups` contains `/fabs/munich` — US1 sc.3, FR-001.
- `scope` does **not** contain `sse.management` — US2 sc.1, FR-003, SC-002.
- `scope`'s `sse.*` entries equal `KeycloakScopeBundles.Kiosk` — US2 sc.2, SC-003.

**Rationale**: SC-002 and SC-003 are statements about **absence** and about a
**set**. Neither is observable from behaviour: a kiosk with `sse.management`
restored would pass every behavioural assertion in the suite, which is how the
present defect's sibling would return. The claim is the only place the absence
exists.

**Alternatives considered**: behaviour only — rejected for the reason above.
Minting a separate token in the test — rejected: it would assert what Keycloak
does, not what the app actually holds.

---

## R10 — Removing the legacy client is clean

Read from the realm JSON: nothing references `smart-sentinel-eye-kiosk` — no
group, no client scope, no service-account role, no other client. Redirect URIs
and web origins are per-client. Deleting the object is the whole change.

Outside the realm, the id appears in `apps/kiosk-web/src/app/auth.ts` (replaced),
`.claude/agents/frontend-engineer.md` (corrected, FR-010) and in
`specs/003-layout-composition/` and `specs/011-frontend-247-resilience/`, which
are historical records and stay as they are (FR-008 says *outside historical
records*).

There is no production deployment — the same reason ADR-0118 defers the
production telemetry sink — so nothing is signed in against it anywhere but a
developer's machine, where the realm re-imports on every start.

**Noted, not touched**: the same pattern exists one client over.
`smart-sentinel-eye-web` is a live legacy client whose replacement,
`management-web`, is equally unused — and `management-web` would hit R3 the moment
anyone pointed the app at it. Out of scope by the spec; it is now a documented
trap rather than a hidden one.

---

## R11 — Redirect URIs and web origins

`kiosk-web` declares `redirectUris: ["http://localhost:5174/*", "http://localhost:*"]`
and `webOrigins: ["+"]` (meaning: the origins of the redirect URIs). The app
redirects to `${window.location.origin}/oidc/callback`, which the first pattern
covers. PKCE is `S256`, matching what `react-oidc-context` sends by default;
`directAccessGrantsEnabled` is `false`, which is correct for a browser client and
irrelevant to the authorization-code flow.

A wrong redirect URI fails at sign-in rather than at the first API call, so this
is confirmed in Phase 5 before anything else is judged.
