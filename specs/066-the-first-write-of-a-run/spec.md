# Feature Specification: The first write of a run

**Feature Branch**: `fix/2014-the-first-write-of-a-run`

**Created**: 2026-09-04

**Status**: Draft — phases 1–3 complete, phase 4a not started

**Issues**: #2014

**Input (#2014)**: "`e2e/system-variables.spec.ts` defines a variable and waits
for it to appear. Both tests in that file fail on a **freshly booted** stack and
pass on a warm one. In one observed case the dialog was still showing
**\"Saving…\"** with the button disabled — a request in flight, not a rejection.
It is the first write of the run against a service that has not served one yet.
[…] **Worth deciding, not just patching.** The narrow fix is a per-assertion
timeout. The wider question is whether the first write of a run should be waited
for **once, somewhere shared**."

---

## The decision the issue asked for, made first

The issue names two options and declines to choose between them.

- **(a) Narrow** — a per-assertion timeout in `e2e/system-variables.spec.ts`,
  matching the precedent already set in two seed files.
- **(b) Shared** — teach `scripts/wait-for-e2e-stack.sh` (or Playwright global
  setup) to wait for a service that can actually **complete a write**.

**The ruling is (a), generalised: per-assertion budgets, expressed once as a
named shared affordance and applied at every exposed site.** (b) is rejected,
and it is rejected on evidence rather than on cost.

### Why (b) cannot work — the mechanism, established by spec 023

The parent brief asked for the actual mechanism behind the slow first write,
warning that "a fix aimed at the wrong mechanism will look right and still be
flaky". **This repository already spent a whole feature on that question**, and
its answer is the reason (b) fails.

`specs/023-first-event-cold-start/` (#1655) measured a fully-cold stack and
found, §3, *confirmed by intervention rather than inferred*:

| Round | New message type(s) introduced | arrival → effect |
|---|---|---|
| A | `FabEventIngestedV1` + `OverlayHighlightRequestedV1` | 10 827 ms |
| B | `SystemVariableValueRequestedV1` | **4 815 ms** |
| C | none | 199 ms |
| D | none | 134 ms |

Round B is the discriminator: it introduces one new message type *after* a
complete journey has already finished. It is still slow. So:

> **The cost attaches to the message type, not to the process, the connection,
> or the event.** About 5 s per new type, near-constant rather than proportional
> to work.

Spec 023 then killed, by reading or by intervention, every candidate a readiness
probe could plausibly warm (§4, §5):

| Candidate | Verdict in spec 023 |
|---|---|
| The ingest loop's poll | Ruled out by reading |
| Outbox schema build (`AutoBuildMessageStorageOnStartup`) | Ruled out — runs during host start (ADR-0088) |
| Wolverine polling intervals (5 s node/scheduled-job) | **Refuted by intervention** — set to 1 s, unchanged |
| Broker queue provisioning (`AutoProvision`) | Excluded by observation — every queue declared at startup |
| Sending-side route resolution (`RoutingFor(Type)`) | **Refuted by intervention** — priming every type at startup changed nothing, and was reverted |
| The consuming side's first receipt (handler codegen) | **Refuted by measurement** — first `receive` after a consumer restart took 0 ms |

Spec 023's own conclusion, §5: *"It remains unexplained, and no candidate is
currently standing."*

**Two consequences settle the ruling.**

1. **The cost is per message type, so one probe write warms one type.** The
   exposed e2e paths cross **five services** and at least **eight distinct
   integration message types** (§Exposure below). A readiness script that
   completes one write warms one of those eight and leaves seven. It would not
   fix the class; it would move the flake to whichever spec goes first among the
   remaining seven, while *appearing* to have fixed it — the exact failure mode
   the brief warned about.
2. **Even within `system-variables.spec.ts` the class is not one type.** Define
   publishes `SystemVariableDefinedV1`
   (`VariableDefinedDomainEventHandler.cs:36`); setting a value publishes
   `SystemVariableValueChangedV1`
   (`VariableValueChangedDomainEventHandler.cs:43`). One probe write cannot warm
   both.

### The other three costs of (b), stated so the ruling is not carried by one argument

- **It needs a token.** `scripts/wait-for-e2e-stack.sh` today probes for a `401`
  precisely because it holds no credentials. Completing a write means minting a
  Keycloak token with `sse.management` scope inside the readiness script —
  against a stack where token minting is known to be delicate (the issuer must
  match Aspire's proxied port, not the container's mapped port).
- **It creates data every run, and one kind cannot be cleaned up.** Spec 056's
  verification, §6: *"A system variable **cannot be deleted** — no control, no
  endpoint. 1618 have accumulated."* A readiness write against system-variables
  is permanent residue by construction. A camera can be retired and a layout
  archived, but only by the teardown project, which does not run on an aborted
  run.
- **Blast radius.** Every CI e2e job depends on this script (`ci.yml:255`). It is
  deliberately best-effort on its one probe today ("Gateway probe inconclusive;
  proceeding after a short buffer"). Adding an authenticated, data-creating step
  converts a class of flake into a class of hard CI failure for reasons that have
  nothing to do with the product.

### Why (a) *as literally scoped in the issue* is also not enough

Patching only `e2e/system-variables.spec.ts` leaves the identical trap in seven
other files. The issue itself says the narrow fix "leaves the same trap for the
next spec that writes first" — and the precedent it points at is already **two
copy-pasted `90_000` literals with two near-identical six-line comments** in
`seed-live-video-wall.setup.ts:44` and `seed-bound-overlay-wall.setup.ts:44`.
Adding a third, seventh and ninth copy of that literal is how a convention
becomes folklore.

So the delivered shape is: **one named budget in `e2e/support/`, carrying the
reasoning once, used at every site that can be a run's first write of its kind.**

---

## Exposure — how many specs make a first write

Counted by hand over `e2e/`, not estimated. A site qualifies when a write is
followed by an assertion on its result that carries **no explicit timeout**, and
therefore runs on the shared `expect` budget (15 s locally, 30 s in CI).

| File | First-of-kind write sites | Service | Guarded today? |
|---|---|---|---|
| `e2e/system-variables.spec.ts` | 5 (3× define, 1× set value, 1× fab row) | system-variables | **no** |
| `e2e/cameras.spec.ts` | 1 (`:31` register) | camera-catalog | **no** |
| `e2e/camera-detail.spec.ts` | 4 (`:19` register, `:59` correct address, `:103` retire, `:162` rename) | camera-catalog | **no** |
| `e2e/overlays.spec.ts` | 1 (`:31` save draft) | overlay-designer | **no** |
| `e2e/layouts.spec.ts` | 5 (`:44`, `:56`, `:58`, `:77`, publish) | camera-catalog + overlay-designer + layout-composition | **no** |
| `e2e/rules.spec.ts` | 2 (`:32` create draft, `:112` publish) | automation | **no** |
| `e2e/support/seed-published-layout.setup.ts` | 3 (`:39`, `:49`, `:54`) | camera-catalog + layout-composition | **no** |
| `e2e/kiosk-reconciliation.spec.ts` | 1 (`:74` set value) | system-variables | **no** |
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | 1 (`:318` set value) | system-variables | **no** |
| `e2e/support/seed-live-video-wall.setup.ts` | 6 (define, overlay draft, overlay publish, camera register, layout draft, layout publish) | system-variables + overlay-designer + camera-catalog + layout-composition | **1 of 6 — the define only** |
| `e2e/support/seed-bound-overlay-wall.setup.ts` | 6 (define, overlay draft, overlay publish, camera register, layout draft, layout publish) | system-variables + overlay-designer + camera-catalog + layout-composition | **1 of 6 — the define only** |

**Every file is exposed, the two seeds included.** They span **five services** —
camera-catalog, overlay-designer, layout-composition, automation,
system-variables — and at least eight distinct integration message types.

**The last two rows read "1 site each, guarded" until phase 6 (#2014 review),
and that under-count is why no task pointed at them.** Each seed makes six
distinct kinds of write across four services; only the define carried a budget.
The correction matters more here than anywhere else in the table: the `seed`
project is a *dependency* of `kiosk` and `wall`, so under
`pnpm test:e2e --project=kiosk` these are the **first writes of the entire run**
— and a seed failure fails every dependent project. (In a full CI run
`chromium` is declared first and warms those types, which is the same local/CI
asymmetry FR-008 raises.) `seed-published-layout.setup.ts` had all three of its
sites treated, which is what makes the inconsistency visible. These two rows name
the writes instead of their lines because both files moved within this feature's
own branch.

**Two facts make the exposure wider than "whichever file happens to be first".**

1. `playwright.config.ts` sets `workers: isCI ? 1 : undefined`, so a local run
   uses Playwright's default (half the logical cores) with `fullyParallel: false`
   — several *files* start at once. On a cold stack, several first writes race
   the same warm-up simultaneously, each on its own 15 s budget.
2. The cost is per message type, not per run, so a file being second does not
   make it safe. `layouts.spec.ts` writes to three services and would pay
   whichever of those types nothing else had reached yet.

---

## The finding that changes the fix: `timeout: 90_000` inside a 60 s test is a lie

The precedent this issue points at works for a reason the issue does not mention,
and copying only half of it would produce a change that reads as a fix and is not
one.

`playwright.config.ts:9` sets `timeout: 60_000` — the **per-test** timeout. An
assertion budget of 90 s inside a 60 s test is capped at 60 s minus everything
already elapsed (OIDC sign-in, navigation, dialog), so the effective budget is
roughly 45–50 s, and the `90_000` in the source is decoration.

The two seed files escape this only because they *also* raise the test timeout —
`seed-live-video-wall.setup.ts:26` and `seed-bound-overlay-wall.setup.ts:21` both
call `setTimeout(180_000)`. **None of the nine exposed files does.** Every
`test.setTimeout` in the suite today is in a kiosk, wall or teardown file.

So the fix is two numbers at each site, not one: the assertion budget **and** the
test timeout that has to contain it.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — a developer's first local run tells them about their change (Priority: P1)

A developer boots the stack and runs the e2e suite. `system-variables.spec.ts`
passes or fails according to whether the product works, not according to whether
the stack has served a write before.

**Why this priority**: It is the file the issue was raised about, it is
independently shippable, and it is where the noise is worst — five exposed sites
in one file, more than any other.

**Independent Test**: On a freshly booted stack, run
`pnpm exec playwright test e2e/system-variables.spec.ts --project=chromium
--workers=1` as the first thing that writes. All five tests pass.

**Acceptance Scenarios**:

1. **Happy path** — **Given** a stack booted less than a minute ago and no write
   yet served, **When** an operator defines a system variable, **Then** the new
   variable appears in the list and the test passes.
2. **Slow-write (the injected case)** — **Given** the define `POST` is delayed by
   20 s, **When** the operator submits the dialog, **Then** the row still appears
   and the assertion does not time out.
3. **Conflict** — **Given** a variable's value is set with a stale `If-Match`
   (ADR-0113), **When** the write is refused, **Then** the page-level alert
   renders and the test fails **fast** — the widened budget must not turn a
   genuine 409/428 into a 90 s wait for something that will never appear.
4. **Bad request** — **Given** a Boolean variable defined without its truthy/falsy
   labels, **When** Define is pressed, **Then** the schema blocks the `POST` and
   the dialog reports it, unchanged by this feature.
5. **Auth** — **Given** a session whose token has expired, **When** a write is
   attempted, **Then** the gateway answers 401 and the alert renders promptly;
   the budget applies to *waiting for an answer*, never to retrying one.

---

### User Story 2 — the next spec that writes first does not rediscover this (Priority: P2)

Every e2e site that can be a run's first write of its kind carries the same
budget, from the same named constant, with the reasoning written once.

**Why this priority**: This is the class fix, and the thing the issue actually
asked to have decided. It is P2 because US1 ships and is observable without it.

**Independent Test**: Boot a cold stack, run the **whole** chromium project
(`pnpm test:e2e --project=chromium`) as the first thing that writes, and observe
no timeout failures on write-result assertions.

**Acceptance Scenarios**:

1. **Given** the nine exposed files, **When** the sweep is complete, **Then**
   every first-of-kind write assertion listed in §Exposure carries the shared
   budget and sits in a test whose own timeout contains it.
2. **Given** the two already-guarded seeds, **When** they are re-read, **Then**
   they import the shared constant instead of restating `90_000` and its
   six-line justification.
3. **Given** a *repeat* write of a kind already exercised in the same file,
   **Then** it keeps the default budget — a warm path measured at 134–270 ms
   (spec 023 §3) does not need 90 s, and blanket-widening would hide real
   regressions.

---

### User Story 3 — CI can eventually see this class instead of absorbing it (Priority: P3, no code)

The local/CI asymmetry is recorded and raised as its own issue.

**Why this priority**: It is a real defect — `retries: 2` plus a 30 s budget mean
**CI cannot observe this class at all**, so a developer's machine is the only
place it is visible, which is exactly backwards. But **reducing CI retries is
weakening a gate, and ADR-0144 forbids this lane from doing that.** So it is a
recommendation, not work.

**Independent Test**: The issue exists, links #2014, and states the options.

**Acceptance Scenario**:

1. **Given** this feature merges, **When** the follow-up issue is read, **Then**
   it names the asymmetry (`expect.timeout` 15 s vs 30 s; `retries` 0 vs 2), says
   why retries must not simply be removed, and proposes the two candidates worth
   weighing: reporting retried tests as a visible CI signal, and making
   `expect.timeout` uniform across environments.

---

### Edge cases

- **A genuine failure now takes longer to report.** A write that will never
  succeed occupies its budget before failing. Bounded deliberately: the budget
  applies only to first-of-kind sites, and Scenario US1-3 pins that error
  *alerts* must still be asserted with the default budget so a refusal is caught
  fast.
- **A cold stack slower than 90 s.** Spec 023's worst measured cold journey was
  14 s and its per-type cost ~5 s; 90 s is roughly six times the worst figure on
  record. If it is ever exceeded the budget is not the answer — the mechanism is,
  and spec 023 has not named it.
- **An aborted run.** Unchanged by this feature: teardown does not run, residue
  stays. Named because (b) would have made it worse and (a) does not touch it.

---

## Independent end-to-end test procedure

Two tiers, because the honest red and the honest proof are not the same run.

### Tier 1 — deterministic, on demand, no cold stack required

Against a **warm** stack:

```sh
pnpm exec playwright test e2e/system-variables.spec.ts --project=chromium --workers=1 -g "slow"
```

The new test intercepts the define `POST` and holds it for 20 s before letting it
through. 20 s is chosen to sit above the local default (15 s) and below the
proposed budget (90 s), so the test's colour is decided by the budget alone and
by nothing about the machine. **Before the fix it fails; after it passes.** This
is the red phase 4a relies on.

### Tier 2 — the real condition, one-shot, corroborating

Spec 023 §5 establishes that **restarting one service does not reproduce the
cost** — only "everything cold at once" does. So Tier 2 requires a full teardown:

```sh
# 1. nothing of the stack running (a live AppHost also holds the binaries)
docker ps -a --format '{{.Names}}'   # remove run-mode containers, KEEP volumes
# 2. boot cold
dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj -- ScenarioSimulator=false
bash scripts/wait-for-e2e-stack.sh
# 3. immediately, before anything else writes
pnpm exec playwright test e2e/system-variables.spec.ts --project=chromium --workers=1
```

Recorded in the verification note whether it reproduces or not. **It is not the
gate**, because a cold stack is single-use and machine-dependent — see plan.md,
Declaration 3.

---

## Requirements

- **FR-001** — `e2e/support/` exports a single named first-write budget and the
  test timeout that contains it, with the reasoning stated once and citing spec
  023 §3.
- **FR-002** — Every first-of-kind write assertion in `e2e/system-variables.spec.ts`
  uses that budget, and each such test raises its own timeout to contain it.
- **FR-003** — The two spec-056 seeds import the constant instead of restating
  the literal — same value on the define — **and adopt it at their other five
  write sites too**, which §Exposure originally recorded as not existing.
- **FR-004** — The remaining exposed files in §Exposure adopt the same budget at
  their first-of-kind write sites (US2).
- **FR-005** — Assertions on *error* surfaces (`getByRole('alert')`,
  `toHaveCount(0)`) keep the default budget. Widening a wait for something that
  should never appear turns every failure into a stall.
- **FR-006** — Repeat writes of a kind already exercised in the same file keep the
  default budget.
- **FR-007** — A deterministic red exists that does not require a cold stack
  (Tier 1), and it must not be satisfiable by editing anything but the timeout.
- **FR-008** — A follow-up issue records the local/CI asymmetry (US3). No change
  to `retries` in this feature.

### Out of scope, each with its reason

- **Any change to `scripts/wait-for-e2e-stack.sh`.** Ruled out above on spec
  023's per-message-type evidence, not on effort.
- **Any change to `retries` or `expect.timeout` in `playwright.config.ts`.**
  Lowering retries weakens a gate (ADR-0144); raising the shared local budget to
  30 s would hide real slowness everywhere to fix it at nine known places.
- **Naming the mechanism.** Spec 023 tried, gave every candidate a verdict and
  left it open — `verification.md` SC-003 records eight candidates with none
  standing, while §4 and §5 name six of them in prose. Re-opening it is a
  measurement feature, not a flake fix, and pretending otherwise here would
  repeat spec 023's §4 warning about publishable wrong answers.
- **Deleting accumulated E2E variables.** The product has no such endpoint (spec
  056 §6). Not created by this feature either.
- **A source-scanning guard test** that asserts no exposed site still uses the
  default budget. Considered and rejected: it proves the convention was written
  down, not that it holds, and it is the shape this repo has already been burned
  by. The shared constant is the affordance instead.

---

## Locked technology choices

| Concern | Choice | Authority |
|---|---|---|
| Browser e2e | Playwright, `e2e/` at repo root, live `aspire run` stack, real Keycloak login | ADR-0108 |
| No Playwright-managed `webServer` | Aspire owns orchestration | ADR-0108 |
| Messaging beneath the write path | Wolverine + Postgres outbox, per-module queues, eager transactions | ADR-0088 |
| Migrations | dedicated `MigrationRunner` worker — completes before services serve, and is **not** the mechanism here (spec 023 §4 ruled out startup storage build) | ADR-0067 |
| Concurrency on the set-value write | `If-Match` expected version | ADR-0113 |
| Smallest change; no speculative knobs | one constant, no env var, no config surface | ADR-0036 |
| Commits | Conventional Commits, **no `Co-Authored-By`** | ADR-0030, ADR-0086 |

---

## Latency budget impact (constitution §IV)

**N/A — no leg.** Every file this feature touches is under `e2e/`. No production
code path changes, so no leg of the event → overlay budget moves.

Worth stating precisely because the *subject* is latency: the ~5 s per-message-type
cold cost spec 023 measured is a real risk to the 200 ms *event → overlay state*
leg on a full cluster start, and spec 023 recorded it as residual risk with no
mechanism named. **This feature does not reduce that risk and does not claim to.**
It stops the test suite mistaking that cost for a product failure.

---

## Assumptions

- **A1** — The observed failure is warm-up, not a rejection. Grounded in the
  issue's own observation (dialog reading "Saving…", button disabled) and
  confirmed in the UI: `SystemVariableDialog.tsx:144` renders `Saving…` only
  while `isLoading`. A 4xx would have rendered the alert instead.
- **A2** — 90 s is sufficient. Reused from the existing precedent rather than
  invented, and roughly 6× spec 023's worst recorded cold figure (14 s).
- **A3** — 20 s is the right injected delay for the Tier 1 red: above the 15 s
  local default, below the 90 s budget, and below CI's 30 s so the test proves
  the same thing in both environments. **This one is load-bearing** — a delay of
  25 s would be red locally and green in CI before the fix.

## Guesses marked

- **G1** — The exposure table's "first-of-kind" classification is derived by
  reading, not by instrumenting a cold run. A site may be warm in practice
  because another file reached its message type first; the classification is
  deliberately conservative in the widening direction, and FR-006 stops it
  becoming a blanket.
- **G2** — That the mechanism is unchanged since spec 023 (2026-08-21). Nothing
  in the intervening specs claims to have found it, and no candidate was left
  standing to have been fixed by accident.
