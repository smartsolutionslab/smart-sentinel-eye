# Spec 073 — A catalogued scope is enforced

**Issue:** #2070
**Branch:** `fix/2070-a-catalogued-scope-is-enforced`
**ADRs:** ADR-007 / ADR-008 (Keycloak per fab; scopes belong to clients — in
`docs/adr/0000-initial-decisions.md`), ADR-0036 (smallest change, no drive-by),
ADR-0070 (minimal APIs), ADR-0114 (fab resolution), ADR-0139 (rules that fail
the build), ADR-0144 (the autonomous lane may not write an ADR).
**Constitution:** §VIII — "Authorization is enforced by scope checks at every
endpoint, plus fab-group membership."
**Supersedes:** spec 005 FR-021's second sentence, "Reads require any
authenticated user."
**Phase 4a colour:** **behaviour-changing → red.**

---

## The defect

`sse.variables.read` exists in three of the four places a scope needs to exist
in, and in none of the ones that would make it do anything.

| Place | State |
|---|---|
| Catalogue — `src/ServiceDefaults/Authorization/Scope.cs:48`, `:105` | Present |
| Realm — `src/AppHost/Realms/smart-sentinel-eye-realm.json:50` | Present, defined |
| Realm — granted by default to `management-web` (`:178`), `kiosk-web` (`:217`), `kiosk-wall` (`:246`) | Granted |
| Runtime kiosk clients — `KeycloakScopeBundles.Kiosk` (`src/Identity/Application/KeycloakAdmin/KeycloakScopeBundles.cs:49`) | Granted |
| **Any endpoint requiring it** | **None** |

The three reads carry only the group-level `.RequireAuthorization()` at
`src/SystemVariables/Api/SystemVariableEndpoints.cs:35`:

- `GET /system-variables` (`:43`)
- `GET /system-variables/snapshot` (`:53`)
- `GET /system-variables/{name}` (`:60`)

The three writes on the same group correctly carry `Scope.Sse.Variables.Write`
(`:71`, `:80`, `:90`).

This is not purely an omission. Spec 005 FR-021 wrote **"Reads require any
authenticated user"** and the endpoints implemented exactly that. Spec 008 then
catalogued `sse.variables.read`, provisioned it in the realm and granted it to
three clients — and nobody went back to the endpoints. The scope has been
carried by every kiosk token since, testing nothing. **This spec makes the
enforcement match the grant, and in doing so retires spec 005 FR-021's second
sentence.**

## What it allows today

Any authenticated principal holding a `/fabs/<x>` group reads every variable in
those fabs, whatever its token was minted for.

`ResolveReadFabsAsync` (`:411`) bounds the damage: a principal with no usable
fab group is refused with `VARIABLE_FAB_REQUIRED`, and a fab it does not hold
is refused by `FabAuthorizationException.ForNoFabMembership`. **But the bound is
fab membership, not intent.** The realm ships two service accounts that are in
fab groups and were never granted `sse.variables.read`:

- `service-account-scenario-simulator` — `/fabs/munich` (realm `:576`)
- `service-account-stream-distribution-attribution` — `/fabs/munich`,
  `/fabs/dresden` (realm `:582`)

Both read every variable in those fabs today. That is the ADR-008
privilege-inheritance case, and it is also — see **Phase 4a** — the only
Docker-backed way to observe the refusal, because the integration fixture's own
client cannot.

---

## The outage question — every client that reads these routes

Adding enforcement can only *remove* access, so the question is not whether the
scope is correct in the abstract but whether any caller that reads these routes
today lacks it. **No caller does.** Nothing outside the two React apps calls
the SystemVariables HTTP API at all: `grep -rn "system-variables" src --include=*.cs`
returns the endpoints themselves, the YARP route in `src/ApiGateway/appsettings.json:61`,
the AppHost resource name and two connection-budget comments. No backend service
is a client of it.

| Caller | Signs in as | Holds the scope? | After the fix |
|---|---|---|---|
| `apps/management-web` (variables page, `SystemVariablesPage.tsx`) | `smart-sentinel-eye-web` (`apps/management-web/src/app/auth.ts:21`) | Yes — via `sse.management`, which `AddScopePolicies` accepts for every `sse.*` policy but `sse.events.publish` | Unchanged |
| `apps/kiosk-web`, window mode (`CellPage.tsx` → `useGetOverlaySnapshotQuery`) | `kiosk-web` (`apps/kiosk-web/src/app/auth.ts:52`) | Yes — realm `:217` | Unchanged |
| `apps/kiosk-web`, wall mode | `kiosk-wall` (`apps/kiosk-web/src/app/auth.ts:51`) | Yes — realm `:246` | Unchanged |
| `management-web` realm client (provisioned, not currently used by the app) | itself | Yes — realm `:178` | Unchanged |
| Runtime-enrolled kiosk clients (`EnrollKioskCommandHandler`) | per-kiosk client | Yes — `KeycloakScopeBundles.Kiosk:49` | Unchanged |
| `AspireFixture` integration tests | `smart-sentinel-eye-web`, `scope=openid sse.management` (`AspireFixture.Auth.cs:12`, `:111`) | Yes — via the bundle | Unchanged |
| Registered devices (`RegisterDeviceCommandHandler` → `KeycloakScopeBundles.Device`) | per-device client | **No** — `sse.cameras.read`, `sse.events.publish` only | **Refused — intended.** Does not read variables today |
| Webhook integrations (`RotateWebhookClientCommandHandler` → `WebhookIntegration`) | per-integration client | **No** — `sse.events.write` only | **Refused — intended.** Does not read variables today |
| `service-account-scenario-simulator` | `scenario-simulator` | **No** | **Refused — intended.** Worker seeds cameras/overlays/rules/layouts; `grep -rn variables src/ScenarioSimulator` finds nothing |
| `service-account-stream-distribution-attribution` | `stream-distribution-attribution` | **No** | **Refused — intended.** Reads `/fabs` only (ADR-0116) |
| `identity-admin`, `migration-runner`, `event-ingestion` | own service accounts | No, and no `sse-groups` either | Already refused at `ResolveReadFabsAsync` |

**Answer: no lockout.** Every caller that reads these three routes holds the
scope; every principal that loses access is one that does not read them and was
never meant to. The kiosk's opening-label path (`GET /system-variables/snapshot`,
#2069) is safe in both kiosk modes.

---

## Is `sse.variables.read` the right scope for all three?

- `GET /system-variables` returns `VariableDto[]` — variable names, types,
  states and **values**. `sse.variables.read`.
- `GET /system-variables/{name}` returns one `VariableDto`, same content.
  `sse.variables.read`.
- `GET /system-variables/snapshot` returns `ResolvedOverlaySnapshotDto`
  (`OverlayIdentifier`, `ResolvedText`, `Version`) — an overlay's label with
  variable values interpolated into it. **`sse.overlays.read` was considered
  and rejected**: the thing the caller learns that it could not learn otherwise
  is the *variable value*, so the variable scope is the tighter one, and the
  register in `EndpointScopeDeclarationTests` already books this route to #2070.
  Requiring both would be a new pattern — **no endpoint in `src/` requires two
  scopes today** — and ADR-0036 forbids introducing one as a drive-by. Every
  client that calls `/snapshot` holds both scopes regardless, so the choice is
  non-breaking either way.

## `.RequireAuthorization(scope)`, not `.RequireScope(scope)`

`RequireScopeExtensions.RequireScope` (`src/ServiceDefaults/Authorization/RequireScopeExtensions.cs:76`)
has **zero call sites**; it is a one-line forwarder to `.RequireAuthorization(scope)`.
Every one of the ~30 scoped endpoints in `src/` — including the three writes in
this very file — spells `.RequireAuthorization(Scope.…)`. Follow the unanimous
convention. Adopting the unused helper here, or deleting it, is a separate
question and out of scope (ADR-0036).

---

## Scope of the change

**In:**

1. `.RequireAuthorization(Scope.Sse.Variables.Read)` on the three GETs.
2. `Required scope: sse.variables.read` appended to each of the three
   summaries; `GET /system-variables/snapshot` gains its **first**
   `.WithSummary` — it is the one endpoint of 56 without one, precisely because
   it had no scope to name.
3. The three `UnenforcedByDesign` rows deleted from
   `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs:242-244`.
4. A test that observes the refusal.

**Out:** the realm (no scope is added, defined or granted), `Scope.cs`, the
endpoint handlers, `RequireScope`, the frontend, `/hubs/layouts` (the other
register in that file), the six write endpoints.

### Deleting the register rows is the work, not a weakened gate

`EndpointScopeDeclarationTests` reads `UnenforcedByDesign` **in both
directions** (merged as #2087, hours before this spec):

- `Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue`
  — a bare-`RequireAuthorization` route **missing** from the register fails.
- `Every_registered_route_still_enforces_no_scope` — a row that **no longer
  matches** a bare-authorization route also fails.

So the instant item 1 lands, the second half goes red on all three rows and
stays red until they are deleted. **That is the guard working exactly as its own
doc-comment says it will** — "Fixing 2070 deletes these rows; the completeness
half fails if it does not" (`:236`). Deleting them is the register's designed
retirement path, not a suppression, not a lowered threshold and not one of the
three things ADR-0144 forbids the lane. A reviewer seeing three deleted rows in
this diff is seeing the fix, and this paragraph exists so that a later reader
does not have to re-derive that.

The pinned counts in the same file (`EndpointFileCount = 12`,
`RouteHandlerMappingCount = 56`) do **not** change: no endpoint is added or
removed.

---

## User stories

### US-1 (P1) — A token without the scope cannot read system variables

**As** the security model, **I want** the three variable reads to require
`sse.variables.read`, **so that** a principal that happens to sit in a fab group
does not inherit reads nobody granted it.

Independently shippable: one file in `src/`, one file in `tests/`, one new test
file. No other context, no contract, no migration, no frontend.

There is no US-2. The summaries (item 2) are not a separate story — they are
what `EndpointScopeDeclarationTests` demands *of* a scoped endpoint, so they
land in the same commit or the build is red.

---

## Functional requirements

- **FR-001** `GET /system-variables`, `GET /system-variables/snapshot` and
  `GET /system-variables/{name}` each carry
  `.RequireAuthorization(Scope.Sse.Variables.Read)`, chained on the mapping in
  the same position the three writes chain theirs.
- **FR-002** The group-level `.RequireAuthorization()` at `:35` **stays**.
  Policies are additive; removing it would change what happens to an
  unauthenticated caller.
- **FR-003** Each of the three summaries contains the literal
  `Required scope: sse.variables.read`, spelled as the other 18 conformant
  endpoints spell it (`ScopeLabel` = `"Required scope: "`).
- **FR-004** `GET /system-variables/snapshot` gains a `.WithSummary` saying what
  it returns and naming its scope. It is the only one of the 56 route handlers
  without one.
- **FR-005** The three `UnenforcedByDesign` rows are deleted in the same commit
  as FR-001. Neither half of the register may be left disagreeing with the code.
- **FR-006** No change to `Scope.cs`, to `smart-sentinel-eye-realm.json`, or to
  any scope bundle. The scope and its grants already exist; that is the defect.
- **FR-007** The refusal is a **403**, produced by the standard policy
  machinery, not a hand-rolled check. No new error code, no new `ApiError`.
- **FR-008** The comment at `:23` — "reads require any authenticated user" — and
  the one at `:38` are corrected. A stale comment asserting the old rule beside
  the new chain is the same defect this spec is closing, one layer down.

---

## Acceptance scenarios

### Happy — a kiosk still reads its labels

```gherkin
Given a token minted for kiosk-web, whose default client scopes include sse.variables.read
And the principal is in group /fabs/munich
When it calls GET /system-variables/snapshot?overlayIdentifier=<munich overlay>
Then it receives 200 and the resolved text
```

### Auth — the defect, stated as the thing that must now fail

```gherkin
Given a client_credentials token for scenario-simulator
And its service account is in group /fabs/munich
And its default client scopes do not include sse.variables.read
When it calls GET /system-variables, GET /system-variables/{name} and
     GET /system-variables/snapshot
Then each answers 403
And before this change each answered 200 (or 404 for a name that does not exist)
```

### Auth — the legacy bundle still passes, deliberately

```gherkin
Given a token minted for smart-sentinel-eye-web with scope "openid sse.management"
When it calls GET /system-variables
Then it receives 200
```

`AddScopePolicies` grants every `sse.*` policy except `sse.events.publish` to a
token carrying `sse.management` (`RequireScopeExtensions.cs:59`). This is
existing, deliberate behaviour and **this spec does not change it**. It is
written down here because it is the reason the fixture's own client cannot
demonstrate the refusal.

### Auth — unauthenticated is still 401, not 403

```gherkin
Given no Authorization header
When a caller calls GET /system-variables
Then it receives 401
```

The group-level `.RequireAuthorization()` still runs (FR-002).

### Bad request — the scope is checked before the input is parsed

```gherkin
Given a token without sse.variables.read
When it calls GET /system-variables/snapshot with no overlayIdentifier
Then it receives 403, not 400
```

Authorization runs in middleware, ahead of the handler's `Guid.Empty` check
(`:317`). A caller that may not read must not learn which of its parameters was
malformed.

### Conflict — the register cannot be left half-fixed

```gherkin
Given the three GETs now enforce sse.variables.read
And the three UnenforcedByDesign rows are still present
When Architecture.Tests runs
Then Every_registered_route_still_enforces_no_scope fails, naming all three rows
```

```gherkin
Given the three UnenforcedByDesign rows are deleted
And the three GETs still enforce no scope
When Architecture.Tests runs
Then Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue
     fails, naming all three routes
```

Both halves are asserted, in this spec, because the pair of them is what makes
the deletion safe.

---

## Independent end-to-end test procedure

Docker is unavailable while this spec is written, so the procedure is recorded
for phase 5 rather than run now.

1. Boot the AppHost. Mint a token from **Aspire's proxied Keycloak endpoint**,
   not the container's mapped port, or every call 401s on the issuer.
2. `client_credentials` as `scenario-simulator` /
   `dev-only-scenario-simulator-secret`. Confirm the decoded token carries
   `groups: ["/fabs/munich"]`, `aud` including the API (spec 069's
   `sse-audience` is one of its default scopes), and **no** `sse.variables.read`.
3. Call all three GETs through the gateway. Expect **403** on each.
4. Repeat with the admin password grant (`smart-sentinel-eye-web`,
   `scope=openid sse.management`). Expect **200** on the list.
5. Open the kiosk at a Munich cell with a variable-bound overlay label. The
   opening label renders — this is the #2069 path, and it is the one that would
   show a lockout as a blank tile.
6. Confirm step 3 answered 200 before the change on the same stack, so the 403
   is attributable to this commit and not to a mis-minted token.

Step 6 is the one that makes the rest mean anything: a 403 from a token that was
always going to 403 proves nothing.

---

## Phase 4a — how red is obtained

**Behaviour-changing, so the tests are observed red first** (constitution
§Testing, ADR-0139). Three reds, in this order. The first two are Docker-free
and are the phase-4 gate; the third is marked for CI.

**Red 1 — the binding, Docker-free, new test.** A new xUnit test asserting that
each of the three routes resolves to an authorization policy named
`Scope.Sse.Variables.Read`. Preferred form: build the endpoints by calling
`MapSystemVariableEndpoints` on a bare `WebApplication`, enumerate
`EndpointDataSource.Endpoints`, and read `IAuthorizeData.Policy` off each
metadata collection — that observes the endpoint the framework actually built,
not the text of the file. `Architecture.Tests` already project-references
`SmartSentinelEye.SystemVariables.Api` (`:42`), so no new project or solution
edit is needed.

> Assumption, marked: endpoint *building* should not require the handlers'
> services, since `[FromServices]` and `[AsParameters]` are resolved per
> request. If it turns out to, the fallback is `builder.Services.AddAuthorization()`
> plus `AddSystemVariablesApi()` — registration only, no database connection —
> and if that too proves entangled, fall back to the source-scan reader already
> in `EndpointScopeDeclarationTests`. All three variants are Docker-free and all
> three are red today. The engineer picks the first that works and says which.

Expected failure: all three routes reported, each with `Policy` null.

**Red 2 — the register, Docker-free, existing guard, obtained by deleting the
rows first.** Delete the three `UnenforcedByDesign` rows as step one of the
change. `Every_endpoint_that_enforces_no_scope_is_registered_against_an_open_issue`
goes red naming `GET /system-variables`, `GET /system-variables/snapshot` and
`GET /system-variables/{name}` — "these endpoints require authentication and
nothing else". Then, once FR-001 lands, `Every_scoped_endpoint_names_the_scope_it_enforces_in_its_summary`
(A4) stays red until each summary carries `Required scope: sse.variables.read`,
which is what forces FR-003 and FR-004. **This ordering is deliberate**: it
produces the specific red — the literal `sse.variables.read`, not merely
"some scope" — out of a guard that already exists, without writing a second
copy of it.

**Red 3 — the refusal, needs the Aspire fixture, marked for CI.** An integration
test in `tests/Integration.Tests/SystemVariables/` that mints a
`client_credentials` token for `scenario-simulator` and asserts 403 on all three
GETs, alongside 200 for the admin client.

**Why the fixture's default client cannot produce this red, and what is done
about it.** `AspireFixture.ClientId` is `smart-sentinel-eye-web`
(`AspireFixture.Auth.cs:12`) and `FetchAccessTokenAsync` always requests
`openid sse.management` (`:111`). That bundle satisfies every `sse.*` policy but
`sse.events.publish`, so a test written the ordinary way — `CreateAdminClientAsync`
— gets 200 **before and after** the change and demonstrates nothing. *This is
precisely how the gap survived to be found by a phase-6 review of an unrelated
PR.* The way round it is a second principal, and the realm already ships two
that qualify: a service account in a fab group with no `sse.variables.read`.
`scenario-simulator` is first choice; `stream-distribution-attribution` is the
alternate. `StreamFabAttributionIntegrationTests.cs:97` and
`FabGroupClaimIntegrationTests.cs:176` are the existing patterns for minting
such a token — reuse one, do not add a fixture helper.

Red 3 is marked **[CI]** in `tasks.md`, as #91's was. If Docker is still
unresponsive at phase 4, reds 1 and 2 satisfy the gate locally and red 3 is
observed on the PR's CI run, with the failure quoted from the job log
**before** the fix commit — download the log first, since a passing re-run
erases the failure from the run's history.

---

## Latency budget

**No leg affected.** The three legs of the 800 ms path are unchanged: the
event→overlay leg is the SignalR push, not this HTTP read. `GET /snapshot` is
the kiosk's *opening* label fetch, outside the six legs, and what this spec adds
to it is one in-process scan of the token's `scope` claim in already-running
authorization middleware — the same work the three writes on this group already
do. Constitution §IV is not touched, and §VII's dashboard obligation does not
attach.

---

## Non-functional

- No new dependency, no new package, no migration, no realm edit, no contract.
- The refusal is a plain 403 from the policy machinery, so it is already
  audited and traced like every other one.
- File stays under 300 LOC (ADR-0084): `SystemVariableEndpoints.cs` is 448 lines
  today and this change adds ~6. **Marked, not resolved** — the file is already
  over the limit and is presumably suppressed or grandfathered; this spec does
  not make that worse in kind and explicitly does not restructure the endpoints
  (ADR-0036). If the analyzer fails on the delta, that is a finding for the
  engineer to report, not to fix by splitting the file.

## Assumptions, marked

1. **`/snapshot` takes `sse.variables.read`, not `sse.overlays.read`.** Argued
   above; non-breaking either way because every caller holds both.
2. **`scenario-simulator` does not read variables.** From
   `grep -rn "variables" src/ScenarioSimulator --include=*.cs`, which finds
   nothing, and from its realm grants, which include no variable scope. It is
   dev-only and never deployed (ADR-0111), so the blast radius of being wrong is
   a red CI job, not an outage.
3. **No non-repo client calls these routes.** Verifiable only for what is in
   this repository. A hand-made Keycloak account placed in a fab group and
   pointed at `/system-variables` would break — which is the same population
   the issue identifies as the reason to do this at all.
4. **Endpoint metadata is readable without the handlers' DI.** See Red 1;
   fallbacks named.

## New ADR needed?

**No.** Every decision this spec makes is an application of one that already
exists: ADR-007/ADR-008 put scopes on clients and require endpoints to check
them; constitution §VIII already states the rule this closes a hole in; ADR-0036
settles `.RequireAuthorization` over the unused `.RequireScope`. Nothing here is
a new architectural choice, so ADR-0144's prohibition is not engaged.

## Gate — Phase 1

Spec reviewed; no `[NEEDS CLARIFICATION]` remains.
