# Spec 340: The realm keys arrive over TLS

**Feature Branch**: `fix/2238-env-conditional-require-https` (cut from `origin/develop` at `064846d3`)

**Created**: 2026-10-12

**Status**: Phases 1-3 complete. No `[NEEDS CLARIFICATION]` remains. The policy was decided by the
repository owner on 2026-10-12 (below). The mechanism is derived in §3 from evidence. Assumption
**A1** (§3.2) was confirmed by the lane's coordinator on 2026-10-12 as the natural implementation
of that decision, not a second fork.

**Input**: Issue [#2238](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2238),
*Discovery and JWKS are fetched over plaintext HTTP, and the compensating Helm overlay does not
exist*. Decision comment (repository owner, 2026-10-12): *"environment-conditional
RequireHttps/sslRequired — fail-closed in a declared prod environment, permissive under the dev/CI
stack where Aspire wires Keycloak over plain HTTP by design. Applies uniformly to WhepAuthValidator
and AuthenticationDefaults. … no deploy-chart work needed yet, just the config switch."*

**Scope cut by the orchestrator, from the issue's own "what would actually fix it" section**: the
two .NET call sites only. The realm JSON's `"sslRequired": "none"` is **out of scope** (§7), even
though the decision comment names `sslRequired`.

**Spec number.** 340. Checked twice on 2026-10-12, the second time at T001, against
`origin/develop` (highest: 339), every branch after `git fetch` (`git log --all -- 'specs/340*'` is
empty), and the sibling `D:/Github/sse-*` worktrees (only this one holds a `340-*`). **Check again before opening
the PR.**

**ADRs and constitution sections referenced:**

- **ADR-0007 / ADR-0008** (decision table rows 007-008), Keycloak OIDC, and **ADR-0023**, the
  bearer pipeline `AuthenticationDefaults` implements.
- **ADR-0130**, the honest-record audit. It records that no production deployment exists. That is
  why the permissive default was never backed by anything.
- **ADR-0051**, per-context `Add<Context>Infrastructure` DI extensions. The WHEP options binding
  stays in `StreamDistributionInfrastructureModule`.
- **ADR-0105**, `Ensure.That` guards.
- **ADR-0139 / ADR-0144** and constitution §Testing. This is a behaviour-changing fix, so the
  colour is **red**. §6 declares the Development twins, which are expected to pass before the fix.
- **ADR-0171**, whose `MediaMtxApiOptions` startup validation is the pattern reused here.
- Constitution **§VIII**, safe by default at trust boundaries.

**No new ADR is needed** (§3.4). The policy is the owner's decision. The mechanism reuses two
things the repository already relies on. The first is `IHostEnvironment`, already used to gate
dev-only surface in all nine context APIs. The second is `ValidateOnStart`, already used in two
places for startup-critical configuration. It needs no new environment concept, no configuration
key and no AppHost change.

**Latency budget impact: N/A.** The change alters the transport of the OIDC discovery and JWKS
fetch and adds a startup check. Neither lies on any of the six legs in constitution §IV.
`/streams/authorize` is on the stream-open path, not on the event→overlay path. Its per-request
cost is unchanged, because the scheme check runs once at startup.

---

## 1. The defect, as it stands on `064846d3`

There are exactly two places where .NET fetches the realm's discovery document and JWKS.
`grep -rn "AddJwtBearer\|HttpDocumentRetriever" src` finds no others.

| Site | Setting | Reaches |
|---|---|---|
| `src/ServiceDefaults/AuthenticationDefaults.cs:66` | `options.RequireHttpsMetadata = false;` unconditionally | All nine context APIs (`AddBearerAuthentication()` in each `Program.cs`) |
| `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs:90` | `new HttpDocumentRetriever { RequireHttps = false }` unconditionally | The MediaMTX WHEP auth hook, `/streams/authorize` (`AllowAnonymous`) |

Both documents decide which signing keys and which issuer the service trusts. If they are fetched
over plaintext, an on-path attacker can substitute a JWKS and mint tokens that the service accepts.
Spec 120 (#2161) made the WHEP fetch on-demand: any anonymous caller presenting an unknown `kid`
triggers it, with a 5-minute floor. The REST APIs already behaved this way through
`RefreshOnIssuerKeyNotFound`. So an attacker can now **choose the moment** of the fetch.

**On the comments the issue calls stale.** Both have already been corrected to stop naming a
Helm overlay. `AuthenticationDefaults` was fixed in `eeb09961` (#2273, which closed #2180), and
`WhepAuthValidator` in `c9c88f99`. They are now accurate descriptions of an **unconditional**
permissive default. This spec makes both untrue: the default stops being unconditional, and
`AuthenticationDefaults`' "Do not flip it here" stops being the instruction. So rewriting them is
**in scope as part of the change**, not a drive-by. A third comment repeats the same reasoning,
in `AddRevocationCheck` (`AuthenticationDefaults.cs:203-206`: "matching AddBearerAuthentication's
own RequireHttpsMetadata = false reasoning above"). So does a fourth, the doc comment on
`WhepMetadataSourceTests` ("`RequireHttps = false` is not asserted here … load-bearing on the
dev/CI http authority"). Both go stale too and are corrected in the same change.

## 2. User stories

The two call sites ship as **one slice**, deliberately. The owner's decision says "applies
uniformly", and the issue's own point is that fixing one site leaves the others. Shipping US1
without US2 would recreate the asymmetry the issue was filed against. Each story is still
independently testable.

### US1 (P1): The nine REST APIs refuse plaintext realm metadata outside Development

As the operator of a fab deployment, I need every API to fetch the realm's discovery document and
JWKS only over TLS whenever it is not running under the dev/CI stack. If it is misconfigured to do
otherwise, it must refuse to start. It must not quietly run with substitutable signing keys.

**Independent test**: compose `AddBearerAuthentication()` in-process on a host whose environment is
Production, and run the host's startup validation. No network is needed.

```gherkin
Scenario: Happy path. A non-Development host with an https authority starts strict
  Given a host whose environment is "Production"
  And ConnectionStrings:keycloak is "https://keycloak.invalid"
  When AddBearerAuthentication() is composed and startup validation runs
  Then startup validation passes
  And the JwtBearer scheme's effective RequireHttpsMetadata is true

Scenario: Conflict. An http authority outside Development refuses to start
  Given a host whose environment is "Production"
  And ConnectionStrings:keycloak is "http://keycloak.invalid"
  And RevocationList:ClientId and RevocationList:ClientSecret are set (so nothing else fails)
  When startup validation runs
  Then it throws
  And the message says the metadata authority must use HTTPS

Scenario: Conflict. A later configure that re-opens plaintext refuses to start
  Given a host whose environment is "Production" with an https authority
  And a Configure<JwtBearerOptions> registered after AddBearerAuthentication sets RequireHttpsMetadata = false
  When startup validation runs
  Then it throws an OptionsValidationException naming RequireHttpsMetadata and the environment

Scenario: Bad request. Every non-Development environment name is strict, not only "Production"
  Given a host whose environment is "Staging" (or any name other than "Development")
  When AddBearerAuthentication() is composed
  Then the effective RequireHttpsMetadata is true
```

**Auth scenario.** The request-time behaviour is unchanged. A token is still validated against
keys from the configured authority. What this story changes is whether those keys could have
arrived over plaintext. The REST pipeline's 401 and 403 behaviour, the audience check and the
revocation hook (spec 270) are untouched. The existing `BearerAudienceTests`,
`BearerRevocationHookTests` and `RegisteredPolicyTests` must stay green **without edits**. They
already compose a Production host with an `https://` authority, so they are the strict path's own
characterisation.

### US2 (P1): The WHEP hook refuses plaintext realm metadata outside Development

As the operator, I need the MediaMTX authorization hook, which decides who may open a camera
stream, to follow the same rule as US1, through the same single policy.

**Independent test**: the `internal` seam `WhepAuthValidator.MetadataSourceFor` and the WHEP
options binding, both composed in-process.

```gherkin
Scenario: Happy path. A non-Development host with an https authority starts strict
  Given a host whose environment is "Production"
  And ConnectionStrings:keycloak is "https://keycloak.invalid"
  When the WHEP auth options are bound and startup validation runs
  Then startup validation passes

Scenario: Conflict. An http authority outside Development refuses to start
  Given a host whose environment is "Production"
  And ConnectionStrings:keycloak is "http://keycloak.invalid"
  When the WHEP auth options are bound and startup validation runs
  Then it throws an OptionsValidationException saying the WHEP metadata authority must use HTTPS

Scenario: Conflict. The retriever itself refuses plaintext outside Development (defence in depth)
  Given the metadata source built for environment "Production" and authority "http://127.0.0.1:1/realms/x"
  When the configuration is requested
  Then it fails with IDX20108 (https required), before any connection is attempted

Scenario: Bad request. A malformed authority outside Development refuses to start
  Given a host whose environment is "Production" and an authority that is not an absolute URI
  When startup validation runs
  Then it throws (a malformed authority is not an https authority)
```

**Auth scenario.** `/streams/authorize` stays `AllowAnonymous` (it is MediaMTX's hook), and its
200/401/403/429 answers are unchanged. In Production, a realm reached over http can no longer
produce a 503 `IdentityProviderUnavailable` on every request. That state can no longer start at
all. Today that 503 would be the symptom, misleadingly logged as an unreachable realm.

### US3 (P1, preserving): The dev/CI stack is unchanged

As a developer or CI runner, I need the Aspire stack, which wires Keycloak over plain HTTP by
design, to boot and authenticate exactly as it does today.

**Independent test**: the Development twins (§6) plus the existing Docker-backed suites.

```gherkin
Scenario: Development stays permissive for the bearer pipeline
  Given a host whose environment is "Development" and an http authority
  When AddBearerAuthentication() is composed and startup validation runs
  Then startup validation passes and the effective RequireHttpsMetadata is false

Scenario: Development stays permissive for the WHEP hook
  Given the metadata source built for "Development" and authority "http://127.0.0.1:1/realms/x"
  When the configuration is requested
  Then it fails with a connection error, and not with IDX20108

Scenario: The Aspire stack still boots and authenticates
  Given the AspireFixture boots the stack (each service runs in "Development", §3.1)
  When the integration suite runs
  Then WhepAuthIntegrationTests and every bearer-authenticated integration test stay green
```

## 3. Mechanism, and why no new concept is needed

### 3.1 What signals "dev/CI" today: `IHostEnvironment`, already correct

- All nine context APIs and `MigrationRunner` ship `Properties/launchSettings.json` with
  `ASPNETCORE_ENVIRONMENT` (or `DOTNET_ENVIRONMENT`) set to `Development` in every profile. Aspire
  run mode applies each project's launch profile. Nothing in `src/AppHost` or `.github/workflows`
  overrides the environment, and no workflow passes `--no-launch-profile`. The e2e job runs
  `dotnet run --project src/AppHost/...` (`ci.yml:810`).
- **Observed, not inferred.** `WhepAuthorizeRateLimitTests.Authorize_declares_the_429_it_can_answer`
  (`tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs:369`) fetches
  `/openapi/v1.json` from the fixture-booted `stream-distribution` and asserts `200`.
  `StreamDistribution/Api/Program.cs:245` maps that endpoint only `if
  (app.Environment.IsDevelopment())`. So a green CI run already proves the fixture boots services in
  Development. `DefaultEndpointsTests`' class comment records the same fact.
- The nine `Program.cs` files already use `app.Environment.IsDevelopment()` to gate dev-only
  surface (`MapOpenApi`). This spec adds a second consumer of the existing signal. It does not
  invent one.
- **Counterfactual built in.** If the signal were wrong and the fixture ran services as
  Production, every service would refuse to start under this change (the dev authority is
  `http://`). The integration suite would then fail loudly, not pass vacuously.

### 3.2 The rule: strict unless `IsDevelopment()`

`RequiresHttpsMetadata(environment) = !environment.IsDevelopment()`. It is defined **once**, as a
public static on `AuthenticationDefaults`, and read by both call sites. That is the same pairing
`WhepAuthValidator` already uses for `AuthenticationDefaults.ApiAudience`, which
`WhepAudienceTests` holds.

**Assumption A1 (confirmed 2026-10-12 by the lane's coordinator).** The decision says "fail-closed in a *declared* prod
environment, permissive under the dev/CI stack". It does not say what happens to an environment
that is neither, such as `Staging`, a custom name, or **an environment that was never set**. .NET
defaults an unset environment to `Production`. This spec reads "fail-closed" as **strict everywhere
except `Development`**. The alternative, strict only when `IsProduction()`, would leave Staging and
custom names permissive. That fails open for exactly the misconfiguration the decision is about.
Under A1, "declared prod" includes "declared nothing", which is the conservative direction.

### 3.3 "Refuse to start": `ValidateOnStart`, the established pattern

The repository already validates startup-critical configuration this way. The examples are
`MediaMtxApiOptions` (ADR-0171, `StreamDistributionInfrastructureModule.cs:216-223`) and
`RevocationListOptions` (`AuthenticationDefaults.cs:169-182`). This spec adds two more of the same
shape (plan §3).

- **Bearer.** The framework's `JwtBearerPostConfigureOptions` already throws
  `InvalidOperationException` ("The MetadataAddress or Authority must use HTTPS unless disabled for
  development by setting RequireHttpsMetadata=false") when the flag is true and the authority is
  `http://`. **It throws lazily, though**: on the first authenticated request, not at startup. So
  today a strict host with an http authority would start and then 500 every request. Registering
  `ValidateOnStart()` on the named `JwtBearerOptions` moves that throw to startup. An explicit
  `Validate` that requires the *effective* `RequireHttpsMetadata` to be true outside Development
  catches a later `Configure<JwtBearerOptions>` that flips it back.
- **WHEP.** `HttpDocumentRetriever { RequireHttps = true }` throws IDX20108 lazily, at the first
  fetch. `WhepAuthValidator.ValidateAsync` would report that as `IdentityProviderUnavailable` on
  every open, which looks like an unreachable realm rather than a misconfiguration. A `Validate` on
  `WhepAuthOptions` (authority must be an absolute `https` URI outside Development) with
  `ValidateOnStart()` moves it to startup. The retriever's own flag stays as defence in depth.

### 3.4 Why this is not an ADR

There is no second-order fork hiding in the mechanism:

- **Environment detection.** It uses the framework abstraction already used for dev-only gating,
  with no new flag, no AppHost wiring and no Aspire-specific signal. `ExecutionContext.IsRunMode`
  is an AppHost-side concept. A service cannot see it, and it would be the wrong axis in any case
  (run mode vs publish mode, not trusted vs untrusted).
- **No configuration knob.** `RequireHttps` is derived from the environment and nothing else. No
  configuration key can re-open plaintext outside Development, because adding one would rebuild
  the permissive default under a new name. If a future on-prem deployment needs a non-TLS realm
  outside Development, that is a **new decision needing an ADR**. It is recorded in plan §6 as the
  escalation trigger, not pre-built.
- **Startup validation.** It is an existing pattern (§3.3).

## 4. Functional requirements

- **FR-001** One policy, `AuthenticationDefaults.RequiresHttpsMetadata(IHostEnvironment)`, returns
  `true` for every environment except `Development` (A1). Both call sites read it. Neither keeps a
  literal `true` or `false`.
- **FR-002** `AddBearerAuthentication` sets `RequireHttpsMetadata` from FR-001's policy for the
  builder's environment.
- **FR-003** Outside Development, the bearer scheme's options are validated at startup. Startup
  fails if the effective `RequireHttpsMetadata` is false, and it fails if the authority is not
  `https`. The authority failure comes from the framework's own post-configure check, which
  `ValidateOnStart` forces to run at startup.
- **FR-004** `WhepAuthValidator.MetadataSourceFor` builds its `HttpDocumentRetriever` with
  `RequireHttps` from FR-001's policy. The DI constructor obtains the environment from
  `IHostEnvironment`.
- **FR-005** Outside Development, the WHEP options are validated at startup. Startup fails unless
  the authority is an absolute URI with scheme `https`.
- **FR-006** Each startup failure message names the setting, says HTTPS is required outside
  Development, and names the remedy: serve Keycloak over TLS, or run under the `Development`
  environment.
- **FR-007** In Development, behaviour is byte-for-byte what it is today. Both sites are
  permissive, and no new validation fails for an `http://` authority.
- **FR-008** The four comments listed in §1 are rewritten to describe the conditional rule. None
  of them may name a Helm overlay or any other control that does not exist.

## 5. Success criteria

- **SC-001** Every red test in §6 is observed failing on `064846d3` before the fix, and the
  failure is quoted in the PR body (ADR-0139).
- **SC-002** After the fix, the full `ServiceDefaults.Tests`, `StreamDistribution.Infrastructure.Tests`
  and `Architecture.Tests` suites, plus the Docker-backed `Integration.Tests`, are green. No
  existing assertion is edited. A fixture whose placeholder authority trips the new strict path
  gets a configuration fix with a stated reason (plan §5). Its assertion is not changed.
- **SC-003** Phase 5 observes a real API host refusing to start when it runs as Production against
  an http Keycloak, and observes the Aspire stack booting and serving an authenticated request and
  a WHEP authorize as before.

## 6. Phase 4a: colour declaration

**Red (behaviour-changing).** Each of these must be observed failing first:

| # | Test | Red today because |
|---|---|---|
| R1 | Bearer, Production + https: effective `RequireHttpsMetadata` is `true` | it is `false` |
| R2 | Bearer, `Staging` and a custom name: effective `RequireHttpsMetadata` is `true` | it is `false` |
| R3 | Bearer, Production + http authority (revocation credentials set): startup validation throws, and the message names HTTPS | nothing runs the post-configure at startup, and the flag is `false` anyway |
| R4 | Bearer, Production + https + a later configure setting the flag `false`: startup validation throws `OptionsValidationException` | no such validation exists |
| R5 | WHEP, Production: `MetadataSourceFor(http://127.0.0.1:1/…).GetConfigurationAsync` fails with an inner IDX20108 | today it tries to connect and fails with a socket error |
| R6 | WHEP, Production + http authority: startup validation of `WhepAuthOptions` throws | no such validation exists |
| R7 | WHEP, Production + a malformed authority: startup validation throws | no such validation exists |

R1 to R4 and R5 to R7 run over the **same** set of environment names. That is the uniformity
proof: the two halves of the claim are tested in the same shape, as `WhepAudienceTests` pairs the
audience.

**Development twins, expected green before and after.** These are not red tests. A green result
before the fix is correct, not a phase-4 failure. They pin FR-007, and each must be seen passing on
`064846d3` and pass **unmodified** after:

| # | Twin |
|---|---|
| G1 | Bearer, Development + http authority: startup validation passes, and `RequireHttpsMetadata` is `false` |
| G2 | WHEP, Development: `MetadataSourceFor(http://127.0.0.1:1/…)` fails with a connection error, not IDX20108 |
| G3 | WHEP, Development + http authority: startup validation passes. Before the fix this passes vacuously, because no validator is registered. It must still pass after. |

The Docker-backed `WhepAuthIntegrationTests` and the rest of the AspireFixture suite are the
system-level twin: dev/CI over http, unchanged.

## 7. Out of scope

- **Realm `"sslRequired": "none"`** (`src/AppHost/Realms/smart-sentinel-eye-realm.json:4`). It is excluded by the
  issue's own fix scope and by the orchestrator's brief. Note that the owner's decision text does
  name `sslRequired`, so this is a known residue, not an oversight. It needs a deploy-time realm
  variant, which belongs to #1015. **Tracked as #2878** (filed 2026-10-12, `tech-debt`, not
  `agent:ready`: it needs its own scoping, and probably an ADR).
- **`UseHttpsRedirection`**: no service calls it. Recorded as evidence in the issue, and not part
  of this fix.
- **Inter-service `http://` clients** (for example `http://identity` for the revocation list,
  `AuthenticationDefaults.cs:208`). These are a different transport, not realm metadata, and
  belong with #1015's TLS story.
- **The Keycloak URL lookup order** (`ConnectionStrings` → `services:keycloak:http:0` →
  `…:https:0`, in both sites). Outside Development, a deployment that publishes both endpoints
  through service discovery would select http and then refuse to start. That fails closed and says
  so, which is acceptable. Reordering the lookup is #1015's wiring.
- Any Helm, chart or AppHost change.
