# Spec 188 — An override the AppHost hears

**Issue:** [#2254](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2254)
— **already on Project #13** ("Smart Sentinel Eye"), status **Todo**, labels
`tech-debt`, `agent:ready`, no `agent:blocked`. Verified 2026-09-20 via
`gh project item-list 13 --owner smartsolutionslab --limit 2000` (the default
limit of 30 returns an empty-looking board — this repo's recorded trap).
**No `item-add` needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **186** as the
highest merged. Every remote branch was swept for `specs/NNN-*`: **186**
(`a-trace-that-keeps-its-secrets`) and **187** (`a-pin-the-model-resolves`) are
claimed by in-flight branches; **188, 189 and 190 are unclaimed**. This spec is
**188**. The number one above develop's highest is not automatically free — it
has collided before — so it was swept rather than assumed.

**ADRs referenced:**

- **Constitution §VI / ADR-024** (.NET Aspire is the composition root). The
  decision this spec serves: *"Aspire integrations are preferred over ad-hoc
  configuration."* The defect is the reverse — a call site taking a literal
  where Aspire's own parameter resolution reads configuration.
- **ADR-0103** (integration tests boot the real AppHost via `AspireFixture`;
  no Testcontainers). The fixture is one of the six call sites and the only one
  that boots containers, so it is where the mechanism is observed working.
- **ADR-0036** (smallest possible change; define "done" up front; no
  speculative generality; surface assumptions).
- **ADR-0037** / **ADR-0144** (the phased workflow; the autonomous lane and its
  phase-4a colours).
- **ADR-0105** (`Ensure.That(...)` argument guards — and its explicit **AppHost
  exemption**, which is why the helper this spec adds carries no guard).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **ADR-0139** (rules that fail the build, not the review; the red-first
  obligation).

**No new ADR is needed.** This spec decides nothing architectural. Aspire's
**own** default overload, `AddParameter(name, secret)`, is already
configuration-backed and reads `Parameters:{name}`; this AppHost happens to use
the literal-valued overload, which is not. The change moves ten call sites
*toward* the framework's own default behaviour, not away from it. It amends no
constitution section and alters no auth model, no bounded context, no contract.
See §6 for the one thing a reviewer might reasonably want written down as a
rule, and why it is not written down here.

**Latency budget (§IV): N/A.** The diff lands in `src/AppHost/` and
`tests/Integration.Tests/`. `src/AppHost` is a composition root, not a runtime
participant in `event arrival → overlay rendered`; no production assembly on any
of the six legs changes, and no leg's measurement status moves.

---

## 1. The finding, re-checked on this tree

The issue is a good one and its central mechanism is **correct**. Two of its
supporting claims are **not**, and both matter to the shape of the fix, so they
are corrected here rather than inherited.

### 1.1 The mechanism — confirmed, from the pinned assembly

`Aspire.Hosting` **13.5.3** (`Directory.Packages.props:32`), decompiled with
`ilspycmd` against the exact DLL the build resolves:

```csharp
// AddParameter(builder, name, value, publishValueAsDefault, secret)
//   delegates to AddParameter(builder, name, () => value, ...)
return builder.AddParameter(new ParameterResource(
    name,
    parameterDefault => parameterDefault == null
        ? valueGetter()                        // <- the literal, always
        : parameterDefault.GetDefaultValue(),
    secret)
{
    Default = publishValueAsDefault ? new ConstantParameterDefault(valueGetter) : null
});
```

Every one of this AppHost's ten call sites passes `publishValueAsDefault:
false` (it is the default and none overrides it), so `Default` is **null**, so
the callback returns `valueGetter()` — the literal — unconditionally.
**`builder.Configuration` is never consulted.** The issue is right: a
`Parameters:<name>=<value>` argument is inert.

For contrast, the configuration-backed overloads both route through:

```csharp
internal static string GetParameterValue(IConfiguration configuration, string name,
                                         ParameterDefault? parameterDefault, string? configurationKey = null)
{
    configurationKey ??= "Parameters:" + name;
    var value = configuration.GetValueWithNormalizedKey(configurationKey);
    return value ?? parameterDefault?.GetDefaultValue()
        ?? throw new MissingParameterValueException(...);
}
```

So `Parameters:<name>` is **exactly the key Aspire itself would read**. The
arguments in the tree are not merely plausible-looking; they are spelled
correctly for a mechanism this AppHost opted out of.

### 1.2 Correction 1 — it is **six** call sites, not two

The issue names `AppHostE2ESwitchTests` and `AspireFixture.cs` and says they
were "found by following one failure, not by an audit." The audit asked for in
the issue's own "worth checking while there" was run. Every `Parameters:`
occurrence in the tree, excluding `bin/`, `obj/` and `node_modules/`:

| File | Arrays | `Parameters:` args |
|---|---|---|
| `tests/Integration.Tests/Fixtures/AspireFixture.cs` | 1 (`parameters`) | 4 |
| `tests/Integration.Tests/AppHostE2ESwitchTests.cs` | 2 (`FixtureArguments`, `RunModeArguments`) | 8 |
| `tests/Integration.Tests/AppHostStackStatusTests.cs` | 1 (`E2EJobArguments`) | 4 |
| `tests/Integration.Tests/AppHostMediaMtxImageTests.cs` | 1 (`RunModeArguments`) | 4 |
| `tests/Integration.Tests/AppHostMigrationGateTests.cs` | 1 (`RunModeArguments`) | 4 |
| `tests/Integration.Tests/AppHostReplicaCountTests.cs` | 1 (`RunModeArguments`) | 4 |

**28 inert arguments across 6 files and 7 arrays.** The only other `Parameters`
hits in the tree are `RSAParameters` in five
`StreamDistribution.Infrastructure.Tests` files — the BCL type, unrelated.

Four distinct parameter names are ever passed: `PostgresUser`,
`PostgresPassword`, `KeycloakPassword`, `RabbitMqPassword`. **`AppHost.cs`
declares ten parameters** — see §2.

### 1.3 Correction 2 — the values do **not** agree with the defaults

The issue's explanation for the invisibility is: *"every such argument matches
the default it is trying to set. The values agree, so the silence is
invisible."*

**Three of the four do not match.** Measured against `src/AppHost/AppHost.cs`:

| Parameter | Argument value | Declared default | Agree? |
|---|---|---|---|
| `PostgresUser` | `postgres` | `postgres` | yes |
| `PostgresPassword` | `testpassword` | `dev-only-postgres-password` | **no** |
| `KeycloakPassword` | `testkeycloak` | `dev-only-keycloak-admin` | **no** |
| `RabbitMqPassword` | `testmessaging` | `dev-only-rabbit-password` | **no** |

The real reason the silence is invisible is different, and it changes the risk
profile of the fix. These are **self-consistent credentials**: the container
that sets the password and every consumer that reads it both obtain it from the
same `ParameterResource`, so *any* value works as long as it is used
throughout. The values are not equal — they are **irrelevant**. Nothing
compares them to anything.

This matters because it means turning the mechanism on is **not** a no-op at
runtime: the integration fixture's live Postgres, Keycloak and RabbitMQ
credentials change from `dev-only-*` to `test*`. §4 establishes that no consumer
holds a second copy of those literals, which is what makes the change safe. Had
the issue's premise been true, this paragraph would not exist and the risk would
have been missed.

### 1.4 Correction 3 — the end-to-end job passes no `Parameters:` at all

Five of the six files describe their array as the shape CI boots. Two examples:

- `AppHostStackStatusTests`: *"The shape the end-to-end job boots: parameters,
  plus the switch that disables the scenario simulator."*
- `AppHostMigrationGateTests`: *"The shape a developer's `aspire run` and the
  end-to-end job see: parameters only, no `E2ETests`."*

`.github/workflows/ci.yml:467` is the only AppHost boot line in CI:

```
nohup dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -c Release --no-build \
  -- ScenarioSimulator=false StackStatusFile="$GITHUB_WORKSPACE/stack-status.tsv" > apphost.log 2>&1 &
```

**No `Parameters:` argument.** Nor does a developer's `aspire run`. So those
comments are wrong twice over: the arguments do nothing, *and* the job they
claim to mirror does not pass them. Today that is invisible because the
arguments are inert in both places at once. §5 decides what to do about it.

### 1.5 The audit's negative result, which is the useful half

Three other argument-shaped switches are passed by the same arrays and the same
CI line — `E2ETests`, `ScenarioSimulator`, `StackStatusFile`. All three read
`builder.Configuration` directly (`AppHost.cs:17`, `:26`, `:719`) and
**therefore work**. There are no other configuration reads in `src/AppHost/`.

**`Parameters:` is the only inert override family in the tree.** The audit asked
for is complete and bounded: fixing it closes the category, not an instance.

---

## 2. The ten parameters, and why the fix cannot be scoped to four

`src/AppHost/AppHost.cs` declares ten parameters. Only the first four are ever
passed as arguments today:

| # | Name | Secret | Passed today | Mirrored in the realm JSON |
|---|---|---|---|---|
| 1 | `PostgresUser` | no | yes | — |
| 2 | `PostgresPassword` | yes | yes | — |
| 3 | `KeycloakPassword` | yes | yes | — |
| 4 | `RabbitMqPassword` | yes | yes | — |
| 5 | `IdentityAdminClientSecret` | yes | no | yes |
| 6 | `MigrationRunnerClientSecret` | yes | no | **yes** |
| 7 | `ScenarioSimulatorClientSecret` | yes | no | yes |
| 8 | `EventIngestionMqttClientSecret` | yes | no | yes |
| 9 | `StreamDistributionAttributionClientSecret` | yes | no | yes |
| 10 | `SystemVariablesSeederClientSecret` | yes | no | yes |

**The parameter spec 128 needed is #6, and it is not one of the four.** A fix
scoped to the arguments that exist today would not have helped the case the
issue was filed from. `AppHostMigrationGateTests` says so in its own words:

> The refusal itself was observed once, by hand, against a stack booted with a
> deliberately wrong `MigrationRunnerClientSecret`
> (`specs/128-a-failed-migration-is-loud/verification.md`).

"By hand" is the uncommitted one-token edit to the default that the issue
describes as the worse tool. Parameters 5–10 are each mirrored by a client
secret seeded in `src/AppHost/Realms/smart-sentinel-eye-realm.json`, which is
precisely why overriding one is a clean way to inject a failing dependency: the
service presents a secret the realm does not know, and the dependency fails for
a real reason rather than a mocked one.

**The fix therefore covers all ten.** A mechanism that works for four of ten is
the same trap with a smaller mouth.

---

## 3. Prioritized user stories

### US1 (P1) — A `Parameters:` argument changes what the AppHost composes

**As** an engineer writing a test that needs a deliberately-broken dependency,
**I want** `Parameters:<name>=<value>` on the AppHost command line to actually
set that parameter, **so that** I can inject a wrong secret, an unreachable host
or a bad connection string without editing source.

Independently shippable: yes. It is the whole mechanism, and it is observable
end to end on its own (§7).

#### Acceptance scenarios

```gherkin
Scenario: an override is honoured (the happy path — new behaviour)
  Given the AppHost declares MigrationRunnerClientSecret with a literal default
  When the application model is built with the argument
       "Parameters:MigrationRunnerClientSecret=deliberately-wrong"
  Then the MigrationRunnerClientSecret parameter resolves to "deliberately-wrong"

Scenario: every declared parameter honours its own override
  Given the AppHost composes N parameter resources
  When the model is built with an override argument for every one of them
  Then each parameter resolves to the value its own argument supplied
  And N is read from the composition, never from a list written in the test

Scenario: no argument leaves the default in place (the conflict case —
          a fix that broke this would break every boot in the repo)
  Given no Parameters: argument is passed
  When the application model is built
  Then MigrationRunnerClientSecret resolves to "dev-only-migration-runner-secret"
  And every other parameter resolves to the literal declared at its call site

Scenario: an empty override is not an override (the bad-request case)
  Given the argument "Parameters:MigrationRunnerClientSecret=" is passed
  When the application model is built
  Then MigrationRunnerClientSecret resolves to "dev-only-migration-runner-secret"
  And the behaviour matches Aspire's own GetParameterValue, which treats
      null-or-empty as absent

Scenario: a misspelled parameter name cannot silently re-create the defect
  Given the override key is derived from the parameter's own name at the single
        point where the parameter is declared
  When a new parameter is added to the AppHost
  Then its override key is correct by construction, and the every-parameter
       scenario above covers it without being edited

Scenario: the override source is the command line and the environment, and
          nothing else (auth/scope)
  Given the AppHost's configuration providers as Aspire composes them
  When no Parameters: key is present in any provider
  Then the declared literal is used
  And a secret parameter's resolved value is still marked sensitive and still
      absent from the stack-status report, whose three-field line shape
      (StackStatusReport) can carry only a name, a state and an exit code
```

### US2 (P2) — The fixture boots on the credentials it asks for

**As** a maintainer, **I want** the four arguments `AspireFixture` has always
passed to take effect on the stack it boots, **so that** the mechanism is
exercised by the one call site that starts containers rather than only by a
model-shape test.

Depends on US1. Not independently shippable; it is the consequence slice, and it
carries the only real runtime risk in this spec.

#### Acceptance scenarios

```gherkin
Scenario: the integration stack comes up on the overridden credentials
  Given AspireFixture passes Parameters:PostgresPassword=testpassword and the
        other three
  When the fixture boots the AppHost in E2ETests mode
  Then every resource reaches a started state
  And the whole Docker-requiring integration suite is as green as it was before
      the change, test for test

Scenario: no consumer holds a second copy of the old literal
  Given the defaults dev-only-postgres-password, dev-only-keycloak-admin and
        dev-only-rabbit-password
  When the tree is searched for each outside src/AppHost/AppHost.cs
  Then the only hit is e2e/wall-withdrawal.spec.ts's fallback for
       SSE_KEYCLOAK_ADMIN_PASSWORD, which runs against the CI e2e stack — a boot
       line that passes no Parameters: argument and therefore still sees the
       default
```

### US3 (P3) — Five comments stop claiming a shape CI does not boot

**As** the next reader of `AppHost*Tests`, **I want** the arrays' doc comments
to say what CI actually boots, **so that** a guard documented as mirroring the
end-to-end job is not quietly pointed somewhere else.

Independently shippable: yes — it is comment-only and could ship alone. It is P3
because it is the audit's finding, not the issue's, and dropping it loses no
capability.

#### Acceptance scenarios

```gherkin
Scenario: the comments describe the real boot line
  Given ci.yml:467 boots with ScenarioSimulator= and StackStatusFile= only
  When a reader opens AppHostStackStatusTests or AppHostMigrationGateTests
  Then the comment does not claim the end-to-end job passes Parameters:
       arguments

Scenario: nothing but comments changed
  Given the five test files before and after the change
  When every comment and XML doc is stripped and the remainder hashed
  Then the two hashes are identical, file for file
```

---

## 4. Why turning the mechanism on is safe

Each default literal, searched across the whole tree (`--include` for `*.cs`,
`*.json`, `*.yml`, `*.yaml`, `*.ts`, `*.md`, `*.ps1`, `*.sh`; `specs/`, `bin/`,
`obj/` and `node_modules/` excluded):

| Literal | Occurrences outside `AppHost.cs` |
|---|---|
| `dev-only-postgres-password` | **none** |
| `dev-only-rabbit-password` | **none** |
| `dev-only-keycloak-admin` | one — `e2e/wall-withdrawal.spec.ts:43`, as the fallback for `SSE_KEYCLOAK_ADMIN_PASSWORD` |
| `dev-only-migration-runner-secret` | one — `Realms/smart-sentinel-eye-realm.json:279` |

The one e2e hit is not a hazard: that suite runs against the CI stack booted at
`ci.yml:467`, which passes no `Parameters:` argument, so `KeycloakPassword` keeps
its default there and the fallback stays correct.

The realm-JSON hits are the **point**, not a hazard: a mirrored secret is what
makes an override a working failure injector. Nothing reads the realm JSON to
validate the parameter; the two are deliberately kept in step by hand, and an
override deliberately takes them out of step.

**No consumer compares a parameter's value to a literal.** That is what makes the
fixture's credential change from `dev-only-*` to `test*` a safe consequence
rather than a breakage.

---

## 5. What is deliberately **not** changed

**The 28 arguments stay where they are.** Removing them was considered in both
places and rejected in both:

- **In `AspireFixture`** they become real and are wanted — they give the
  integration stack credentials distinct from a developer's `aspire run` stack.
- **In the five model-only test classes** `CreateAsync` builds the model and
  starts nothing, and a `ParameterResource`'s value is resolved lazily, so
  nothing there reads a parameter value either before or after this change.
  Keeping them costs nothing and gains a little: once US1 lands, those five
  classes build the model through the configuration-lookup path rather than the
  literal path, so they exercise the new code incidentally.

Removing them would have been a change with no observable effect, in five files,
in service of a comment. **The comment is what misleads, so the comment is what
US3 fixes.**

**Option 2 from the issue — "remove them and take the defaults directly" — is
rejected**, on the issue's own reasoning plus one this spec adds: it would throw
away the capability, and it would leave spec 128's actual need
(`MigrationRunnerClientSecret`, never passed as an argument at all) exactly where
it is. Removing four arguments would not have removed the trap; it would have
removed the only signpost pointing at it.

---

## 6. The one thing a reviewer might want written down, and why it is not

This spec establishes, in effect, that **every AppHost parameter is overridable
from the command line or the environment, including the nine marked
`secret: true`**. A reviewer could reasonably ask for that as a stated rule.

It is not written down as one, for three reasons:

1. It is already Aspire's own default. `AddParameter(name, secret)` — the
   overload this AppHost does not use — reads `Parameters:{name}` and has no
   fallback at all. This spec lands *between* that and today's literal: read
   configuration, fall back to the literal.
2. The AppHost is a dev and compose-time tool. In `publish` mode the values come
   from the publish target regardless, and per `CLAUDE.md` the Aspire k8s
   publisher has never been run here.
3. **The autonomous lane may not write an ADR or amend the constitution**
   (ADR-0144). If a reviewer wants the convention recorded, that is a separate,
   human-started issue — and this paragraph is the pointer to it.

---

## 7. Independent end-to-end test procedure

Not "the tests pass". This is the observation spec 128 could not make, run
against a real stack, and it is phase 5's verification note.

1. From a clean worktree on the branch, with no other Aspire stack running (this
   repo's recorded trap: one machine, one Aspire stack — a second concurrent
   boot yields `FailedToStart` that reads exactly like a code defect), boot the
   AppHost with a deliberately wrong migration secret:

   ```sh
   dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj \
     -- ScenarioSimulator=false Parameters:MigrationRunnerClientSecret=deliberately-wrong
   ```

2. **Expected:** the `migrations` resource fails, and the failure is the one spec
   128 wanted — the MigrationRunner cannot mint its Keycloak service-account
   token, because the realm seeds `dev-only-migration-runner-secret` and the
   runner now presents something else. Capture the resource's state and the
   console log line naming the authentication failure.

3. **Control run.** Boot the same command line **without** the `Parameters:`
   argument. `migrations` must complete normally. Without this half the
   observation proves only that the stack can fail, which was never in doubt.

4. **Counterfactual.** Run step 1 against `origin/develop`. `migrations` must
   **succeed** — the argument being ignored is the whole defect, so the same
   command line must behave differently before and after. This is the step that
   distinguishes "the override works" from "something else broke the stack", and
   it is the one this repo's own record says is most often skipped.

5. **The fixture's own credentials.** With the integration suite's stack up
   (`E2ETests=true`), confirm the Postgres container is actually running on
   `testpassword` — e.g. the connection string Aspire injects into a service,
   read from the dashboard's environment view for that resource. Record the value
   observed, not merely that the suite was green: a green suite is consistent
   with the argument still being ignored.

**Stop conditions, reported rather than worked around:** if step 1's stack fails
for a reason other than the migration secret; if step 4 shows `origin/develop`
also failing; or if step 5 shows `dev-only-postgres-password` still in use after
the change.

---

## 8. Locked tech choices

Nothing new. `Aspire.Hosting` 13.5.3 as pinned; xUnit + Shouldly (ADR-0052); the
`FixtureLogic` trait for Docker-free model-only tests, as the five existing
`AppHost*Tests` already use; `AspireFixture` for anything that boots (ADR-0103);
no Testcontainers; no new package.

The helper US1 introduces lives in `src/AppHost/`, which is **exempt from
ADR-0105's `Ensure.That(...)` requirement** by that ADR's own terms, so it
carries no argument guard.
