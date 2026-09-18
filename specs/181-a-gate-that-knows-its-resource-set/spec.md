# Spec 181 — A gate that knows its resource set

**Issue:** #2268 — "The e2e readiness gate probes three ports, so a missing service reports green"
**Type:** bug (CI infrastructure)
**Lane:** ADR-0144 autonomous (`agent:ready`, Project #13, status Todo)
**ADRs:** ADR-0108 (Playwright e2e against a live `aspire run` stack), ADR-0033
(CI gates — the e2e job is one of four required checks), ADR-0103 + ADR-0068
(Aspire fixture is the integration mechanism; no Testcontainers), ADR-0024 /
`0000-initial-decisions.md` row 024 (Aspire AppHost is the composition root),
ADR-0037 (this workflow), ADR-0144 (the lane).
**New ADR needed:** no — see §8, which also names the one finding that *would*
make one necessary.

---

## 0. Scope

One vertical slice: **the e2e job's readiness gate learns the resource set the
AppHost actually composed, and refuses to open when any member of it has not
started.**

Explicitly **not** in this spec:

- Changing what the Playwright suite tests, or adding a test for MinIO or
  audit-observability behaviour. The gate is the subject; suite coverage is a
  separate question.
- Changing the integration (Docker) job or `AspireFixture`. That job already
  reported the failure correctly — it is the precedent here, not the patient.
- The MinIO registry fix (#2266) or the outage (#2265). This issue is
  pre-existing and was filed separately so those stayed small.
- Reducing the e2e job's boot time. §6 records the budget impact; optimising it
  is not this slice.

---

## 1. The premise, checked before specifying

Per the standing lesson that a board issue's premise can be stale by the time it
is delivered, each claim was re-read against `origin/develop` @ `9da906f1`.

| Claim | Verdict |
|---|---|
| `scripts/wait-for-e2e-stack.sh` gates on nine databases' migrations, ports 5173/5174/5175, and a gateway→camera-catalog 401 | **Holds.** Lines 27–143. |
| It never asks about `minio` or `audit-observability` | **Holds.** Neither name appears in the file. |
| It has no notion of the resource set it expects | **Holds.** The only enumerated set is `DATABASES` — nine database names, hand-written. |
| `audit-observability` `WaitFor`s `minio` | **Holds**, at `src/AppHost/AppHost.cs:467`. The issue cites `:447`; the file has drifted ~20 lines since filing. Corrected here rather than repeated. |
| `AspireFixture.FormatFailedResourceReport` names every dead resource | **Holds**, `tests/Integration.Tests/Fixtures/AspireFixture.cs:881+`, with `IsHealthy` / `FatalStartupStates` above it at 178–241. |
| The gateway probe is best-effort and the migration probe deliberately is not | **Holds**, and the migration probe's own comment (lines 25–26) states the stance this spec reuses: *"I could not ask" is not "the schema is there"*. |

One thing the issue does **not** say, and which decides the mechanism:
**`audit-observability` is a project resource, not a container.** So the
obvious cheap fix — cross-referencing `docker ps` against the composition — is
blind to the very resource in the evidence. §5 records that as a rejected
option rather than leaving it for phase 4 to rediscover.

A second thing the issue does not say: **the composition is conditional.**
`isRunMode`, `isE2ETests` and `isScenarioSimulator` (AppHost.cs:14–24) each add
or remove resources, and the e2e job boots with `-- ScenarioSimulator=false`.
Any expected set derived by *parsing* `AppHost.cs` would be wrong by
construction for at least one of the three shapes.

---

## 2. User stories, prioritised

### US1 (P1) — A resource that never started closes the gate

*As the e2e job, when a resource the AppHost composed never reaches a started
state, I refuse to run the Playwright suite, so that a job named "full stack"
cannot report green over a stack that is missing a service.*

Independently shippable and independently observable: run the gate against a
stack description in which one resource is absent, and see it exit non-zero
where today it exits zero.

### US2 (P2) — The refusal names the offenders

*As whoever reads the red job, I am told which resources did not start and what
state each reached, so that I can act on the log without re-running anything.*

Mirrors `FormatFailedResourceReport`'s shape — the behaviour the issue names as
the one the gate lacks. Separable from US1: US1 is the exit code, US2 is the
text.

### US3 (P3) — The gate cannot silently stop being asked

*As the repo, if the boot command and the AppHost switch ever disagree, the
build fails, so that the gate cannot quietly return to being three port probes.*

This is the failure mode the issue is an instance of — a record narrower than
it claims. A gate whose input is produced by a separate step is exactly the
shape that drifts.

---

## 3. Acceptance scenarios (Gherkin)

Terms used below:

- **status report** — the file the AppHost writes describing every resource it
  composed and the state each reached. Format fixed in `plan.md` §3.
- **started** — `Running`, or `Finished` with exit code `0` (the one-shot
  `migrations` resource). Everything else is not started, including `Waiting`,
  `Starting`, `NotStarted`, `FailedToStart`, `Exited`, `RuntimeUnhealthy`,
  `Terminated`, and any state the script does not recognise.

### US1 — happy path

```gherkin
Scenario: every composed resource started
  Given the AppHost has written a status report naming every resource it composed
    And every one of them is Running, except migrations which is Finished with exit code 0
    And the nine databases carry applied migrations
    And ports 5173, 5174 and 5175 serve
   When the readiness gate runs
   Then it exits 0
    And it reports that the full composed resource set started
```

### US1 — the regression this spec exists for

```gherkin
Scenario: a resource that could not be pulled at all
  Given the AppHost has written a status report naming every resource it composed
    And minio is FailedToStart
    And audit-observability is Waiting
    And every other resource is Running
    And the nine databases carry applied migrations
    And ports 5173, 5174 and 5175 serve
    And the gateway answers 401 for camera-catalog
   When the readiness gate runs
   Then it exits non-zero
    And the Playwright suite does not run
```

> This scenario is #2264's stack exactly. Against today's script it exits **0**.
> That is the red observation phase 4a must record.

### US1 — a resource that ended badly is fatal immediately

```gherkin
Scenario: a one-shot resource that ended non-zero
  Given the AppHost has written a status report
    And migrations is Finished with exit code 1
   When the readiness gate runs
   Then it exits non-zero on the first poll
    And it does not spend the remaining wait budget on resources that wait for it
```

### US1 — "I could not ask" is not "it started"

```gherkin
Scenario: no status report at all
  Given no status report exists for the whole wait window
   When the readiness gate runs
   Then it exits non-zero
    And the message names the switch that produces the report
```

```gherkin
Scenario: a status report from a previous boot
  Given a status report exists that the current boot did not write
   When the boot step runs
   Then the stale file is removed before the AppHost starts
    And the gate therefore never reads it
```

### US2 — the refusal names the offenders

```gherkin
Scenario: the failure report
  Given the gate is refusing because minio is FailedToStart and audit-observability is Waiting
   When it writes its failure
   Then the output names minio and its state
    And names audit-observability and its state
    And does not name any resource that started
```

```gherkin
Scenario: the report carries no secrets
  Given the status report describes a composition containing secret parameters
   When it is written
   Then it contains no parameter value, connection string, password or endpoint URL
    And it contains only resource names, states and exit codes
```

### US3 — the wiring is guarded

```gherkin
Scenario: the boot command and the AppHost switch agree
  Given the AppHost writes a status report only when the switch is set
   When the guard test runs
   Then it fails unless the e2e job's boot command in .github/workflows/ci.yml sets that switch
```

```gherkin
Scenario: the expected set is the composition, not a list
  Given the AppHost composes a different set of resources under ScenarioSimulator=false
   When the expected set is computed
   Then it is taken from the built application model
    And no resource name is written down anywhere outside the composition
```

### Bad request / auth

**N/A, stated rather than omitted.** This slice adds no HTTP surface, no
endpoint, no authorisation decision and no user input. The only trust question
is the one in the second US2 scenario: what may appear in a file that is printed
into a public CI log. That is an acceptance criterion above, not a review
finding to be found later.

---

## 4. Independent end-to-end test procedure

Runnable by hand, on a machine with Docker and the repo:

```sh
# 1. BEFORE any change, prove the defect. Requires only bash + node.
cd <repo>
node --test scripts/wait-for-e2e-stack.test.mjs
# Expect: the existing three tests pass. Add the new one from T001 and expect
# it to FAIL with "the gate opened over a stack missing minio (#2268)".
# Record that failure verbatim — it is the phase-4a red.

# 2. AFTER the change, the same command.
node --test scripts/wait-for-e2e-stack.test.mjs
# Pass: every test passes, the three pre-existing ones unmodified.

# 3. The AppHost really writes the file (this is the half no stub can prove).
rm -f ./stack-status.tsv
dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj \
  -- ScenarioSimulator=false StackStatusFile="$PWD/stack-status.tsv"
# In a second shell, while it boots:
cat ./stack-status.tsv
# Pass: one line per composed resource, appearing BEFORE the stack is up
#       (early lines show Waiting/Starting), converging to Running /
#       Finished 0. No parameter names, no passwords, no URLs.
# Record the observed line count and compare it to the composition.

# 4. The gate opens against that real stack.
bash scripts/wait-for-e2e-stack.sh
# Pass: exits 0, and its first section reports the resource set.
# Record the wall-clock time this section took — T010 needs the figure.

# 5. Counterfactual, by hand (memory: prove a guard by counterfactual).
#    With the stack up and the gate known to pass, edit the status report to
#    set minio's state to FailedToStart, then re-run the gate.
bash scripts/wait-for-e2e-stack.sh
# Pass: exits non-zero AND the output names minio. If it exits non-zero
#       without naming it, US2 is not satisfied — that is a blocker, not a nit.

# 6. US3's guard.
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  --filter "Category=FixtureLogic"
# Pass: green. Then temporarily delete `StackStatusFile=` from ci.yml's boot
# step and re-run: it must go red. Revert.
```

Step 3 and step 5 are the ones that cannot be delegated to CI: no stub can
prove the AppHost writes the file, and no green run proves the gate would have
gone red.

---

## 5. Locked tech choices, and the options rejected

**Chosen: the AppHost writes its own resource-state report; the gate reads it.**

The AppHost already holds the authoritative answer to both halves of the
question — which resources the composition contains, and what state each
reached — via `ResourceNotificationService`, the same mechanism `AspireFixture`
uses under ADR-0103/ADR-0068. Nothing new is introduced: no transport, no
dependency, no tool on the runner.

| Rejected option | Why |
|---|---|
| Query the Aspire dashboard resource service | It is gRPC (`aspire.v1.DashboardService`) behind an API key. Reading it from bash needs `grpcurl` plus the proto, neither present on the runner. The issue explicitly warns not to assume the fixture's approach ports over; this is the form of that warning that turns out to bite. |
| Cross-reference `docker ps` against the composition | Blind to project resources. `audit-observability` — the resource in the issue's own evidence — is `AddProject`, not a container. It would not catch the reported failure. |
| Grep `AppHost.cs` for `.AddX(...)` registrations | The composition is conditional on three switches (§1). A static parse is wrong for at least one shape, and is itself the hand-written list whose drift the issue is about. |
| Add a "last" Aspire resource that `WaitFor`s everything and opens a port | Observable from bash, but says nothing about *which* resource is missing when it never starts — losing exactly the half the issue praises in `FormatFailedResourceReport`. |

Locked, consistent with the stack table and existing patterns:

- Switch passed as an **AppHost command-line argument**, the way
  `ScenarioSimulator=false` already is — not an env var. This also makes US3's
  guard a direct sibling of the existing `AppHostE2ESwitchTests`, which already
  reads `ci.yml` for that argument.
- The set of resources to expect is **every resource in the built model that is
  not `IResourceWithoutLifetime`** — Aspire's own marker for parameters and
  connection strings, which have no lifetime and can never be `Running`. A type
  filter, not a name list.

  > **Correction (T001, phase 4b):** `IResourceWithoutLifetime` turned out not
  > to hold this role in Aspire.Hosting 13.5.3 — decompiling the assembly
  > during the live-boot spike showed nothing implements it, `ParameterResource`
  > included, so it would have excluded zero resources and leaked all 10 of
  > this AppHost's secret parameters into the report. The filter actually
  > shipped is `resource is not ParameterResource` — still the type filter
  > this bullet asks for, naming the type that exists in this SDK version
  > rather than a vestigial marker. See `plan.md` §2.2 and the PR body.
- The started/not-started classification lives **in the shell script**, so it is
  testable by `scripts/wait-for-e2e-stack.test.mjs` without booting anything.
- The existing migration, port and gateway probes **stay**. The resource gate
  answers "did the process start"; the migration probe answers "does the schema
  exist", and the port probes answer "is Vite listening", which a `Running` npm
  app resource does not prove. Three different questions; the smallest change
  adds one, deletes none.
- Test framework unchanged: `node --test` for the script (run by `pnpm test` in
  the web job), xUnit + `[Trait("Category", "FixtureLogic")]` for the guard (run
  by the Docker-free fixture-logic step, `ci.yml:67`).

---

## 6. Latency-budget impact

**N/A — not on the event→overlay path.** This is CI infrastructure. No leg of
constitution §IV's table is touched, and no runtime code path changes.

**CI budget impact, which is the real cost and is recorded instead:** the e2e
job has `timeout-minutes: 45` (raised for #2376). The resource gate adds a wait
of up to 10 minutes to the worst case. In the normal case it costs nothing:
whatever it waits for, the migration and port probes below it were already
waiting for, so those return on their first poll once it opens. T010 records the
observed duration from the first green CI run so the claim is measured rather
than asserted.

---

## 7. Phase 4a colour — **red**

Behaviour-changing, and the change is precisely "a case that passes must
fail". Ambiguity resolves to red (ADR-0144) and there is none here.

Three tests must be observed failing before any implementation:

1. `scripts/wait-for-e2e-stack.test.mjs` — the gate opens over a stack whose
   status report names `minio` as `FailedToStart`. Red today because the script
   never reads a status report; it exits 0.
2. `scripts/wait-for-e2e-stack.test.mjs` — the gate opens when no status report
   exists at all. Red today for the same reason.
3. `tests/Integration.Tests/AppHostStackStatusTests.cs` — the e2e boot command
   sets the switch, and the AppHost's expected set excludes
   `IResourceWithoutLifetime` resources. Red today because neither exists.

The verbatim failure output of all three goes in the PR body (ADR-0139).

**Not characterisation.** The three existing tests in
`wait-for-e2e-stack.test.mjs` must pass **unmodified** before and after — they
cover the migration probe, which this slice does not change. An edit to any of
their assertions is evidence the change reached further than it should.

---

## 8. New ADR needed?

**No**, and the reasoning is stated so it can be checked rather than trusted:

- The mechanism is Aspire's own resource notifications, already the repo's
  chosen integration mechanism under ADR-0103 and ADR-0068. No new technology.
- The AppHost is already the composition root and the place runtime resources
  are described (stack table, row "Orchestration"). Writing what it composed is
  within that role.
- ADR-0108 already decided that e2e runs against a live stack Aspire owns; this
  makes the job's readiness question match that decision instead of
  approximating it with ports.

**The one finding that would change this:** if phase 4 discovers that
`ResourceNotificationService` cannot be watched from an AppHost started by
`dotnet run` — and the only remaining route is the gRPC resource service, a new
runner dependency (`grpcurl`), or a new transport — that is an architectural
decision the autonomous lane may not make. **Stop, label `agent:blocked`, and
say so.** Do not add a tool to the runner and call it an implementation detail.

---

## 9. Agents for the remaining phases

| Phase | Agent | Why |
|---|---|---|
| 4a | `test-writer` | Writes the three red tests, runs them, returns verbatim output. May not implement. |
| 4b | `infra-engineer` | Aspire AppHost wiring, a bash gate, a GitHub Actions workflow. No domain, application or frontend code is touched. |
| 6 | `infra-reviewer` | Stack bootability, CI workflow correctness, and the question that agent exists to ask — whether a green run proves anything. |
| 6 | `security-reviewer` | **Not warranted.** No auth surface, no endpoint, no secret handling. The one trust question — what may appear in a file printed into a public log — is an acceptance criterion in §3, enforced by the `IResourceWithoutLifetime` filter and asserted by a test, not deferred to review. |

---

## 10. Files phase 4 may touch

| File | Change |
|---|---|
| `src/AppHost/StackStatusReport.cs` | new — the writer |
| `src/AppHost/AppHost.cs` | two lines: build, attach, run |
| `scripts/wait-for-e2e-stack.sh` | new first section |
| `scripts/wait-for-e2e-stack.test.mjs` | new tests; the three existing ones unmodified |
| `.github/workflows/ci.yml` | boot step: remove any stale report, pass the switch |
| `tests/Integration.Tests/AppHostStackStatusTests.cs` | new — US3's guard |
| `.gitignore` | ignore the report |

Anything outside this list is out of scope for #2268.
