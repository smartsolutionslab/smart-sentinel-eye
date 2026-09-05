# Feature Specification: The WHEP hook checks the audience too

**Branch**: `fix/2090-the-whep-hook-checks-the-audience-too` · **Issue**: #2090
**Created**: 2026-09-05 · **Status**: Phase 1 complete, awaiting review
**ADRs**: 0007, 0008, 0036, 0051, 0103, 0105, 0139, 0144 — **no new ADR**
**Specs it continues**: 069 (`sse-audience`, and the decision that a token names
the API it is for), 041 (the client split), 052 (`kiosk-wall`)

---

## Summary

Spec 069 turned audience validation on for the standard bearer pipeline. One
authenticated HTTP surface is not on that pipeline: `POST /streams/authorize`,
MediaMTX's external-auth hook, which validates the viewer's token with a
hand-rolled `WhepAuthValidator` because MediaMTX posts the token **in the JSON
body** rather than as an `Authorization` header. That validator still says

```csharp
ValidateAudience = false, // mirrors JwtBearerOptions in AuthenticationDefaults
```

and the comment is now false. This spec makes the WHEP hook check `aud` the way
every other surface does, from the same constant, and adds the test that fails
if the two ever part again.

**Everything in the issue was checked against the tree**, and one detail is
off: the `ValidateAudience = false` line is
`src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs:43`, not `:44`
(line 44 is `ValidateLifetime = true`). `6dac431a` — "fix(auth): a token names
the API it is for" — is on `develop` and does set
`options.TokenValidationParameters.ValidAudiences = [ApiAudience]`
(`src/ServiceDefaults/AuthenticationDefaults.cs:67`). The divergence is real.

---

## How real, and how narrow

**Structurally exact, practically almost nil today.** Both halves are worth
stating, because overstating this would repeat #91's error in the other
direction — #91 was itself defence in depth and said so.

To get through this endpoint a caller needs a token that is realm-signed,
unexpired, issued by the right issuer, and carrying `sse.streams.read` or the
grandfathered `sse.management` bundle
(`AuthorizeWhepCommandHandler.cs:27,29,51-55`). The audience check adds nothing
against a caller who already has one of those. What it excludes is a token
carrying those scopes that was minted for **something else**. The concrete
populations:

| Population | Can it reach 200 today? | After this change |
|---|---|---|
| Tokens from `smart-sentinel-eye-web`, `management-web`, `kiosk-web`, `kiosk-wall` | yes — and they carry `aud: smart-sentinel-eye-api` already | unchanged |
| Access tokens minted **before** #91's realm import, still inside their lifetime | yes — no `aud` at all, and `sse.management` | refused, along with every other endpoint that already refuses them |
| Keycloak's built-in clients (`account`, `security-admin-console`, `broker`) | no — `aud: account`, but none of the `sse.*` scopes, so the handler answers **403** | unchanged |
| A future principal in this realm minted for another API — a second product sharing the realm, an SSO federation, a service account created by hand without `sse-audience` | **yes** | refused |

Only the last row is a gap nothing else covers, and it is hypothetical: no such
principal exists. **So the honest answer is "narrow".** The value is not a
closed attack path; it is that the invariant *every authenticated HTTP surface
in this system checks `aud`* currently has one undeclared exception, and it sits
on the one auth path that is hand-rolled — the path most likely to be missed a
second time. #2085 records the other non-checking surface (MQTT), for a
different reason and with a different owner.

---

## Is `WhepAuthValidator` the only copy?

**Yes.** `grep -rn "TokenValidationParameters" --include=*.cs src` returns two
production sites: `AuthenticationDefaults.cs:67` and `WhepAuthValidator.cs`
(21, 39, 57). Nothing else constructs one.

**The webhook JWT path does not have this gap.** `ValidateJwtAsync`
(`src/EventIngestion/Api/EventsEndpoints.Writes.cs:262-289`) looked like a
second hand-rolled validator and is not one: its first statement is
`request.HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)`,
so the token is validated by the standard bearer handler with the options #91
changed, and it inherits audience validation for free. What it hand-rolls on
top is *authorisation* — scope, `azp` against the integration's client, and the
`/fabs/<id>` group — not token validation. **One copy, not two**, so the fix
covers one file.

---

## Decisions

### D1 — read the audience from `AuthenticationDefaults.ApiAudience`

`src/StreamDistribution/Infrastructure/SmartSentinelEye.StreamDistribution.Infrastructure.csproj`
already carries a project reference to `SmartSentinelEye.ServiceDefaults`.
`BoundaryTests` forbids context-to-context references and infrastructure
frameworks in Domain; it says nothing against Infrastructure → ServiceDefaults,
and all eight other bounded contexts do the same — verified, not assumed: nine
`src/*/Infrastructure` projects exist and all nine carry the reference. So the
constant is reachable and ADR-0051 is untouched.

**This is deliberately not the reasoning spec 069 used for the realm.** There,
the literal is spelt out three times on purpose, because `Identity.Application`
**may not** reference `ServiceDefaults` (ADR-0051) and a shared constant
asserted against itself proves nothing. Here the reference already exists, so a
second literal would be a copy created by choice — the exact thing that broke.

### D2 — rejected: a shared `TokenValidationParameters` factory in ServiceDefaults

`AddJwtBearer` hands the callback an already-constructed
`TokenValidationParameters` and `AddBearerAuthentication` mutates it in place. A
shared factory would have to *replace* that instance for all nine APIs to give
the WHEP hook a common ancestor — a larger, more surprising change to the file
every service depends on, for a guarantee D1 plus D3 already provide. ADR-0036:
smallest change.

### D3 — a test, because a comment was what failed

The comment on line 43 was the entire binding between the two, and it did not
survive one change. ADR-0139 is the repo's standing answer to that: a rule that
fails the build, not one a reviewer has to remember. The test asserts the
**shape** the two configurations share, not just the literal — `ValidateAudience`
and the `ValidAudiences` set — so it still catches a second audience added to
one side only, or the flag flipped back, even though after D1 both sides trace
to one constant. Stated plainly because a parity test over a shared constant is
otherwise easy to over-credit.

### D4 — delete the line rather than negate it

`TokenValidationParameters.ValidateAudience` defaults to `true`. #91 deleted its
`ValidateAudience = false` rather than writing `= true`, on the grounds that a
line restating a default only makes the next reader wonder what it is for. Same
choice here.

### D5 — the issuer stays independently derived

`BindWhepAuthOptions` (`StreamDistributionInfrastructureModule.cs:181-197`)
derives the authority from configuration the same way `AddBearerAuthentication`
does, and that duplication is older than this issue. Unifying it is a different
change with a different risk profile. **Not in this slice.**

---

## User Scenarios & Testing

### User Story 1 — a token minted for another API cannot open a stream (P1)

A viewer opens a WHEP stream. MediaMTX posts the stream path and the viewer's
bearer to `POST /streams/authorize`. The hook must accept the token only if it
names this product's API, exactly as the nine REST APIs do.

Independently shippable: two source files (one behaviour change, one endpoint
summary sentence), one new test file. No realm change, no frontend change, no
migration, no Aspire resource.

#### Acceptance scenarios

```gherkin
Scenario: the happy path is unchanged
  Given a kiosk signed in through kiosk-web or kiosk-wall
  And its access token carries aud "smart-sentinel-eye-api" and sse.streams.read
  When MediaMTX posts that token to POST /streams/authorize for a live path
  Then the response is 200
```

```gherkin
Scenario: a token minted for another API is refused
  Given a realm-signed, unexpired token carrying sse.streams.read
  And its aud names some other API
  When MediaMTX posts it to POST /streams/authorize
  Then the response is 401
  And the stream does not open
```

```gherkin
Scenario: a token with no audience claim at all is refused
  Given a token minted before the sse-audience scope existed
  When MediaMTX posts it to POST /streams/authorize
  Then the response is 401
```

```gherkin
Scenario: the right audience without the scope is still forbidden, not unauthorized
  Given a token carrying aud "smart-sentinel-eye-api" and no sse.streams.read
  And no sse.management bundle either
  When MediaMTX posts it to POST /streams/authorize
  Then the response is 403
  And not 401 — the caller is known, and is not entitled
```

```gherkin
Scenario: a malformed or absent token is unchanged
  Given a body whose token is null, empty, or "this-is-not-a-jwt"
  When MediaMTX posts it to POST /streams/authorize
  Then the response is 401
```

**No conflict scenario.** This endpoint neither writes nor versions anything;
`If-Match` and 409 (ADR-0113) do not apply. Recorded rather than omitted.

**No bad-request scenario beyond the above.** A path that does not parse is
already answered 403 by design (`WhepAuthIntegrationTests`
`.Authorize_with_an_invalid_path_returns_403`), and this change does not move it.

---

## Requirements

- **FR-001** `WhepAuthValidator`'s `TokenValidationParameters` validate the
  audience. The `ValidateAudience = false` line is **deleted**, not negated (D4).
- **FR-002** Those parameters set
  `ValidAudiences = [AuthenticationDefaults.ApiAudience]` — the constant, never
  a second literal (D1).
- **FR-003** The construction of those parameters is reachable from
  `SmartSentinelEye.StreamDistribution.Infrastructure.Tests` without booting a
  host. The existing `InternalsVisibleTo` in
  `SmartSentinelEye.StreamDistribution.Infrastructure.csproj` already names that
  assembly; **no new csproj attribute is added.**
- **FR-004** A Docker-free test asserts that these parameters **throw**
  `SecurityTokenInvalidAudienceException` for an audience of `"some-other-api"`,
  calling `Microsoft.IdentityModel.Tokens.Validators.ValidateAudience` — the
  same pure function
  `BearerAudienceTests.A_token_minted_for_another_api_is_refused` calls.
- **FR-005** A Docker-free test asserts the WHEP parameters and the
  `JwtBearerOptions` built by `AddBearerAuthentication` agree on both
  `ValidateAudience` and the **set** of `ValidAudiences` (D3).
- **FR-006** A Docker-free test asserts the parameters do **not** throw for
  `"smart-sentinel-eye-api"` — the over-correction guard. Validating an audience
  nothing names would refuse every viewer instead of the wrong ones.
- **FR-007** The comment at the deleted line is replaced by one that says where
  the audience comes from, not that something is mirrored. No comment in this
  file asserts a relationship that nothing enforces.
- **FR-008** `AuthorizeWhep`'s `WithSummary` says the hook checks the audience
  along with issuer, signature and lifetime. It currently says "against the same
  Keycloak realm", which is the equivalence #2087 flagged as inviting too much.
- **FR-009** No existing test's assertions are edited. `WhepAuthIntegrationTests`
  mints through `AspireFixture.ClientId` = `smart-sentinel-eye-web`
  (`AspireFixture.Auth.cs:12,111`), which carries `sse-audience`, so it must pass
  unmodified.
- **FR-010** No realm file change. No new package reference. No new project
  reference.

## Success criteria

- **SC-001** The WHEP hook refuses a foreign audience, provable without Docker.
- **SC-002** The WHEP hook and the bearer pipeline are asserted to agree, so a
  change to one that is not made to the other fails the build.
- **SC-003** A token carrying `smart-sentinel-eye-api` is still accepted — the
  happy path is not traded for the fix.
- **SC-004** The full suite passes unmodified (FR-009).
- **SC-005** At phase 5, a real audience-less token is observed being refused
  by this endpoint, with the status code recorded.

---

## What breaks if the audience is enforced here

**The outage question, one endpoint smaller than #91's.** Answered from the
tree, not from memory.

The token the WHEP hook sees is the browser's OIDC access token:
`apps/shared/src/streaming/WhepClient.ts:109-114` sends
`Authorization: Bearer ${await this.opts.getToken()}` and
`apps/shared/src/ui/composites/useWhepSession.ts:82` documents that `getToken`
is `() => Promise.resolve(auth.user?.access_token)`. So the minting clients are
exactly the browser clients.

| Client | `sse-audience` in `defaultClientScopes`? | Verified at |
|---|---|---|
| `smart-sentinel-eye-web` | yes | realm JSON:143 |
| `management-web` | yes | realm JSON:168 |
| `kiosk-web` | yes | realm JSON:211 |
| `kiosk-wall` | yes | realm JSON:240 |

Default, never optional, so no minting path has to ask for it. **Nothing else
calls this endpoint**: `MTX_AUTHHTTPADDRESS` (`AppHost.cs:314`) is the only
configured caller, and the route is `AllowAnonymous` precisely because MediaMTX
is the only thing that reaches it.

**The runtime-created clients cannot reach this gate anyway.** Of the three
bundles in `KeycloakScopeBundles`, only `Kiosk` contains `sse.streams.read`;
`Device` (`sse.cameras.read`, `sse.events.publish`) and `WebhookIntegration`
(`sse.events.write`) are already answered 403 here, audience or not. A kiosk
client enrolled **before** #91 would lack `sse-audience` and would start being
refused — but spec 069 records (from constitution §VIII) that the kiosk app does
not use enrolled-kiosk clients today, and such a client has been refused by
every other endpoint since #91 merged. Enforcing here closes the last surface
still accepting a credential that is already dead everywhere else.

**Residual risk: a stale Keycloak volume.** If the running Keycloak's data
volume predates #91's realm import, its tokens have no `aud`, and this change
turns a working local WHEP session into a 401 that looks like a regression. That
is the same trap #91 had; the fix is `docker volume rm`, not a code change.

---

## Independent end-to-end test procedure

**Steps 3-7 need Docker, which is unresponsive on this machine and needs a
manual restart.** Steps 1-2 are the whole of the automated evidence and need
nothing.

1. `dotnet test tests/StreamDistribution.Infrastructure.Tests` — the three
   assertions of FR-004/005/006. No stack, no signing key, no minted token.
2. `dotnet test tests/ServiceDefaults.Tests` — spec 069's `BearerAudienceTests`
   still green, confirming the side this one is compared against did not move.
3. `docker volume rm` the Keycloak data volume, then
   `dotnet run --project src/AppHost` and wait for Keycloak healthy. Skipping
   the volume delete is the documented way to verify a realm that was never
   imported.
4. Mint through the **Aspire proxied endpoint** (not the container's mapped
   port, or the issuer will not match and everything 401s for the wrong reason):
   `POST {keycloak}/realms/smart-sentinel-eye/protocol/openid-connect/token`
   with `grant_type=password&client_id=smart-sentinel-eye-web&username=admin&password=Admin1234&scope=openid sse.management`.
   Decode the payload; record `aud` verbatim.
5. `POST {stream-distribution}/streams/authorize` with
   `{"token":"<that token>","path":"cam-<guid>"}` → **200**.
6. **The negative.** Remove `"sse-audience"` from `smart-sentinel-eye-web`'s
   `defaultClientScopes` in `src/AppHost/Realms/smart-sentinel-eye-realm.json`,
   repeat steps 3-4, repeat step 5 → **401**. Record the status and the body.
   Restore the file, repeat step 3, confirm step 5 returns to 200.
7. Open the kiosk at `http://localhost:5174`, sign in, open a wall. Video plays.
   That is the happy path observed rather than asserted.

Step 6 is a phase-5 drill rather than a test for the same reason spec 069 gave:
producing an audience-less token requires a realm that contradicts the realm
guards, and a test that edits the realm file under itself is worse than a
documented procedure.

---

## Locked tech choices

Keycloak per fab (ADR-0007, ADR-0008); JWT validated per service, never at the
gateway (ADR-0106); one shared `AddBearerAuthentication` (ADR-0051); argument
guards via `Ensure.That` (ADR-0105); xUnit + Shouldly, no Testcontainers
(ADR-0052, ADR-0103); sentence-style test names (ADR-0053).

## Latency budget impact

**N/A to all six legs of constitution §IV.** Audience validation is an in-memory
string comparison against an already-parsed token — no network call, no key
fetch, no allocation of consequence. It happens once per WHEP **open**, which is
not on the `event → overlay` path at all; the *event → overlay state* leg runs
over an already-established hub connection. `NFR001_JwtValidationLatencyTests`
covers validation cost for the bearer pipeline and is unaffected by one added
string comparison.

## Out of scope

- Unifying the issuer/authority derivation between `BindWhepAuthOptions` and
  `AddBearerAuthentication` (D5).
- The `AllowAnonymous` route and body-carried token. That is MediaMTX's
  protocol, is legitimate, and this issue does not ask to change it.
- #2085 — Mosquitto never checks `aud`. Different surface, different plugin
  (ADR-0100), different owner.
- A source-scanning guard forbidding a **third** hand-rolled
  `TokenValidationParameters`. There have only ever been two sites and one of
  them turned out not to be one (see above); a guard against a copy nobody has
  written is speculative generality (ADR-0036). If a second hand-rolled
  validator is ever proposed, that is the moment for it.
