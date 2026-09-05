# Tasks: A token names the API it is for

**Spec**: `specs/069-a-token-names-the-api-it-is-for/spec.md` · **Plan**: `plan.md`
**Issue**: #91 (`agent:ready`, `tech-debt`) · **Lane**: autonomous, eligible —
plan.md Declaration 2 establishes there is no new ADR to write. **If a reviewer
asks for per-context audiences instead, the lane is blocked**: that is an
architectural decision and ADR-0144 bars the lane from making one.

**Phase 4a colour: RED** (behaviour-changing, plan.md Declaration 3).
**No characterisation control is declared.** The existing suite must pass
unmodified (SC-005); a red in `KioskScopeParityTests`, `RealmIdentityTests` or
`WebhookBearerValidationIntegrationTests` is a design error in the plan — block
and report, do not edit the assertion.

---

## Parallelism (ADR-0109)

**One agent, `infra-engineer`, preceded by `test-writer`** (plan.md
Declaration 1). The `[P]` markers below are therefore about *ordering within
`test-writer`*, not a fan-out across agents.

**The four test files are disjoint and touch four different assemblies**, so
`test-writer` may write and run them in any order. **T004 needs Docker and is
the long pole** — start it first; T001-T003 finish inside its runtime.

**The contention file is `src/AppHost/Realms/smart-sentinel-eye-realm.json`.**
Four test files read it and T005 rewrites it. Nothing else in the repo may be
editing it concurrently; if another worktree is, serialise.

**Foundational**: T005 (the realm) is what every other green depends on. Nothing
in `Shared.Kernel`, `Shared.Contracts` or an Aspire *resource* changes, so there
is no wider blocker for other features.

| Step | Agent | Tasks |
|---|---|---|
| 4a | `test-writer` | T004 first, then T001, T002, T003 — all `[P]` |
| 4b | `infra-engineer` | T005 → T006 → T007 → T008 |
| 5 | `verify` | T009 |
| 3-gate | orchestrator | T010 |

---

## Commit shape

**Two commits.** Both build on their own, which is the requirement (CLAUDE.md;
rebase-merge lands them individually on `develop`).

1. `test(auth): a token that does not name this API is not yet refused` — the
   four test files. **Red by construction**, and that red is what ADR-0139 asks
   to see quoted in the PR body.
2. `fix(auth): a token names the API it is for` — T005 + T006 + T007 together.

**They are one commit because they are one rule.** Splitting the realm from the
`ValidateAudience` flip would produce a commit in which the services demand an
audience the realm does not emit — a bisect landmine, and the exact shape of the
production outage the spec describes. If they are ever split anyway, the order
is **realm first, services last**, for the same reason the deployment order is.

No `Co-Authored-By` footer (ADR-0086), regardless of any session-level
attribution instruction.

---

## Task list

### T001 [P] [US1] — the options refuse a foreign audience (phase 4a) — `test-writer`

New file: `tests/ServiceDefaults.Tests/BearerAudienceTests.cs`. **No Docker.**

Build the options the nine APIs actually get:

```csharp
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
builder.AddBearerAuthentication();
using ServiceProvider provider = builder.Services.BuildServiceProvider();
JwtBearerOptions options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
```

An empty builder, so no `appsettings.json` and no environment leak in. The
authority URL is never dialled — `AddJwtBearer` fetches metadata on first
request, not at configuration time.

**Three assertions, all red today:**

1. `options.TokenValidationParameters.ValidateAudience.ShouldBeTrue(...)` —
   `AuthenticationDefaults.cs:62` sets it `false`.
2. `options.TokenValidationParameters.ValidAudiences.ShouldContain("smart-sentinel-eye-api")`
   — nothing sets an audience today. **Assert the literal**, not a constant:
   the constant does not exist yet, and a test that cannot compile is not a red
   test. Pinning to the realm is T002's job.
3. **The refusal itself**, as a pure function on the configured parameters:

   ```csharp
   Should.Throw<SecurityTokenInvalidAudienceException>(() =>
       Validators.ValidateAudience(["some-other-api"], securityToken: null,
           options.TokenValidationParameters));
   ```

   Today this returns without throwing, because `ValidateAudience` is off. This
   is the honest negative, and it needs no stack and no signing key —
   `Validators.ValidateAudience` is the same function the bearer handler calls.

   **If `Microsoft.IdentityModel.Tokens.Validators` has moved** in the pinned
   package version, fall back to `new JwtSecurityTokenHandler().ValidateToken`
   with a self-signed token and a `SigningCredentials` the parameters accept —
   heavier, same assertion. Report which was used; do not drop the assertion.

Command: `dotnet test tests/ServiceDefaults.Tests --filter "FullyQualifiedName~BearerAudience"`

---

### T002 [P] [US1] — the realm declares the audience, on every client (phase 4a) — `test-writer`

New file: `tests/Architecture.Tests/RealmAudienceTests.cs`. **No Docker.**
Mirror `RealmIdentityTests` — same `ReadRealm()` walk to `SmartSentinelEye.slnx`,
same held `JsonDocument`, same per-client loop with a message naming the client.

**Four assertions, all red today:**

1. `The_realm_defines_an_audience_scope` — `clientScopes` contains
   `sse-audience`; `include.in.token.scope` is `"false"`; it carries exactly one
   `protocolMappers` entry whose `protocolMapper` is `oidc-audience-mapper`.
2. `The_audience_scope_names_this_product's_api` — that mapper's
   `config.included.custom.audience` is `"smart-sentinel-eye-api"` and
   `config.access.token.claim` is `"true"`. **Assert the same literal T001
   asserts.** Between the two files that literal is checked against the realm
   file *and* against the options the services build, so editing one source
   without the other fails. That is FR-009's guarantee; it does not need a
   shared constant to hold, and asserting a constant against itself would not
   have given it.
3. `Every_client_holds_the_audience_scope` — loop over `clients`, each must list
   `sse-audience` in `defaultClientScopes`. **Per client, not by sampling**: the
   custom message must name the failing clientId, because the reader's next
   question is always *which one*. Nine failures today.
4. `No_client_carries_a_private_audience_mapper` — restates spec 042 FR-005 for
   this claim specifically. Green today and expected to stay green; it exists
   because the issue asks for exactly the thing it forbids.

**Do not assert `sse-audience` is absent from `KeycloakScopeBundles`** here —
that is T003's subject and this file has no reason to reference
`Identity.Application`.

Command: `dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~RealmAudience"`

---

### T003 [P] [US1] — a client created at runtime gets it too (phase 4a) — `test-writer`

New file: `tests/Identity.Application.Tests/KeycloakAdmin/RuntimeClientAudienceTests.cs`.
**No Docker.**

**This is the outage guard.** Three handlers create Keycloak clients through the
Admin API — `EnrollKioskCommandHandler`, `RegisterDeviceCommandHandler`,
`RotateWebhookClientCommandHandler` — and
`KeycloakClientRepresentation` has **no `protocolMappers` field at all**, so
`DefaultClientScopes` is the only thing that decides what lands in their tokens.
No existing test in this repository would notice their tokens losing the
audience: `WebhookBearerValidationIntegrationTests:40` substitutes
`management-web` for a rotated client.

Reuse the existing fake/`Mock<IKeycloakAdminClient>` these handlers are already
tested with — **read the neighbouring test files first and mirror them; do not
introduce a new fake**. Capture the `KeycloakClientRepresentation` passed to
`CreateClientAsync`.

**Six assertions, three red:**

For each of the three handlers, red today:
`representation.DefaultClientScopes.ShouldContain("sse-audience", customMessage: ...)`
— the message must say that without it the client's token is refused by every
API the moment audience validation is on.

And for each, green today and asserted so a later "tidy-up" cannot trade one for
the other: the representation still contains **every** entry of the
corresponding `KeycloakScopeBundles` list.

Command: `dotnet test tests/Identity.Application.Tests --filter "FullyQualifiedName~RuntimeClientAudience"`

---

### T004 [P] [US1] — a real minted token carries it (phase 4a) — `test-writer`

New file: `tests/Integration.Tests/Identity/TokenAudienceIntegrationTests.cs`.
**Needs Docker. Start this first.**

Mirror `TokenAttributionIntegrationTests` — `[Collection(AspireCollection.Name)]`,
`AspireFixture` injected, a private `AudiencesOf(string token)` helper built on
`JwtSecurityTokenHandler` exactly as that file's `SubjectOf` is.

**Why this exists when T002 already reads the file**: reading names cannot see a
mapper that is present and does not fire. `RealmIdentityTests`' own class remarks
make that point about `sse-identity`; a mistyped Keycloak config key is
discarded in silence, and this realm has already lost thirty-two scope names
that way. **This is the only assertion that would catch it.**

**Three assertions, all red today:**

1. `A_minted_token_names_the_api_it_is_for` — `aspire.GetAccessTokenAsync(
   AspireFixture.AdminUsername, AspireFixture.AdminPassword)`, then
   `AudiencesOf(token).ShouldContain("smart-sentinel-eye-api")`. This is the
   `smart-sentinel-eye-web` path every other integration test authenticates
   through, so its red is also the reason the whole suite would break if T005
   were shipped without T006.
2. `A_service_accounts_token_names_it_too` — a `client_credentials` grant for
   `scenario-simulator` (secret `dev-only-scenario-simulator-secret`; see
   `PlantFloor.cs` for the request shape). Covers the machine half of the
   inventory.
3. `A_client_enrolled_at_runtime_mints_a_token_that_names_it` — enrol through
   Identity, then `client_credentials` with the returned secret, then assert the
   audience. `NFR002_MqttConnectAuthTests` already does the enrol-then-mint
   dance; mirror it rather than inventing one.

**Do not attempt a negative here.** Minting an audience-less token requires a
client that contradicts FR-003, and a test that rewrites the realm file under a
running stack is worse than the documented drill. The negative lives in T001 (as
the exact function) and in T009 step 6 (as a real 401).

Command: `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~TokenAudience"`

---

### T005 [US1] — the realm carries the audience — `infra-engineer`

`src/AppHost/Realms/smart-sentinel-eye-realm.json`.

1. Add the `sse-audience` client scope next to `sse-identity`, exactly as
   plan.md → *The realm change, exactly* spells it out.
2. Add `"sse-audience"` to `defaultClientScopes` on **all nine** clients:
   `smart-sentinel-eye-web`, `management-web`, `kiosk-web`, `kiosk-wall`,
   `identity-admin`, `migration-runner`, `stream-distribution-attribution`,
   `scenario-simulator`, `event-ingestion`.
3. **Default, never optional.** An optional scope must be requested by name, and
   four minting paths do not control the `scope` parameter.
4. **No client gains a `protocolMappers` array** (FR-004).
5. **Check the description length.** The new scope's description must stay under
   255 characters; a longer one kills the realm import and hangs the whole
   Aspire fixture with the stack reporting itself healthy.

Verify: T002 goes green. `RealmIdentityTests` and `KioskScopeParityTests` must
stay green — if either reddens, stop (plan.md → *Why this does not break the
three existing realm guards* explains why neither should).

---

### T006 [US1] — the services validate it — `infra-engineer`

`src/ServiceDefaults/AuthenticationDefaults.cs`.

1. Add `public const string ApiAudience = "smart-sentinel-eye-api";` beside
   `KioskClientId`, with a remark saying what it is and that T001/T002 pin it to
   the realm from two directions.
2. `options.Audience = ApiAudience;`
3. **Delete** line 62 (`options.TokenValidationParameters.ValidateAudience = false;`).
   Delete it; do not set it to `true`. The framework default is `true`, and a
   line asserting a default invites the next reader to wonder what it is for.
4. **Rewrite the comment at `:59-61`.** It currently says a bearer-only client
   and audience mapper *"lands when the Identity context is built out (spec
   TBD)"* — a deferral note that outlived the deferral by the whole life of this
   shortcut. Replace it with what is true: the audience arrives on the
   `sse-audience` client scope, every realm client carries it, and clients
   created at runtime get it from `KeycloakScopeBundles.AudienceScope`.

Verify: T001 goes green.

---

### T007 [US1] — clients created at runtime carry it — `infra-engineer`

Four files in `src/Identity/Application`.

1. `KeycloakAdmin/KeycloakScopeBundles.cs` — add
   `public const string AudienceScope = "sse-audience";` with a remark saying
   why it is a second spelling of a string `ServiceDefaults` also holds: ADR-0051
   keeps this layer ASP.NET-free, which is why every `sse.*` string in this file
   is already re-spelt rather than imported. **Do not add a project reference to
   `ServiceDefaults` to remove the duplication** — that reference is the thing
   ADR-0051 forbids.
2. **Leave `Kiosk`, `Device` and `WebhookIntegration` unchanged** (FR-008).
   Adding a non-permission entry to `Kiosk` fails
   `KioskScopeParityTests.The_kiosk_client_grants_everything_an_enrolled_kiosk_device_does`,
   because the bundle side of that comparison is not filtered through
   `IsPermission` while the realm side is.
3. Append at the three call sites:
   `Commands/Handlers/EnrollKioskCommandHandler.cs`,
   `Commands/Handlers/RegisterDeviceCommandHandler.cs`,
   `Commands/Handlers/RotateWebhookClientCommandHandler.cs` —
   `DefaultClientScopes: [.. KeycloakScopeBundles.Kiosk, KeycloakScopeBundles.AudienceScope]`
   and the two equivalents. A collection expression with a spread, per the house
   rule; `var` cannot express it.

Verify: T003 goes green; `KioskScopeParityTests` stays green.

---

### T008 [US1] — the whole suite, unmodified — `infra-engineer`

1. `dotnet test tests/Architecture.Tests tests/ServiceDefaults.Tests tests/Identity.Application.Tests`
2. `dotnet test tests/Integration.Tests` — the full suite, not a filter. T004's
   assertion 1 covers the client every other integration test authenticates
   through, so a missed client shows up here as a broad failure rather than a
   subtle one.
3. **Stop the running AppHost before building.** A live stack holds the service
   binaries and MSB3027 looks exactly like a broken build.
4. **No existing test's assertions may be edited** (SC-005). If one has to be,
   the design is wrong — report, do not adjust.
5. Format and analyzers clean, Release build (`dotnet_style_prefer_collection_expression`
   is a warning and fails Release).

---

### T009 [US1] — phase 5 verification — `verify`

Follow spec → *Independent end-to-end test procedure*. Three points are not
optional:

**Delete the Keycloak data volume, not just the container.**

```sh
docker rm -f $(docker ps -aq --filter "name=keycloak")
docker volume ls | grep -i keycloak     # then docker volume rm <each>
```

Keycloak keeps the imported realm in its volume. Restart it without deleting
the volume and it silently serves the **old** realm while every service demands
the new audience — the stack reports itself perfectly healthy, every request
401s, and a verifier who skips this step concludes the mapper works when it was
never imported. This is the single most likely way this feature fails in the
field.

**Mint from the Aspire proxied endpoint, not the container's mapped port**, or
the issuer will not match and everything 401s for the wrong reason.

**Record the decoded `aud` verbatim** in the verification note. A note saying
"audience validation works" discharges nothing.

Then the negative, step 7 of the procedure: remove `"sse-audience"` from
`smart-sentinel-eye-web` only, delete the volume, boot, and call a protected
`GET`. **Quote the status code, the empty body, and the full `WWW-Authenticate`
header** — that header is the only place the diagnosis appears, because a bearer
challenge is not an exception and none of the `AddExceptionHandler`
registrations or `AddProblemDetails` runs. Restore the file and confirm the call
returns to 200.

Latency: **N/A**, and say so explicitly. No leg of constitution §IV is on this
path; audience validation is an in-memory comparison on an already-parsed token
and the hub authenticates once at handshake, not per frame.

---

### T010 — the phase-3 gate — orchestrator

Issue #91 is already feature-level, which is the granularity Project #13 tracks
(no `[TNNN]` issues since spec 028). Add it to the board if it is not there:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/91
```

`item-add` prints nothing on success, and `item-list` defaults to 30 items —
verify with `--limit 2000` and query by `content.url`, not by number.

**Do not run `/speckit-taskstoissues`.** Ten task issues on a board used for
in-flight feature tracking is the drift CLAUDE.md corrected.

---

## Dependencies

```
T004 ─┐
T001 ─┤
T002 ─┼─→ T005 ─→ T006 ─→ T007 ─→ T008 ─→ T009
T003 ─┘
                      T010 (independent, any time)
```

- T001-T004 are `[P]` with each other and **all four block T005**: the red is
  observed and captured verbatim before any production file moves. That is
  ADR-0144's phase-4 split, not a scheduling preference.
- T005 → T006 is ordered, not parallel: the reverse order produces a stack in
  which every request 401s.
- T009 needs T008 green and a **freshly-imported** realm.
