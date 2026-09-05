# Tasks: The WHEP hook checks the audience too

**Spec**: `specs/071-the-whep-hook-checks-the-audience-too/spec.md` · **Plan**: `plan.md`
**Issue**: #2090 (`agent:ready`, `bug`) · **Lane**: autonomous, eligible —
plan.md Declaration 2 establishes there is no new ADR to write. **If a reviewer
asks for a shared `TokenValidationParameters` factory across all nine APIs, or
for the WHEP token to move into a header, the lane is blocked**: both are
architectural decisions and ADR-0144 bars the lane from making one.

**Phase 4a colour: RED** (behaviour-changing, plan.md Declaration 3).
T001's assertions 1 and 2 must be observed failing. **Assertion 3 is expected
green on arrival** and is declared here so its colour is not read as a phase-4a
failure — it is the over-correction guard, not the evidence.

**No characterisation control is declared.** The existing suite must pass
unmodified (FR-009, SC-004). A red in `WhepAuthIntegrationTests`,
`BearerAudienceTests` or `AuthorizeWhepCommandHandlerTests` is a design error in
the plan — block and report, do not edit an assertion.

---

## Parallelism (ADR-0109)

**One agent, `backend-engineer`, preceded by `test-writer`** (plan.md
Declaration 1). This feature is genuinely small — two source files, one test
file — so there is almost nothing to fan out, and saying so is more useful than
inventing markers.

**`[P]` applies to exactly one pair**: T002 and T003 own disjoint files
(`Infrastructure/Auth/WhepAuthValidator.cs` and `Api/StreamEndpoints.cs`) and
may be done in either order or together.

**Contention files**: `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`
is written by T002 and read by T001. Nothing else in the repo should be editing
it; if another worktree is, serialise.

**Foundational work: none.** `Shared.Kernel`, `Shared.Contracts`, `AppHost` and
the Aspire resource graph are all untouched (FR-010), so this feature blocks no
other feature and can be fanned out alongside anything else.

| Step | Agent | Tasks |
|---|---|---|
| 4a | `test-writer` | T001 |
| 4b | `backend-engineer` | T002 `[P]`, T003 `[P]` → T004 |
| 5 | `verify` | T005 |
| 3-gate | orchestrator | T006 |

---

## Commit shape

**Two commits.** Each builds on its own — the requirement, because rebase-merge
lands them individually on `develop` and a commit that only compiles with its
successor breaks `git bisect` forever.

1. `test(streams): the WHEP hook does not yet check the audience` — T001 alone.
   **Red by construction**, and that red is the evidence ADR-0139 asks to see
   quoted verbatim in the PR body.
2. `fix(streams): the WHEP hook checks the audience too` — T002 + T003.

Commit 1 compiles on its own **only if T001 calls a member that already
exists**. See T001's note on this: the test must reach the parameters through
the factory the fix introduces, which does not exist yet. Resolve it the way
T001 says — introduce the factory as a pure extraction in commit 1, with the
audience *not yet* set — rather than by merging the commits.

Conventional Commits (ADR-0030). **No `Co-Authored-By` footer** (ADR-0086),
regardless of any session-level attribution instruction.

---

## Task list

### T001 [US1] — the WHEP hook does not yet check the audience (phase 4a) — `test-writer`

New file: `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepAudienceTests.cs`.
**No Docker, no Aspire fixture, no signing key, no minted token.**

**First, the compile problem, because it decides the commit shape.** The
parameters are a private field built in `WhepAuthValidator`'s constructor, which
also constructs a `ConfigurationManager<OpenIdConnectConfiguration>`. The test
must not build a validator. So commit 1 performs a **pure extraction** — no
behaviour change:

```csharp
internal static TokenValidationParameters CreateParameters(string authority) => new()
{
    ValidateIssuer = true,
    ValidIssuer = authority,
    ValidateAudience = false, // unchanged in this commit — this is what T001 proves
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    NameClaimType = "preferred_username",
};
```

with the constructor calling it. `internal` suffices:
`SmartSentinelEye.StreamDistribution.Infrastructure.csproj` already grants
`InternalsVisibleTo` to `SmartSentinelEye.StreamDistribution.Infrastructure.Tests`
(added for `StreamHealthWatcher.DispatchAsync`, #1801). **Do not add a new
`InternalsVisibleTo`, and do not make the member public.**

That extraction is the *only* production edit in commit 1, and it changes no
behaviour — which is what lets the three assertions below be honest about what
they measure.

**Assertion 1 — the refusal, as a pure function. RED.**

```csharp
Should.Throw<SecurityTokenInvalidAudienceException>(() =>
    Validators.ValidateAudience(
        ["some-other-api"],
        securityToken: null,
        WhepAuthValidator.CreateParameters(Authority)));
```

where `Authority` is any string, e.g.
`"https://keycloak.invalid/realms/smart-sentinel-eye"` — it is never dialled.
`Microsoft.IdentityModel.Tokens.Validators.ValidateAudience` is the same public
static function the bearer handler calls; spec 069's
`BearerAudienceTests.A_token_minted_for_another_api_is_refused` already uses it
exactly this way.

**Today this does not throw**, because `ValidateAudience = false` makes the
function return without looking. The expected failure is Shouldly's
"Should throw SecurityTokenInvalidAudienceException but did not". **Quote it
verbatim.**

**Assertion 2 — the hook and the pipeline agree. RED.**

Build the real bearer options the way `BearerAudienceTests.BearerOptions()`
does — an *empty* builder, so no `appsettings.json` and no ambient environment
variable can supply the answer:

```csharp
HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
builder.AddBearerAuthentication();
using ServiceProvider provider = builder.Services.BuildServiceProvider();
JwtBearerOptions bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
```

Then compare against `WhepAuthValidator.CreateParameters(Authority)`:

- `ValidateAudience` equal on both — today `false` vs `true`.
- the **sets** of `ValidAudiences` equal — today `[]` (or null) vs
  `["smart-sentinel-eye-api"]`. Materialise both through `?? []` before
  comparing; "no audience configured" arrives as a **null** collection, and
  letting Shouldly dereference it reports an `ArgumentNullException` instead of
  the missing audience. That trap cost spec 069 a debugging round; do not
  re-learn it.

The custom message should say what the failure means, not what it is: *the WHEP
hook accepts tokens the nine APIs would refuse; the comment claiming it mirrors
them is the only thing that ever bound them (spec 071 FR-005).*

**Assertion 3 — the over-correction guard. GREEN ON ARRIVAL, by declaration.**

```csharp
Should.NotThrow(() =>
    Validators.ValidateAudience(
        [AuthenticationDefaults.ApiAudience],
        securityToken: null,
        WhepAuthValidator.CreateParameters(Authority)));
```

Green today for the wrong reason (validation is off) and green after the fix for
the right one. It exists so the refusal cannot be bought by validating an
audience nothing names, which would 401 every kiosk in the fab. **Its green
arrival is not a phase-4a failure** — plan.md Declaration 3 declares it. Say so
in the report; do not "fix" it into a red.

**If `Microsoft.IdentityModel.Tokens.Validators` has moved** in the pinned
package version, fall back to `new JwtSecurityTokenHandler().ValidateToken` with
a self-signed token and `SigningCredentials` the parameters accept — heavier,
same assertions. Report which route was used; do not drop an assertion.

Sentence-style test names (ADR-0053), Shouldly (ADR-0052).

Command:
`dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~WhepAudience"`

**Return the verbatim output.** It is the engineer's brief and the PR's
evidence, and the engineer may not edit this file to pass.

---

### T002 [P] [US1] — the WHEP hook checks the audience — `backend-engineer`

`src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`.

In `CreateParameters`:

- **delete** `ValidateAudience = false` and its comment. Do not write
  `ValidateAudience = true`: `true` is the framework default, and a line
  restating a default only makes the next reader wonder what it is for. This is
  the same call `6dac431a` made in `AuthenticationDefaults` (FR-001, spec D4).
- **add** `ValidAudiences = [AuthenticationDefaults.ApiAudience]` (FR-002).
  Plural, not `ValidAudience` and not `options.Audience` — the singular is what
  cost #91 a failing test, and the plural is the collection
  `Validators.ValidateAudience` reads.
- **add** a comment saying where the audience comes from and what holds the
  pairing — not that anything is mirrored (FR-007). Suggested:

  ```
  // The audience arrives on the sse-audience client scope (spec 069). Read from
  // the constant the bearer pipeline reads, so this hook cannot accept a token
  // the nine APIs would refuse; WhepAudienceTests holds the pairing.
  ```

`using SmartSentinelEye.ServiceDefaults;` is added. **This is not a new project
reference** — `SmartSentinelEye.StreamDistribution.Infrastructure.csproj`
already references `ServiceDefaults`. If you find yourself editing a csproj,
stop: something has been misread (FR-010; the one permitted exception is
plan.md Risk 1).

Do **not** touch: the `ConfigurationManager` construction, `MapInboundClaims`,
the `catch` blocks, `ValidateAsync`, `WhepAuthOptions`, `BindWhepAuthOptions`,
`IWhepAuthValidator`, `WhepAuthSubject`, or `AuthorizeWhepCommandHandler`'s
scope gate.

Turns T001's assertions 1 and 2 green **without touching the test file**.

Command:
`dotnet test tests/StreamDistribution.Infrastructure.Tests --filter "FullyQualifiedName~WhepAudience"`

---

### T003 [P] [US1] — the endpoint says what it checks — `backend-engineer`

`src/StreamDistribution/Api/StreamEndpoints.cs`, the `AuthorizeWhep`
`WithSummary` block only (FR-008).

The summary currently says the handler "validates that token itself against the
same Keycloak realm". Keep the shape, name the checks: issuer, signature,
lifetime **and audience** — then the existing sentences about
`sse.streams.read`, the grandfathered `sse.management` bundle, and the 401/403
split.

This is the clause #2087 flagged: "the same Keycloak realm" invites the reader
to assume equivalence with the bearer pipeline, which is precisely the assumption
that stopped holding. Naming the four checks removes the invitation.

**No routing, policy, `Produces` or `AllowAnonymous` change.** Disjoint from
T002, hence `[P]`.

---

### T004 [US1] — the suite, unmodified — `backend-engineer`

Run, in this order, and report each:

1. `dotnet test tests/StreamDistribution.Infrastructure.Tests`
2. `dotnet test tests/StreamDistribution.Application.Tests` — the handler's
   scope gate and `FakeWhepAuthValidator` must be untouched and green.
3. `dotnet test tests/ServiceDefaults.Tests` — `BearerAudienceTests` is the side
   this change is compared against; if it moved, the parity assertion is
   measuring the wrong thing.
4. `dotnet test tests/Architecture.Tests` — `BoundaryTests`,
   `PrimitiveBoundaryTests`, `HandlerDeconstructionTests`, `RealmAudienceTests`.

**Do not run a solution-wide build**: disk is at ~8.5 GB free (97%). Build the
projects these need, nothing more.

`tests/Integration.Tests` needs Docker, which is unresponsive on this machine.
**Do not attempt it, and do not report it as passing.** `WhepAuthIntegrationTests`
is the CI-side proof that the happy path survived (ADR-0103); name it in the PR
as deferred to CI, the way `6dac431a` named `TokenAudienceIntegrationTests`.

**If any existing assertion goes red, block and report.** FR-009 forbids editing
one to accommodate this change.

Then: format and analyzers clean, Release build of the touched projects only.

---

### T005 [US1] — phase 5 verification — `verify`

The procedure is spec.md *Independent end-to-end test procedure*.

Steps 1-2 run here and now. **Steps 3-7 need Docker and cannot run on this
machine** — the engine is unresponsive and needs a manual restart.

Write the verification note with what was actually observed, and say plainly
that steps 3-7 were **not run**, with the reason. Do not describe the feature as
verified end to end on the strength of the unit tests. The negative drill
(step 6) is the only place the refusal is observed against a real Keycloak, and
until it runs, SC-005 is outstanding.

If Docker returns before the PR merges, run steps 3-7 and record `aud` verbatim
plus both status codes. Delete the Keycloak **data volume**, not the container —
restarting keeps the old realm and the stack looks perfectly healthy.

---

### T006 — the phase-3 gate — orchestrator

`tasks.md` is the artifact this feature is tracked against; **no per-task
issues** (CLAUDE.md, the practice since spec 028). The gate is that #2090 is on
Project #13:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2090
```

Needs the `project` scope (`gh auth refresh -s project,read:project`).
`item-add` prints nothing on success; verify with `--limit 2000`, because
`item-list` defaults to 30 and a filled board reads as empty.

Then **stop**. Phase 3's gate is a human review of spec + plan + tasks.

---

## Dependencies

```
T001 (test-writer, RED)
  ├─→ T002 [P] ──┐
  └─→ T003 [P] ──┴─→ T004 ──→ T005
T006 runs independently, at the phase-3 gate.
```

- **T001 blocks T002 and T003.** The red must be observed and quoted first
  (ADR-0139); an engineer that writes the fix before seeing the failure has
  skipped phase 4a, which is the one phase with no exemption.
- **T002 and T003 are `[P]`** — disjoint files, no shared symbol.
- **T004 needs both.**
- **T005 needs T004 green.**
- Nothing here blocks another feature: no `Shared.*`, no `AppHost`, no Aspire
  resource, no migration (FR-010).
