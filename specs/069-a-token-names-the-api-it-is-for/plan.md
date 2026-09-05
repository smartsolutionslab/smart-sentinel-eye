# Implementation Plan: A token names the API it is for

**Spec**: `specs/069-a-token-names-the-api-it-is-for/spec.md` · **Issue**: #91
**Branch**: `fix/91-a-token-names-the-api-it-is-for` · **Base**: `origin/develop` @ `70f9223`
**Lane**: the issue carries `agent:ready`, so the autonomous lane is eligible
(ADR-0144) — see Declaration 2, which is what decides that.

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`infra-engineer`, alone.**

The change is Keycloak realm configuration plus one line of ASP.NET
authentication setup — infrastructure by every reading. The one part that looks
like backend work is not: `EnrollKioskCommandHandler`,
`RegisterDeviceCommandHandler` and `RotateWebhookClientCommandHandler` each
change **one list literal** — the set of Keycloak client scopes a client is
created with. That is the same fact as the realm file's `defaultClientScopes`,
expressed for clients the realm file cannot contain.

**Splitting it would put the risky half behind a coordination boundary.** The
runtime-created clients are the outage risk in this feature (spec, *inventory*);
handing them to a second agent while `infra-engineer` owns the realm means the
two halves of one rule land in two reviews. One agent, one PR.

No frontend work. `apps/` is not touched: the browser clients already send
whatever Keycloak mints, and neither app reads `aud`.

### Declaration 2 — is the honest answer a new ADR?

**No.** Every choice here is forced by decisions already recorded.

- *One audience rather than nine* is a consequence of **ADR-0106**: the gateway
  forwards one token to nine services and validates nothing, so per-context
  audiences would mean every token carrying all nine. That is not a decision,
  it is arithmetic on an existing one. **ADR-0008** (realm per fab) makes the
  nine-audience shape strictly worse and is the second reason, not the first.
- *A client scope rather than a per-client mapper* is **spec 042 FR-005**,
  already enforced by `RealmIdentityTests.No_client_carries_its_own_mapper`.
  Following an existing enforced rule is not a new decision.
- *Enabling `ValidateAudience`* is the issue's own ask and standard OIDC; it
  needs no architectural cover beyond **ADR-0007**.

**The one thing that would need an ADR is the option this plan rejects**:
per-context audiences. If a reviewer wants that instead, the lane is **blocked**
and it goes back to a human — ADR-0144 bars the lane from writing an ADR, and
this plan may not invent that architecture silently.

Two further things this plan explicitly does **not** do, for the same reason:
it does not amend the constitution, and it does not touch the Mosquitto
`aud` gap (spec, *Out of scope*) — that is a decision about a Go plugin's trust
model and belongs in its own issue.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a colour is RED.**

A token that authenticated yesterday is refused today. That is the feature. A
test arriving green is a phase-4 failure.

**No characterisation control is declared**, and the absence is deliberate: the
existing suite is expected to pass **unmodified**, which is SC-005. If
`KioskScopeParityTests`, `RealmIdentityTests` or
`WebhookBearerValidationIntegrationTests` goes red, that is a **design error in
this plan**, not a test to adjust — block and report, do not edit the
assertions.

---

## Architecture

### Bounded contexts and layers

**No domain model is touched, in any context.** Three source areas change, and
one of them is not code.

| Area | Layer | What changes |
|---|---|---|
| `AppHost` | Aspire composition root / realm import | one new `clientScope`; `sse-audience` added to nine clients' `defaultClientScopes` |
| `ServiceDefaults` | shared authentication setup | `ApiAudience` constant; `options.Audience`; the `ValidateAudience = false` line deleted |
| `Identity` | **Application** — three command handlers + `KeycloakScopeBundles` | one shared constant; three call sites append it to `DefaultClientScopes` |

**`Identity.Domain` is not touched.** `KeycloakScopeBundles` and
`KeycloakClientRepresentation` live in Application on purpose (ADR-0051: the
layer stays ASP.NET-free), and this change stays inside that shape.

### Entities, value objects, invariants

**None added.** Constitution §II does not engage: nothing here is a domain
model, so `PrimitiveBoundaryTests` has nothing to say about the `string`
constants.

Two constants, in two assemblies, holding two different facts:

```
ServiceDefaults.AuthenticationDefaults.ApiAudience   = "smart-sentinel-eye-api"  // what a service validates
Identity.Application.KeycloakScopeBundles.AudienceScope = "sse-audience"          // what a client is granted
```

**They are deliberately not one constant.** `Identity.Application` must not
reference `ServiceDefaults` (ADR-0051), which is exactly why
`KeycloakScopeBundles` already re-spells every `sse.*` string rather than
importing `ServiceDefaults.Authorization.Scope`. The pairing is held by
`Architecture.Tests`, which already references both assemblies and already does
this for `KioskClientId`
(`KioskScopeParityTests.The_kiosk_client_id_the_services_compare_against_is_the_realms`).
**Do not "fix" the duplication by adding a project reference** — that reference
is the thing ADR-0051 forbids.

The one invariant this feature adds:

> Every principal that can mint a token in this realm carries the API audience,
> and every API refuses a token that does not.

It is enforced in three places, and needs all three: the realm file (FR-003,
guarded by reading the file), the three handlers (FR-007, guarded by a unit
test on the representation), and a minted token (FR-010, guarded by decoding
one). **Reading the file cannot see a mapper that exists and does not fire** —
`RealmIdentityTests`' own class remarks say so about `sse-identity`, and the
same limit applies here.

### Messaging

**Unchanged.** No domain event, no integration event, no queue, no outbox, no
saga. Nothing in `Shared.Contracts` moves.

### Boundary rules

- No cross-context project reference is added or needed. The three changed
  handlers are all inside `Identity.Application`.
- `Shared.Contracts` is untouched — an audience is a token property, not a
  message shape.
- `Architecture.Tests` keeps its existing position as the only assembly that
  reads both `Identity.Application` and `ServiceDefaults` (ADR-0083 keeps that
  scope narrow; this adds assertions to it, not reach).
- `ServiceDefaults` gains no dependency: `Audience` is a property of the
  `JwtBearerOptions` it already configures.

---

## The realm change, exactly

**New client scope**, placed next to `sse-identity` in the `clientScopes` array
so the two claim carriers read together:

```json
{
  "name": "sse-audience",
  "description": "Names the API a token is for, in the access token's aud claim. Carries no permission (spec 069).",
  "protocol": "openid-connect",
  "attributes": { "include.in.token.scope": "false", "display.on.consent.screen": "false" },
  "protocolMappers": [
    {
      "name": "api-audience",
      "protocol": "openid-connect",
      "protocolMapper": "oidc-audience-mapper",
      "consentRequired": false,
      "config": {
        "included.custom.audience": "smart-sentinel-eye-api",
        "id.token.claim": "false",
        "access.token.claim": "true",
        "introspection.token.claim": "true"
      }
    }
  ]
}
```

That description is 103 characters. **Keep every description under 255** — a
longer one kills the realm import and hangs the whole Aspire fixture, with the
stack reporting itself healthy (FR-012).

**Nine client edits**, each adding `"sse-audience"` to the existing
`defaultClientScopes` array: `smart-sentinel-eye-web`, `management-web`,
`kiosk-web`, `kiosk-wall`, `identity-admin`, `migration-runner`,
`stream-distribution-attribution`, `scenario-simulator`, `event-ingestion`.

**No client gains a `protocolMappers` array** (FR-004).

### Why this does not break the three existing realm guards

| Guard | Why it stays green |
|---|---|
| `RealmIdentityTests.Every_scope_a_client_names_exists` | `sse-audience` is defined in the same file's `clientScopes`. |
| `RealmIdentityTests.No_permission_scope_carries_an_identity_mapper` | It iterates only `sse.`-prefixed names. `sse-audience` is hyphenated — the realm's own convention for a claim carrier. |
| `RealmIdentityTests.No_client_carries_its_own_mapper` | The mapper is on the scope, not on any client. This is the guard the issue's step 2 would have broken. |
| `KioskScopeParityTests.The_kiosk_client_grants_{nothing,everything}…` | Both sides filter through `IsPermission`, i.e. `sse.`-prefixed. The realm side drops `sse-audience`; the bundle side never gains it (FR-008). |

**That last row is why FR-008 exists.** Adding `sse-audience` to
`KeycloakScopeBundles.Kiosk` would fail
`The_kiosk_client_grants_everything_an_enrolled_kiosk_device_does`, because the
bundle side of that comparison is **not** filtered. Append at the call sites.

---

## Files touched

### Source (6)

| File | Change |
|---|---|
| `src/AppHost/Realms/smart-sentinel-eye-realm.json` | new scope; nine `defaultClientScopes` edits |
| `src/ServiceDefaults/AuthenticationDefaults.cs` | `ApiAudience` const; `options.Audience = ApiAudience`; delete `:62` and rewrite the comment at `:59-61` |
| `src/Identity/Application/KeycloakAdmin/KeycloakScopeBundles.cs` | `AudienceScope` const; the three lists unchanged |
| `src/Identity/Application/Commands/Handlers/EnrollKioskCommandHandler.cs` | `DefaultClientScopes: [.. KeycloakScopeBundles.Kiosk, KeycloakScopeBundles.AudienceScope]` |
| `src/Identity/Application/Commands/Handlers/RegisterDeviceCommandHandler.cs` | same shape, `Device` |
| `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs` | same shape, `WebhookIntegration` |

The comment being deleted at `AuthenticationDefaults.cs:59-61` says the mapper
*"lands when the Identity context is built out (spec TBD)"*. Replace it with
what is true — the audience comes from the `sse-audience` scope and is pinned by
`RealmAudienceTests` — rather than deleting it silently. A stale comment about a
deferred decision is how this file described itself for the whole life of the
shortcut.

### Tests (4 new files)

| File | Kind | Needs Docker |
|---|---|---|
| `tests/ServiceDefaults.Tests/BearerAudienceTests.cs` | options + `TokenValidationParameters` | no |
| `tests/Architecture.Tests/RealmAudienceTests.cs` | realm file | no |
| `tests/Identity.Application.Tests/KeycloakAdmin/RuntimeClientAudienceTests.cs` | handler → representation | no |
| `tests/Integration.Tests/Identity/TokenAudienceIntegrationTests.cs` | mints real tokens | **yes** |

**Three of the four reds run without a stack.** That is the design goal from
the brief: prefer a red that can be produced on demand. The fourth is the only
one that can see a mapper which exists and does not fire, so it is not
optional — it is just slower.

---

## Risks

### Risk 1 — a stale Keycloak volume makes the change look like a broken login

**The highest-probability failure, and it is not in the code.** In run mode
`AppHost.cs:123-128` gives Keycloak a persistent lifetime and a data volume; a
changed realm file is silently ignored on restart. Services then demand an
audience the realm never emits, and `gateway.ts:51`'s one-renew-one-retry
returns a second audience-less token, so the retry fails too.

**Mitigation**: the phase-5 procedure deletes the **volume** (FR-011), and
tasks.md repeats the command rather than referring to it. Integration and e2e
runs are immune — `isRunMode && !isE2ETests` means those containers are
throwaway.

### Risk 2 — a client missed is an outage, not a finding

There is no partial mode. **Mitigation**: FR-003 asserted **per client** in a
loop over the realm file (SC-004), the way `Every_client_holds_the_identity_scope`
is — not by sampling, because the two clients that worked by accident before
spec 042 would probably have been the sample.

### Risk 3 — the runtime-created clients are invisible to the existing suite

`WebhookBearerValidationIntegrationTests` exercises the JWT webhook path with
`management-web` standing in for a rotated client (`:40`), so a rotated client
with no audience passes every test in the repository and fails in production.
**Mitigation**: FR-007 guarded by a handler-level unit test, plus FR-010, which
enrols a real client and mints from it.

### Risk 4 — a mistyped mapper config key is discarded in silence

Keycloak ignores configuration it does not recognise; this realm has already
been bitten twice by exactly that (spec 042: thirty-two discarded scope names
per boot, said to nobody). A file guard reading `included.custom.audience` would
pass against a key Keycloak never reads.

**Mitigation**: `TokenAudienceIntegrationTests` decodes a minted token. **The
file guard alone does not discharge FR-002** — say so in review if the
integration test is proposed as optional.

### Risk 5 — `bearerOnly` in Keycloak 26.5

Avoided rather than mitigated: D3 declares no new client, so nothing depends on
a deprecated client type. If a reviewer restores the client at the phase-1 gate,
this risk comes back and needs a boot to settle.

### Risk 6 — coverage and metrics gates

Neither moves. The source change is four constants and three list literals; no
new branch, no new file over 300 LOC, no method over 30 LOC (ADR-0084). Domain
coverage is untouched because no Domain file is.

---

## What is deliberately not done

- **The Mosquitto plugin does not check `aud`** and is not changed here. It is
  Go, it is compiled into the AppHost image, and its trust model (`azp ==
  username`, issuer suffix, signature, expiry) is ADR-0100's. A separate issue.
- **`management-web` is declared and signed into by nothing.** It still gets the
  scope, because it is still minted — by an integration test.
- **The `scenario-simulator` secret duplicated three ways** stays duplicated. No
  fourth copy is added.
- **No `IAuthorizationDecisionPoint`.** Constitution §VIII records it as unbuilt
  (issue #1970); an audience check is not the place to start building it.
