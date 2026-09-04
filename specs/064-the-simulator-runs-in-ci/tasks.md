# Tasks: The simulator runs in CI

**Feature Branch**: `fix/2013-the-simulator-runs-in-ci`

**Spec**: [`spec.md`](./spec.md) · **Plan**: [`plan.md`](./plan.md)

**Issues**: #2013 · **Engineer**: infra · **Phase 4a colour**: **red**

---

## Parallelism: two lanes, and the split is by file ownership

ADR-0109 marks `[P]` only where tasks own **disjoint files**. Here that gives
exactly one honest parallel pair, and it is worth taking because the two lanes
need different kinds of care:

- **The code lane** (T001 → T002 → T003 → T004) owns `src/AppHost/AppHost.cs`,
  `.github/workflows/ci.yml` and
  `tests/Integration.Tests/AppHostE2ESwitchTests.cs`. Strictly ordered: the red
  must be observed before the guard exists.
- **The comment lane** (T005) owns four `e2e/*.ts` files, one `apps/shared`
  test file, one README and one design document. It shares no file with the
  code lane.

**T005 is `[P]` with the code lane, but it must not be committed alone.** The
comments assert what T004 makes true; a commit ordering that lands them first
publishes a false statement (spec.md, User Story 1). Write in parallel, commit
after T004.

**T001 is the load-bearing artifact and blocks everything.** It is the only
moment the pre-fix behaviour can be recorded. Once the guard exists the failure
cannot be reproduced without reverting, and a red nobody watched is not a red
(ADR-0139).

---

## Task list

### T001 [US1] — the pre-fix red, observed and quoted (phase 4a)

**Do first, alone.** Add one test to
`tests/Integration.Tests/AppHostE2ESwitchTests.cs` that builds the application
model with the *run-mode* argument set the way `ci.yml` will set it, and asserts
the simulator is absent:

```
Arguments: the four Parameters:* values, plus "ScenarioSimulator=false".
           NOT "E2ETests=true" — this is the run-mode shape.
Assert:    names.ShouldNotContain("camera-sim");
           names.ShouldNotContain("scenario-simulator");
```

Name it sentence-style, matching the file (ADR-0053), e.g.
`ScenarioSimulator_argument_excludes_the_simulator_from_a_run_mode_stack`.

**Run it and watch it fail.** It must fail on
`names.ShouldNotContain("camera-sim")` — because nothing reads that
configuration key today, so the model still contains it.

**Capture the verbatim failure output.** It goes in the PR body unedited. This
is the only evidence a later reader can check that the behaviour actually
changed, and it is the whole reason this task exists separately from T002.

`CreateAsync` builds the model only — it starts no resources and costs no
containers, exactly as the file's existing class comment records. Safe to run
beside a live stack.

**Done when:** the failure is observed and its output is saved.

**Blocks:** T002, T003, T004, and the commit of T005.

---

### T002 [US1] — the invariants the change must not break (phase 4a)

Same file. Two more tests, both of which should be **green immediately** — they
are not the red, they are the fence around it:

1. `A_run_mode_stack_without_the_argument_still_composes_the_simulator` —
   Parameters only, no `ScenarioSimulator`, no `E2ETests`. Asserts `camera-sim`
   and `scenario-simulator` **are** present. Guards spec.md invariant 3: a
   developer's `aspire run` is unchanged.
2. `The_simulator_argument_leaves_the_web_apps_and_the_fixture_video_in_place` —
   with `ScenarioSimulator=false`, asserts `management-web`, `kiosk-web`,
   `kiosk-wall` and `fixture-video` **are** present.

Test 2 is the one that matters most and it encodes spec.md Finding B directly:
it is the standing proof that this change did not do what
`E2ETests=true` would have done. If someone later "simplifies" the guard by
folding the simulator back onto `E2ETests`, this test fails and names the
reason.

**A malformed-value test is optional and low value.** The fail-open behaviour is
a consequence of `bool.TryParse`, not of code being written; a test for it would
assert the BCL. The risk it covers (R3) is covered by T003 instead, at the place
the typo would actually be made.

**Do NOT touch the two existing tests.** `E2ETests_argument_excludes_the_dev_only_resources`
and `E2ETests_argument_leaves_postgres_without_a_data_volume` must pass
unmodified (spec.md invariants 1 and 2, plan.md conflicting-switch scenario). If
either needs editing, stop — the change went further than intended.

**Depends on:** T001.

---

### T003 [US1] — the workflow says what it must say (phase 4a)

A test that reads `.github/workflows/ci.yml` and asserts the e2e boot line
carries `-- ScenarioSimulator=false`.

**State its limits in the test's own comment, honestly.** It proves the workflow
file contains a string; it cannot prove the argument reached
`builder.Configuration`, and a green result here is not evidence the simulator
was absent from a run. That evidence is T006, step 3.

It earns its place anyway, and for one specific reason: the switch **fails open**
(spec.md, the malformed-value scenario), so a typo in `ci.yml` silently restores
the exact bug this feature fixes and every test still passes. R3 is a defect *in
the workflow file*, which is the one class of defect a file-reading guard can
genuinely catch.

**Normalise path separators** when locating the file — `GetRelativePath` yields
the platform separator, and a backslash literal is green on Windows and red on
Linux CI.

**Depends on:** T001. **`[P]` with T002** in principle (both are new tests in
different concerns), but they land in the same test project and probably the
same file; not worth splitting.

---

### T004 [US1] — the switch and the guard (phase 4b)

`src/AppHost/AppHost.cs`, three edits, no more:

1. After line 15 (`isE2ETests`), add `isScenarioSimulatorEnabled` in the shape
   given in `plan.md`, with its comment. Default **on**: absent or unparseable
   means a developer's stack.
2. Line 518: `if (isRunMode && !isE2ETests && isScenarioSimulatorEnabled)`.
3. The block comment at line 508 — currently *"so CI/E2E/prod never see it"*,
   the sentence #2013 is about — rewritten to say what each of the three
   conjuncts excludes. It should name #2013, because a reader who finds this
   guard will want to know why it has three conditions and not two.

`.github/workflows/ci.yml`, one edit: `-- ScenarioSimulator=false` appended to
the `dotnet run` boot line, **before** the redirection.

**Then re-run T001. It must go green**, and T002's four assertions must still
hold.

**Nothing else in `AppHost.cs` changes.** Not `fixture-video` (:146), not the
Vite apps (:435), not the data-volume or pgAdmin guards. Their `E2ETests` gate
means *"this is the integration fixture"* and is correct.

**Depends on:** T001, T002, T003.

---

### T005 [P] [US1] — six comments that stop being false (phase 4b)

Disjoint files from the code lane; write in parallel, **commit after T004**.

| File | The rewrite |
|---|---|
| `e2e/camera-detail.spec.ts:203` | Drop the simulator entirely. Its camera points at an address nothing serves — it gets no picture because it never asked for one |
| `apps/shared/src/observability/kioskLatency.test.ts:9` | Drop *"CI has no video"*. This is a vitest unit test in Node: no browser, no WebRTC, no stack. That was never the reason |
| `e2e/kiosk-shows-a-wall.spec.ts:15` | True of *this wall*, whose seeded camera has no source. Must not say CI has no video — `fixture-video` exists and spec 056 uses it |
| `e2e/support/seed-published-layout.setup.ts:8` | Empty-catalogue claim becomes true; cite the new guard |
| `e2e/layouts.spec.ts:22` | *"fresh, empty DB"* becomes true; cite the new guard |
| `src/AppHost/Resources/README.md:12` | Update the quoted guard expression |
| `docs/design/scenario-simulator-m2.md:37,53,518` | Update the quoted guard expression |

**The rule for every one of them:** say what *this* test is blind to, from its
own facts. Do not restate a stack-wide claim about video — that is how these
comments became wrong, and two of the six would become wrong again a different
way if simply flipped (spec.md, the table).

**A diff in `e2e/` or `apps/shared/` that contains anything but comment lines
means the change overreached.** No assertion, locator, timeout or import moves.

**`docs/adr/0111-*.md` and `docs/adr/0138-*.md` are NOT touched** (ADR-0144).

**Depends on:** nothing to write; T004 to commit.

---

### T006 [US1] — observe it, in the stack (phase 5)

The procedure is `spec.md` §Independent end-to-end test procedure. Three of its
steps are not optional:

- **Step 3 — read the dashboard.** `camera-sim` and `scenario-simulator` absent;
  `management-web`, `kiosk-web`, `kiosk-wall`, `fixture-video`, `mediamtx`
  present and healthy. This is the only check that catches R2, the `--`
  forwarding risk, and **a green suite is explicitly not accepted in its place**
  — if the argument were silently dropped, every test would still pass.
- **Step 4 — count the cameras.** Zero, not 23.
- **Step 5 — the full Playwright suite**, against that stack. This is the only
  thing that can contradict Finding A. `kiosk-shows-a-label-over-video.spec.ts`
  passing is what proves `fixture-video` survived.

**If a spec fails at step 5**, the finding is that *that spec* has an undeclared
dependency on the simulator's data — not that the change is wrong. Fix it by
making the spec seed its own data, as every other spec already does, and record
it in the verification note. **Do not restore the simulator to CI to make it
pass**: that is weakening a gate to reach green (ADR-0144).

**Also run step 6** — a no-argument boot, confirming the developer stack is
unchanged. Cheap, and it is the invariant most likely to be assumed rather than
checked.

Write `verification.md` beside this file. §IV is N/A and the note must say so
explicitly rather than omitting it — an absent latency section reads as an
oversight. **Do not report an e2e job duration as an improvement**; no baseline
was taken.

**Depends on:** T004, T005.

---

## Dependency graph

```
T001 (red, observed + quoted)          ← blocks everything
  ├─ T002 (invariants)
  ├─ T003 (ci.yml guard)
  └─ T004 (switch + guard + ci.yml)    ← needs T002, T003 present
        └─ T006 (phase 5)

T005 [P] (comments) ── writable any time, committed after T004 ── T006
```

**One `[P]`, and it is real:** T005 shares no file with any other task. Nothing
else parallelises — T001 must precede T004 by construction, and T002/T003 land
in the same test project.

**No foundational blocker for the orchestrator to fan out around.** Nothing in
`Shared.Kernel`, `Shared.Contracts` or a new Aspire resource; one existing
composition file gains one conjunct.

---

## Commits (ADR-0030 Conventional Commits, ADR-0086 **no `Co-Authored-By`**)

Each commit must build on its own — rebase-merge lands them individually on
`develop` (CLAUDE.md).

1. `test(apphost): the simulator argument is read, and the web apps stay` —
   T001–T003. Committed **red** for T001's test, with the failure quoted in the
   PR body.
2. `fix(apphost): the scenario simulator gets its own switch, and CI sets it` —
   T004. Turns T001 green. References #2013 with a closing keyword.
3. `docs(e2e): six comments stop claiming CI has no simulator` — T005.

Commit 1 leaves the branch with a failing test. That is the ADR-0139 shape and
it is intended; commit 2 is what a bisect would land on.

---

## Phase 3 gate

**The gate is: the feature's issue is on Project #13** (CLAUDE.md — per-task
issues stopped after spec 028; `/speckit-tasks` adds nothing to the board).

Issue #2013 carries `agent:ready`. Confirm it is on the board:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2013
```

`item-add` prints nothing on success and is idempotent. Verify with
`--limit 2000` — `item-list` defaults to 30 and a filled board looks empty
otherwise. Filter by `content.url`; the number filter returns zero.

**No per-task issues.** Six tasks against one file-scale change is exactly the
case CLAUDE.md warns adds tens of items to a board tracking work at feature
granularity.

---

## Follow-ups this feature deliberately does not open

- **The developer stack's seeding race** (spec.md scope ruling 2) — real,
  untouched, and not a defect anyone has hit. Not filed: an issue for a
  hypothetical in a dev-only harness is board noise.
- **The CI-side race** — **not filed, because this change removes the racer**
  (spec.md scope ruling 1). Recorded there rather than here so the reasoning
  travels with the decision that made it moot.
