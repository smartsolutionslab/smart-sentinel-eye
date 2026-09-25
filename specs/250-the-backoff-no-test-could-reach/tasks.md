# Tasks 250 — The backoff no test could reach

**Spec:** `specs/250-the-backoff-no-test-could-reach/spec.md`
**Plan:** `specs/250-the-backoff-no-test-could-reach/plan.md`
**Issue:** #2449 — on Project #13 (status *In Progress*, verified via the
issue's `projectItems`). **No `item-add` needed.**

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **CHARACTERISATION (behaviour-preserving)**

Not red, and the choice is not ambiguous, so the "ambiguity resolves to red" rule
does not apply:

- **The production change preserves behaviour by construction.** Every existing
  construction path resolves to `new MqttBackoff()` — the same 1 s / 30 s at the
  same time. The covering tests are the whole existing `ScenarioSimulator.Tests`
  assembly; they are captured green **before** (T001) and must pass
  **unmodified** after (T004).
- **The new tests pin behaviour production already has.** `ResetIfHeld` with the
  doubled yardstick is correct today (EventIngestion's identical copy is tested).
  So they arrive green, and that is expected, not a shortcut. What the lane's red
  rule exists to prove — that an assertion can fail for the right reason — is
  proven instead by **counterfactuals CF1–CF4** (`plan.md` §5), each observed red
  and quoted, exactly as spec 179's T2 was.
- **If a new test is red in an ordinary run, stop.** That means the publisher's
  backoff is actually broken — a bug fix, which is a separate issue. A refactor
  that is also a bug fix is two issues (CLAUDE.md, §Testing); do not fix it here.

### Engineer roles: `test-writer` → `backend-engineer` → `test-writer`

**The engineer this needs is `backend-engineer`** (one production file plus the
C# test double), bracketed by `test-writer` for the baseline and for the new
tests. By file, not by directory — see `plan.md` §6.

### Files phase 4 may touch — exactly these

1. `src/ScenarioSimulator/Mqtt/MqttPublisher.cs` — 4b only
2. `tests/ScenarioSimulator.Tests/Fakes/FakeMqttClient.cs` — 4b only
3. `tests/ScenarioSimulator.Tests/FakeMqttClientDropHoldContractTests.cs` — 4c, new
4. `tests/ScenarioSimulator.Tests/MqttPublisherBackoffTests.cs` — 4c, new
5. `specs/250-the-backoff-no-test-could-reach/verification.md` — phase 5, new

**Forbidden:** every other file under `tests/ScenarioSimulator.Tests/` (the
characterisation set — `MqttPublisherDropAccountingTests.cs`,
`MqttPublisherProtocolPinTests.cs`, `FakeMqttClientContractTests.cs` above all);
`src/ScenarioSimulator/Mqtt/MqttBackoff.cs` and `BilletTimelineExtensions.cs`
except as transient CF patches; everything under `src/EventIngestion/` and
`tests/EventIngestion.Infrastructure.Tests/` (the reference, and PR #2585's
territory); any project reference; any options type or DI registration.

### No new ADR

Reasoned in `spec.md` §3.3: internal-only seam, test-only callers, no
configuration surface, the pattern already in production in the twin loop. The
decision-shaped alternative (configurable backoff) is explicitly out of scope.

### Stack

**Not needed.** Plain xUnit, no `AspireFixture`, no Docker. Do not stop any
running AppHost; if MSB3027 appears, report it (a stack holds service binaries,
not these test assemblies).

---

## Tasks

`[P]` = disjoint files, may run in parallel. Almost nothing is parallel: one
assembly, one slice, and 4a → 4b → 4c is a gate sequence.

### Foundational

*(None. No Shared.Kernel / Shared.Contracts / AppHost / Aspire change.)*

### Phase 4a — `test-writer` (US1)

- **[T001] [US1]** Baseline: run `dotnet test tests/ScenarioSimulator.Tests`
  **twice** on the untouched branch (the first run after machine churn can read
  like a regression). Record the pass count and quote the summary verbatim. This
  is the characterisation evidence's "before".
  *Depends on: nothing.*

### Phase 4b — `backend-engineer` (US1)

- **[T002] [US1]** `MqttPublisher.cs`: the three edits in `plan.md` §2 — field no
  longer initialised, internal constructor gains trailing `MqttBackoff? backoff =
  null` → `backoff ?? new MqttBackoff()`, one doc paragraph naming why and the
  EventIngestion precedent. Public constructor untouched.
  *Depends on: T001.*

- **[T003] [P] [US1]** `Fakes/FakeMqttClient.cs`: add
  `DropEveryConnectionImmediately` and `HoldEveryConnectionFor(TimeSpan)`, raised
  from `ConnectAsync`'s success path only, after the gate and the counter, via the
  existing `DropAsync()` (`plan.md` §3). Doc comments adapted from EventIngestion's,
  saying why the hook is `ConnectAsync` here; correct the class-level "what
  differs" paragraph. Disjoint file from T002.
  *Depends on: T001.*

- **[T004] [US1]** Characterisation "after": run the whole
  `ScenarioSimulator.Tests` assembly twice — same pass count as T001, all green —
  and show `git diff origin/develop --stat -- tests/ScenarioSimulator.Tests/`
  lists only `Fakes/FakeMqttClient.cs`. Build Release once to confirm no new
  error (S107 on the internal ctor is the one expected advisory warning; name it
  if it appears). Quote outputs.
  *Depends on: T002, T003.*

### Phase 4c — `test-writer` (US1)

- **[T005] [P] [US1]** Write `FakeMqttClientDropHoldContractTests.cs` — the three
  cases in `plan.md` §4.1. Deadline polls, not fixed yields (ADR-0150); the hold
  lower bound states its tolerance and why.
  *Depends on: T004.*

- **[T006] [P] [US1]** Write `MqttPublisherBackoffTests.cs` — harness plus the
  three cases in `plan.md` §4.2, constants carried over **with** their derivations.
  Disjoint file from T005.
  *Depends on: T004.*

- **[T007] [US1]** Run the assembly: all new tests green; quote. Record the
  observed `ConnectAttempts` for each window (add a `Console.WriteLine` as
  EventIngestion's row-4 test does, if needed to read the figure).
  *Depends on: T005, T006.*

- **[T008] [US1]** Counterfactuals CF1–CF4 (`plan.md` §5), one at a time: patch,
  run, quote the red **and** the observed count, revert, verify
  `git diff --exit-code -- src/ tests/ScenarioSimulator.Tests/Fakes/`. Confirm each
  ceiling sits clear of both the correct and the defective population; if one does
  not, re-derive it from the measurement and document the derivation — never move
  a constant just to reach green. Finish with a clean full run whose pass count
  shows the rebuild happened. **This task's revert check gates the change.**
  *Depends on: T007.*

### Phase 5 — verify

- **[T009] [US1]** `verification.md`: premise table, T001/T004 characterisation
  pair, T007 green, CF1–CF4 reds with observed counts, the revert proof, the S107
  note, and a "Findings not fixed here" section (simulator `MqttBackoff` has no
  direct tests; fake `IsConnected` is an unsynchronised property). Latency: **N/A
  — dev-only simulator (ADR-0111)**, stated.
  *Depends on: T008.*

---

## Dependency graph

```
T001 (baseline) ─┬─> T002 ─┐
                 └─> T003 ─┴─> T004 (characterisation after) ─┬─> T005 ─┐
                                                              └─> T006 ─┴─> T007 ─> T008 (CF + revert gate) ─> T009
```

## Definition of done

1. `ScenarioSimulator.Tests` green before and after the seam, same pass count, the
   pre-existing test files byte-identical to `origin/develop`.
2. Five new tests green; each shown red under at least one counterfactual, with
   the counts quoted in the PR body.
3. The PR diff contains exactly the five files listed above.
4. `EventIngestion` untouched.
5. Conventional Commits, **no `Co-Authored-By` footer** (ADR-0086); each commit
   builds on its own; PR `--base develop` (ADR-0028); body quotes T001/T004 and
   the CF reds (ADR-0139) and uses a closing keyword for #2449.
