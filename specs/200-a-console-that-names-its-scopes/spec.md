# Spec 200 — A console that names its scopes

**Issue:** #2279 — *The operator console mints the management bundle, so every granular scope is decorative for every human caller*
**Branch:** `2279-granular-console-scopes`
**Phase-4a colour:** **RED** (behaviour-changing — an authorization decision that is 200/201 today must become 403)
**ADRs:** ADR-0080 (browser auth: `react-oidc-context`, the management app's auth-code flow), ADR-0131 (amends 0080's kiosk half; the console half stands), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel markers), ADR-0114 (fab authorization — the guard this one sits *beside*, not inside)
**Constitution:** §VIII *Safe by Default at Trust Boundaries* ("Authorization is enforced by scope checks at every endpoint"), §IX row *Authorization — scopes + fab groups*, §Security ("token-bound, short-lived credentials"), §Testing (red for new behaviour)
**Related, deliberately NOT in this slice:** #2280 (cross-fab webhook rotation), #2281 (Identity lists leak every fab), #2285 (no brute-force protection; password grants on the public client), #2070 / spec 073 (the same shape closed for `/system-variables`), #1976 / spec 041 (the same shape closed for the **kiosk**)

---

## Problem

The operator console signs in as the wrong client, and the client it uses
hands every human caller a wildcard.

`apps/management-web/src/app/auth.ts:21,26` — **verified on `origin/develop`
at 57fef847**:

```typescript
  client_id: 'smart-sentinel-eye-web',
  …
  scope: 'openid sse.management',
```

`src/AppHost/Realms/smart-sentinel-eye-realm.json:125-147` — that client is
`publicClient: true`, `directAccessGrantsEnabled: true`, and its
`defaultClientScopes` are `sse-identity sse-audience sse-groups`
**`sse.management`** `sse.audit.read`.

`src/ServiceDefaults/Authorization/RequireScopeExtensions.cs:36,46,59`:

```csharp
public const string LegacyManagementBundle = "sse.management";
…
bool acceptLegacyBundle = !string.Equals(scope, Scope.Sse.Events.Publish, StringComparison.Ordinal);
…
if (acceptLegacyBundle && tokens.Contains(LegacyManagementBundle, StringComparer.Ordinal))
{
    return true;
}
```

So a token carrying `sse.management` satisfies **21 of the 22 catalogued
scopes** (`Scope.All`, everything but `sse.events.publish`). Every operator
console session holds it. The granular catalogue is decorative on the human
path, and the fab guard (ADR-0114) is the only thing left bounding what a
signed-in human can reach.

**The replacement already exists and is already provisioned.** The realm's
`management-web` client (`:150-188`) is described in the realm file itself as
*"Replaces smart-sentinel-eye-web in spec 009"* and carries the twenty granular
`sse.*` scopes by name. Nothing points at it. This slice points the console at
it and takes the bundle away.

### Premise check — three of the issue's claims, re-verified, one stale

The issue says *"verified by reading; not observed against a running stack"*,
so each claim was re-read on this branch's base:

| Issue claim | Status |
|---|---|
| `auth.ts:21,26` uses `smart-sentinel-eye-web` + `openid sse.management` | **Holds**, verbatim |
| `smart-sentinel-eye-web` is a public client with direct access grants and the bundle as a default scope | **Holds** |
| `RequireScopeExtensions` grandfathers all `sse.*` but `events.publish` | **Holds** |
| `management-web` exists, is provisioned, and nothing points at it | **Holds** — no `.ts`, `.tsx` or `.cs` file names it as a sign-in client; the only C# uses are two webhook tests and one architecture guard |
| `AspireFixture.Auth.cs:12` mints on the bundle | **Holds** |
| *"nothing proves a granular scope is enforced against a token that lacks it"* | **STALE — two such tests already exist.** `VariableReadScopeIntegrationTests` (spec 073, issue #2070) mints `client_credentials` for `scenario-simulator` and asserts 403 on three `/system-variables` reads. `EventTypeRegistryAuthorizationIntegrationTests` (spec 143) plants a throwaway Keycloak client with a narrow `defaultClientScopes`, mints against it, and asserts 403 on `POST`/`DELETE /event-types`. |

That last row **changes the plan rather than the goal**: the mechanism the
issue asks for is already built and proven twice, so phase 4a does not need to
invent one. What is genuinely missing is a test of **this** path — nothing
anywhere asserts anything about what the *operator console's own* token holds,
or that `smart-sentinel-eye-web` is refused anything.

### A second correction, and it bounds what this slice may claim

`management-web`'s twenty default scopes are `Scope.All` minus
`sse.events.publish` and… nothing else. The bundle grandfathers `Scope.All`
minus `sse.events.publish`. **The two sets are identical.**

So this change does **not** shrink the set of endpoints a signed-in operator
can reach, and the spec must not pretend otherwise. What it does:

1. **The wildcard leaves the human path.** A token stops carrying a claim that
   means *"pass every policy"* and starts carrying the list of things it may
   do. Every granular policy becomes individually enforced against an explicit
   claim, so narrowing any one of them is now a realm edit that *works* rather
   than a realm edit the grandfather clause ignores.
2. **`smart-sentinel-eye-web` stops being a blanket-authority client.** It is
   public with direct access grants; today any realm account plus its own
   password mints a token that passes 21 policies. After this it passes one
   (`sse.audit.read` — see *Out of scope*).
3. **There is, for the first time, a test that fails if the console's token is
   widened.** Absence is not observable from behaviour: a console holding
   write-everything behaves exactly like one holding what it needs. That is
   the argument spec 041 made for the kiosk (`e2e/kiosk-identity.spec.ts:34-44`),
   and the console was never given the same treatment.

**What it explicitly does not do:** make an operator's token narrower than an
admin's. Scopes in this realm are granted **per client**, not per realm role,
so every human signing in to the console gets the same twenty. Per-role least
privilege needs a role→client-scope mapping that does not exist and that no
ADR decides — see *Out of scope*.

---

## Locked tech choices

React + TypeScript + Vite (ADR-0074); `react-oidc-context` for the management
app (ADR-0080); Keycloak per fab (ADR-0007/0008); scope policies via
`ServiceDefaults.Authorization.RequireScopeExtensions`; xUnit + Shouldly
(ADR-0052); integration against the real Aspire stack via `AspireFixture`,
**no Testcontainers** (ADR-0103); Playwright e2e (ADR-0108).

**No new dependency, no new abstraction, no new client, no new scope.** Every
piece this slice needs — the `management-web` client, the narrow-token probe
pattern, the e2e token-claim reader — is already in the tree.

---

## User stories

### US1 (P1) — The console carries the scopes it names, and nothing else

*As* the security owner of a fab,
*I want* the operator console to sign in with the granular-scope client and the
legacy bundle to be granted to no one,
*so that* a token states what it may do, a policy refusal is a real refusal,
and a stolen or password-grabbed console credential no longer passes every
authorization check in the product.

This is the whole shippable slice, and **its three edits are atomically
coupled** — this is the single most important planning fact in this spec:

- Repointing `auth.ts` **without** dropping the bundle changes nothing
  security-wise (the operator's authority is identical either way).
- Dropping the bundle **without** repointing `auth.ts` **breaks sign-in
  entirely**: naming a scope a client does not hold fails the whole
  authorization request with `invalid_scope` and yields no token at all
  (documented as *observed* at `apps/kiosk-web/src/app/auth.ts:81-82,93-95`,
  spec 041). The console would show a login loop.
- Either edit **without** moving `AspireFixture.ClientId` leaves ~300
  integration tests minting `openid sse.management` against a client that no
  longer has it — `invalid_scope`, and the whole integration suite fails to
  obtain a token.

They land in one commit-set or the feature is broken. Phase 4b must not
deliver a partial.

### US2 (P2, deferrable) — The grandfather clause is gone, not merely unused

*As* the next person to edit this realm,
*I want* `sse.management` to mean nothing to the code,
*so that* re-granting it — by a realm edit, a merge, or a hand-rolled client in
production — cannot silently restore blanket authority.

After US1 no client holds the bundle, so the clause at
`RequireScopeExtensions.cs:46,59` is inert. **Inert is not gone.** US2 deletes
the clause, the `LegacyManagementBundle` constant and the `sse.management`
client-scope definition from the realm.

**US2 is genuinely separable.** US1 ships and is a complete security
improvement without it, backstopped by SC-7's architecture guard, which fails
the build if any client re-acquires the bundle. If phase 4 runs long or US2
turns red in a way that is not quickly explained, **ship US1 and file US2** —
say so in the PR rather than half-doing it.

---

## Acceptance scenarios

Seeded accounts used below: `operator` / `Operator1234` (realm role `user`,
`/fabs/munich`) and `admin` / `Admin1234` (realm roles `user`, `admin`,
`/fabs/munich`) — both declared in the realm file.

### The refusal — SC-1 is the reason this spec exists

**SC-1 (P1, security) — a scope the token does not hold is refused**

```gherkin
Given the realm no longer grants sse.management to smart-sentinel-eye-web
When a token is minted by password grant for client "smart-sentinel-eye-web"
     as the seeded operator
Then that token's scope claim does not contain "sse.management"
 And GET /system-variables with it answers 403 Forbidden
```

Today: the token carries `sse.management`, and the same `GET` answers **200**.
This is the red.

**SC-2 (P1, control on the refusal) — the same token is not refused everything**

```gherkin
Given the token from SC-1
When GET /audit?pageSize=1 is called with it
Then it answers 200 OK
```

`sse.audit.read` stays a default scope of `smart-sentinel-eye-web`, so this
proves the 403 in SC-1 is *about the missing scope* — not a broken mint, an
issuer mismatch, a rejected audience, or a stack that refuses everybody. A
refusal assertion with no positive control is the failure mode this repository
has filed against itself more than once.

**SC-3 (P1, control on the console) — the console's own path still works**

```gherkin
Given a token minted the way the console mints it —
      client "management-web", scope "openid", as the seeded operator
When GET /system-variables is called with it
Then it answers 200 OK
```

### What the console carries

**SC-4 (P1) — the console's token names its scopes and not the bundle**

```gherkin
Given a token minted for client "management-web" with scope "openid"
When its scope claim is decoded
Then it contains sse.cameras.write, sse.layouts.write, sse.overlays.write,
     sse.variables.write and sse.audit.read by name
 And it does not contain "sse.management"
```

Asserted as a **set membership plus an absence**, following
`e2e/kiosk-identity.spec.ts`: a check that only confirms the console works
passes just as happily with the bundle restored, which is exactly how this
weakness returns.

**SC-5 (P1, browser) — what the running console actually holds**

```gherkin
Given an operator signed in to management-web through the real Keycloak form
When the access token the app is holding is read from its own OIDC storage
Then its azp claim is "management-web"
 And its scope claim does not contain "sse.management"
 And its groups claim contains "/fabs/munich"
```

SC-4 asks what Keycloak *will* mint; SC-5 asks what the browser *is holding*.
They are different questions, and the repository has been burned by answering
only the first (`guards that read the design artefact`). The `groups`
assertion is a control: `sse-groups` is a default scope of both clients, and a
repoint that lost it would break every fab-scoped read while every scope
assertion above stayed green.

**SC-6 (bad request / sign-in integrity) — naming a scope the client lacks fails loudly**

```gherkin
Given the console is configured for client "management-web"
When it requests scope "openid sse.management"
Then Keycloak answers invalid_scope and issues no token
```

Not a test to write — it is the **reason `scope` must become `'openid'`
alone** and the failure mode a careless half-edit produces. Recorded here so
phase 4b does not "helpfully" keep the old scope string. The twenty `sse.*`
scopes are *default* client scopes, applied whether or not they are asked for;
`sse-groups` sets `include.in.token.scope: false` and could not be requested
even if someone wanted to.

**SC-7 (P1, configuration) — the bundle is granted to no client**

```gherkin
Given the realm file
When every client's defaultClientScopes and optionalClientScopes are read,
     together with every KeycloakScopeBundles list
Then "sse.management" appears in none of them
```

A static guard, in the shape `ScopeGrantTests` already uses. It proves the
design was **written down**, not that it holds — SC-1 is what asks the running
system. Both, because the static one is the only thing that fails the build
when someone re-grants the bundle in a PR that never boots the stack.

**SC-8 (auth — the 401 boundary is unmoved)**

```gherkin
Given no Authorization header
When GET /system-variables is called
Then it answers 401 Unauthorized, not 403
```

Pinned because SC-1 turns a 200 into a 403 and a mis-minted token also produces
a non-200. 403 exactly, 401 exactly — never merely "not 200".

### US2

**SC-9 (P2) — a principal carrying only the bundle passes no policy**

```gherkin
Given an authenticated principal whose only scope claim is "sse.management"
When it is evaluated against every policy in Scope.All
Then every one of them fails
```

A pure unit test on `AddScopePolicies` — no stack, no realm, instant.

**This scenario deliberately inverts an existing, currently-green test**:
`RequireScopePolicyTests.Legacy_management_bundle_passes_a_normal_sse_policy`
(`tests/ServiceDefaults.Tests/Authorization/RequireScopePolicyTests.cs:87-97`)
asserts today's behaviour and must be **replaced** by SC-9, not edited into
compliance. The replacement is written by the **phase-4a test-writer** as the
red step and quoted in the PR; the phase-4b engineer may not touch it. Its
sibling `Legacy_management_bundle_does_not_pass_the_events_publish_policy`
(`:99-109`) becomes a special case of SC-9 and folds into it.

---

## Independent end-to-end test procedure

A reviewer with a clean checkout, Docker running, and no knowledge of this
spec can reproduce the finding and the fix:

1. **Delete the Keycloak data volume first.** `WithRealmImport` runs only
   against a fresh volume (`src/AppHost/AppHost.cs:150-154`), and the realm
   container is `ContainerLifetime.Persistent` in run mode. Skipping this step
   runs the *old* realm against the *new* code, which is a false red that reads
   exactly like a failed fix. See `plan.md` §*The volume trap* for the command.
2. **Boot the stack:** `dotnet run --project src/AppHost`.
3. **Before the change — the defect, in two commands.** Mint against the public
   client with no client secret, as any realm account can:

   ```sh
   curl -sk -X POST "<keycloak>/realms/smart-sentinel-eye/protocol/openid-connect/token" \
     -d grant_type=password -d client_id=smart-sentinel-eye-web \
     -d username=operator -d password=Operator1234 -d scope=openid
   ```

   Decode the `scope` claim: it contains `sse.management`. Call
   `GET /system-variables` with it: **200**. The operator has a scope the
   client was never granted by name.
4. **After the change:** the same mint yields a token whose `scope` claim has
   no `sse.management`; the same `GET` answers **403**; `GET /audit?pageSize=1`
   with that same token still answers **200**.
5. **The console, in a browser.** Open `management-web` (`:5173`), sign in as
   `operator` / `Operator1234`, and in the console run the
   `localStorage`/`sessionStorage` `oidc.user:` read that
   `e2e/support/kiosk-session.ts:readKioskAccessToken` performs. `azp` is
   `management-web`; `scope` has no `sse.management`; the Cameras page still
   lists, and registering a camera still works.

Steps 3–4 are the mechanised SC-1/SC-2; step 5 is the mechanised SC-5. **The
Keycloak endpoint must be Aspire's proxied endpoint, not the container's
mapped port** — a token minted from the mapped port carries an issuer the APIs
reject, and every call 401s regardless of scope, which reads as a total
authorization failure.

---

## Latency budget

**N/A.** Nothing on this path is on the event-to-overlay budget. The change
alters which Keycloak client a browser signs in to and which claim a policy
reads; the six legs in constitution §IV are untouched, and no leg is
re-measured or re-claimed.

One second-order note, recorded rather than measured: the console's token
`scope` claim grows from one `sse.*` entry to twenty, adding a few hundred
bytes per request header. On an HTTP API call path with no budget row, at
operator-console request rates. Not material, not claimed as measured.

---

## Out of scope

Each of these is a real gap. Each is deliberately not here.

- **#2280** (cross-fab webhook rotation), **#2281** (Identity lists leak every
  fab), **#2285** (no brute-force protection; password grants on the public
  client). Named as related by the issue and by its author's own comment: the
  fab guard still bounds *which* fab, which is why those three matter. This
  spec touches none of them and makes none of them worse.
- **Per-role least privilege.** Making an operator's token narrower than an
  admin's needs a role→client-scope mapping in Keycloak (a scope granted
  conditionally on a realm role, or separate clients per persona). That is an
  **architectural decision no ADR makes**, and the autonomous lane may not make
  one (ADR-0144). **Flagged: an ADR is owed before anyone builds this.** It is
  the thing that would turn "the console names its scopes" into "each human
  holds only their own", and it is the natural successor to this slice.
- **Retiring `smart-sentinel-eye-web` altogether.** After US1 the client has
  **no consumer left in the repository** — the console, the integration fixture
  and the run-mode measurement helper all move to `management-web` — yet it
  remains public, direct-access-grant enabled, and holding `sse.audit.read`.
  That is a standing surface with nothing behind it. **Recommend filing a
  follow-up** to delete or disable it. Not done here: it is not what #2279
  asks for, and deleting a client referenced by ADR-0080 and five specs is a
  decision, not a fix.
- **Removing `directAccessGrantsEnabled` from the browser clients.** The
  password grant is what makes #2285 reachable and what the integration fixture
  depends on. Changing it is #2285's business and would take the whole test
  suite with it.
- **`sse.events.publish`.** Neither client holds it, no HTTP endpoint requires
  it (verified: the only `src/` occurrences are the catalogue entry, the
  grandfather carve-out and `KeycloakScopeBundles.Device`), and it is enforced
  on the MQTT side by mosquitto-go-auth (ADR-0100). Untouched.
- **`IAuthorizationDecisionPoint`** (constitution §IX, issue #1970). Still
  absent. This spec does not add it and does not need it.

---

## File contention

`gh pr list --state open` shows **one** open PR: **#2485**
(`2425-drop-chunk-by-identity`), which touches AuditObservability retention
SQL. No overlap with any file below.

Files this spec touches, and who else might: `auth.ts` (management-web only —
no in-flight work), the realm JSON (**the one to watch**; any spec adding a
scope or a client edits it), `AspireFixture.Auth.cs` (**high-traffic** — every
integration spec reads it, few edit it), `RequireScopeExtensions.cs` +
`RequireScopePolicyTests.cs` (US2 only).

**The realm JSON and `AspireFixture.Auth.cs` are the contention risk.** If
another lane opens a PR touching either while this is parked, rebase early —
rebase-merge renames SHAs (ADR-0087) and a parked branch replays its own old
copies as conflicts.

---

## Success criteria

- **SC-1 observed failing before the change**, with the failure quoted in the
  PR body (ADR-0139, constitution §Testing).
- SC-1 through SC-8 green after, on a stack booted against a **freshly created
  Keycloak volume**.
- The full integration suite green after `AspireFixture.ClientId` moves — the
  proof that "identical authority, different claim" is a fact and not an
  argument.
- No `[NEEDS CLARIFICATION]` remains.
- The feature issue (#2279) is on Project #13.

## Assumptions, marked

1. **No HTTP endpoint requires `sse.events.publish`.** Verified by grep across
   `src/`; it is the only scope the bundle grandfathers that `management-web`
   does not grant, so it is the only way the fixture move could change a test
   outcome. If phase 4a finds one, that test is the exception and the plan says
   what to do with it (`plan.md` §*The one asymmetry*).
2. **Keycloak applies all default client scopes regardless of the `scope`
   parameter.** Not assumed — asserted against the running provider by
   `EventTypeRegistryAuthorizationIntegrationTests.A_management_web_token_carries_every_scope_that_client_grants`,
   which exists and is green.
3. **`management-web`'s `redirectUris` cover the console's dev origin.** Read:
   `http://localhost:5173/*` and `http://localhost:*` — the same pair
   `smart-sentinel-eye-web` carries, minus `:5174`. The console runs on
   `:5173`. If AppHost ever moves it, sign-in breaks with a redirect-uri error,
   not a scope error.
