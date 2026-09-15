# Tasks — Spec 162, tests that actually run

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2397](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2397) · **Branch:** `fix/2397-tests-that-actually-run`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour:** **BEHAVIOUR-CHANGING (RED first)** — all of T003, T004, T006, T007.
No behaviour-preserving work item exists, so **no characterisation obligation arises**;
the one deletion is discharged by a count comparison (T002), not by a characterisation
suite. See `plan.md` §6.
**Latency (§IV): N/A** — no production runtime file changes, no leg moves, no measurement owed.
**Delivery:** one PR → `develop`, cut fresh from `origin/develop` @ `7803e2d6`. **Not stacked.**
**New ADR required:** **no** — `spec.md` §5.3. One architecture question is escalated
rather than decided; see the gate note below.
**Board gate:** already satisfied — #2397 is on Project #13 (status *In Progress*).
Nothing to add; **do not** run `/speckit-taskstoissues` (feature-level tracking since
spec 028).

---

## GATE NOTE — read before T005 and T006

`spec.md` §4 carries a `[DECISION REQUIRED]`: **(A)** keep ADR-0076's `RealtimeClient`
abstraction and give it an assertion that can fail, or **(B)** retire it — delete
`apps/shared/src/realtime/index.ts`, its `"./realtime"` package export, and the empty
`src/Realtime.Abstractions/` project.

**These tasks are written for (A).** If the human answers **(B)**:

- T006 changes from "add the pin" to "delete the module, its package.json export, the
  `src/Realtime.Abstractions/` project and its `slnx` entry";
- T007 (the two `TS2322` counterfactuals) is **dropped** — there is no pin to falsify;
- US3 disappears with the module;
- **T001–T005 and T008–T009 are unaffected.** US1 and its guard ship either way. That is
  why the slice is scoped this way.

**(B) also requires an ADR** (superseding ADR-0076's client half, or recording its
abandonment), and **the lane may not write one** — so (B) is a **BLOCK on #2397**, not a
route the engineer may take on its own judgement. **The lane must not answer this itself**
(ADR-0144).

---

## `[P]` markers — ADR-0109

**T003 and T006 are the fan-out point, and they are independent of each other and of
everything else.** T003 owns `scripts/<guard>.test.mjs`; T006 owns
`apps/shared/src/realtime/index.ts`. Neither reads the other's file.

**T003 is foundational for US1 and blocks T005**: the guard's red must be observed against
a tree that still contains the defect. Deleting `client.spec.ts` first destroys the only
red this slice gets for free.

**T006 and any US3 work are one task, not two**, because both touch `index.ts`. A `[P]`
marker on two tasks editing the same file is the disjoint-file rule broken; they are merged
rather than marked.

---

## Evidence first — before anything is edited

### T001 — [Evidence] Capture the unmodified baseline

Record, from a clean tree on this branch (identical to `origin/develop` @ `7803e2d6`):

```sh
cd apps/shared && npx vitest run 2>&1 | tail -5          # the passing TOTAL — write it down
npx vitest list --filesOnly --json | grep -c '"file"'     # expect 30
npx vitest list --filesOnly --json | grep -c "client.spec" # expect 0
cd ../.. && git status --porcelain                        # expect empty
```

Also capture the equivalent `--filesOnly --json` counts for `apps/kiosk-web` and
`apps/management-web`, and for each of the three compare that count against the file count
`npx vitest run` reports.

**This comparison is assumption A-1 (`spec.md` §13) and it is load-bearing:** the guard
asks `list` a question the gate answers with `run`. **If they disagree for any app, stop
and report** — a guard that asks a different question than the gate is worse than none.

**Done when:** four numbers are written down (shared's passing total, and the three
list-vs-run agreements), and `git status` is empty.

**Depends on:** nothing.

---

### T002 — [Evidence, COUNTERFACTUAL] Prove the deletion removes zero coverage

`spec.md` §3.5 claims deleting `client.spec.ts` cannot change any result because it has
never executed. Deleting a test is the shape of the thing ADR-0144 forbids, so the claim is
**measured, not argued**.

Temporarily rename `client.spec.ts` → `client.test.ts` so vitest *does* collect it, and run:

```sh
cd apps/shared && npx vitest run 2>&1 | tail -5
```

**Expect the total to be T001's total + 2, and both new tests to PASS.** That is the
positive control: it proves the file is syntactically live and would run if collected, so
"it never ran" is a statement about the glob and not about a broken file.

`git checkout -- .` / restore the original name. Confirm `git status --porcelain` is empty
and the count is back to T001's.

**If the renamed file FAILS**, stop and report: the file is not merely uncollected, it is
also broken, and the deletion rationale needs re-deriving.
**If the count does not move by exactly 2**, stop and report: `spec.md` §1.1's measurement
was wrong.

**Done when:** both totals quoted (T001's, and T001's + 2), and the tree is clean.

**Depends on:** T001.

---

## Phase 4a RED — written by `test-writer`, before T005

### T003 — [P] [US1] The collection guard

**New file:** `scripts/<name>.test.mjs` — must end `.test.mjs` so `test:guards`
(`node --test "scripts/**/*.test.mjs"`) picks it up. Name it for the **property**
(e.g. `test-collection.test.mjs`), not for this feature.

Written and run **while `client.spec.ts` is still present**. Three assertions
(`plan.md` §5):

1. every file under an app's `src/` matching
   `/\.(test|spec)\.(ts|tsx|mts|cts|js|jsx|mjs|cjs)$/` appears in that app's collected set;
2. `apps/kiosk-web` and `apps/management-web` report **exactly zero** uncollected files;
3. the app roots are **derived** from `apps/*/package.json` having a `test` script — not
   hardcoded.

Mechanism: `npx vitest list --filesOnly --json` with `cwd` set to each app, parsed as JSON
(`plan.md` §3.1). **Do not parse `vitest.config.ts` and do not reimplement the include
globs** — memory: *guards that read the design artefact*. Normalise path separators on both
sides before comparing (`plan.md` §3.3); a backslash literal is green on Windows and red on
Linux CI. Failure messages name repo-relative paths.

Header comment in the `lint-scope.test.mjs` style: cite `#2397 / spec 162`, say which
assertion asks the tool and which reads an artefact, and carry a **first-run note** — that
assertion 1 is expected RED and assertions 2 and 3 GREEN, and that the green pair proves
nothing until assertion 1 has been red-then-green and T004 has run.

**Done when:** `pnpm test:guards` exits non-zero and the failure **names
`apps/shared/src/realtime/client.spec.ts`**. Return the output **verbatim** — it is the
engineer's brief and it is quoted in the PR body (ADR-0139).

**The engineer may not edit this guard to reach green.** Green comes from T005.

**Depends on:** T001. **Disjoint from:** T005, T006.

---

### T004 — [US1, COUNTERFACTUAL] Prove the guard discriminates

Assertion 1 going red proves the guard fires. It does **not** prove the guard fires *on
the right thing* — a guard that flags every file it enumerates would produce the same red.
Both halves, or neither means anything (spec 161's `selector: "*"` point).

**(a) must-flag.** Create `apps/shared/src/realtime/scratch.spec.ts` with one trivial
passing test. Run `pnpm test:guards`.
**Expect:** the guard names **two** files — `client.spec.ts` *and* `scratch.spec.ts`.

**(b) must-not-flag.** `mv` it to `scratch.test.ts`. Run `pnpm test:guards`.
**Expect:** back to naming **one** file. And independently confirm it is genuinely
collected, not merely tolerated:

```sh
cd apps/shared && npx vitest list --filesOnly --json | grep scratch   # expect a hit
```

**(c)** `rm` the scratch file. `git status --porcelain` **must be empty.**

**If (b) still names two files**, the guard is keyed on the filename rather than on
collection — that is a naming rule, not the reachability rule `spec.md` §5.2 specifies.
Stop and report.

**Done when:** the 2 → 1 transition is quoted, the `vitest list` hit is quoted, and the
tree is clean.

**Depends on:** T003.

---

## Phase 4b — the implementation

### T005 — [P] [US1] Delete the file no runner collects

**File:** `apps/shared/src/realtime/client.spec.ts` — **deleted**, not renamed.

`spec.md` §3.5: renaming converts an invisible non-test into a visible non-test. The claim
it was gesturing at moves to T006.

**Done when:** `pnpm test:guards` is **green**, and `cd apps/shared && npx vitest run`
reports **exactly T001's total** — unchanged, because nothing that ran was removed.

**If the total is lower than T001's**, stop: the tests were executing after all and the
deletion is a block, not an adjustment (`plan.md` §6.2).

**Depends on:** T002 (premise proven) **and** T003 (the red observed first).
**Disjoint from:** T006.

---

### T006 — [P] [US2, US3] Pin the RealtimeClient surface, and record the adoption gap

**File:** `apps/shared/src/realtime/index.ts` — **only**. Read the GATE NOTE first.

Add the exhaustiveness pin (`plan.md` §4c) — the `Exact<A, B>` conditional type and one
binding over `keyof RealtimeClient`. This is the assertion the deleted test's *name* has
been claiming to make since 2026-08-30, and unlike `(keyof RealtimeClient)[]` it fails in
**both** directions.

Add a short comment (US3) recording what phase 1 measured: nothing implements this
interface, the shipped transport is the SignalR client in `layoutHub.ts`, and
`src/Realtime.Abstractions/` is an empty project. Why the comment: the next reader
otherwise spends an afternoon working out why the "replaceable transport" has nothing
replaceable behind it. Keep it to a few lines and state facts, not plans — **the comment
must not announce a decision** (`spec.md` §4 is unanswered).

Two mechanical constraints, both to be **checked, not assumed** (`plan.md` §4c):

- The binding must survive `eslint src --max-warnings 0`. Export it, or reformulate as a
  pure type constrained by `extends true`. **If the only way to keep it is a disable
  comment, stop and report** — ADR-0144 names a new suppression as a gate weakening.
- `index.ts` is currently types-only and erases to nothing; a `const` makes it emit. No
  consumer imports `./realtime`, so nothing observes this, but say so in the PR. The
  type-only formulation is an equally acceptable fallback; **the erasure property is not
  worth a suppression.**

**Done when:** `npx tsc --noEmit` and `npx eslint src --max-warnings 0` are both green in
`apps/shared`.

**Depends on:** T001. **Disjoint from:** T003, T005.

---

### T007 — [US2, COUNTERFACTUAL] Prove the pin can fail

The pin is **green the moment it is written** — that is what "the current shape is correct"
means. Green therefore carries no information; a pin over `Exact<never, never>` is green
too. The red must be constructed, and constructed **in the subject's file**.

Both probes edit `apps/shared/src/realtime/index.ts`, **never the pin**:

| Probe | Edit | Expected |
|---|---|---|
| (a) gained member | add `reconnect(): void;` to `RealtimeClient` | `TS2322: Type 'true' is not assignable to type 'false'.` |
| (b) lost member | delete `disconnect(): void;` | the same `TS2322` |

Run `cd apps/shared && npx tsc --noEmit` after each. `git checkout -- .` after each —
restore by revert, not by retyping.

**Editing the pin instead of the interface reproduces exactly the defect this spec
exists to remove** (`spec.md` §3.4: the expectation and the subject must live in different
files). The PR must make clear from the probe which file was edited.

**Done when:** both `TS2322` outputs are quoted verbatim, and `git status --porcelain` is
empty.

**Depends on:** T006.

---

## Verification

### T008 — [US1, US2] Full gate

```sh
pnpm lint && pnpm typecheck && pnpm test
```

All green. `pnpm test` includes `test:guards`, so the new guard runs in the `frontend` CI
bucket without `ci.yml` being touched. Confirm the diff does **not** contain
`.github/workflows/ci.yml`, `apps/shared/vitest.config.ts`, or any `package.json`
dependency change.

Note the wall-clock cost of `test:guards` before and after (it now spawns three `vitest
list` processes). Report it; do not pre-optimise unless it exceeds a few seconds.

**Depends on:** T004, T005, T007.

---

### T009 — [US1, US2] The independent end-to-end procedure

Run `spec.md` §9 start to finish, as written, from a clean tree. All seven steps.

Step 5 is the one most likely to be skipped and it is what makes step 4 mean anything.
Step 6 is the one most likely to be faked, by editing the pin instead of `index.ts`.
Step 7 (the count comparison) discharges the deletion.

**Done when:** the verification note carries the output of steps 1, 2, 4, 5, 6 and 7, and
`git status --porcelain` is empty at the end.

**Depends on:** T008.

---

## Dependency graph

```
T001 ──┬──> T002 ──┐
       │           ├──> T005 [P] ──┐
       ├──> T003 [P] ──> T004 ─────┤
       │           └───────────────┤
       │                           ├──> T008 ──> T009
       └──> T006 [P] ──> T007 ─────┘
```

Fan-out: **T003 and T006 together** immediately after T001 — two disjoint files, no shared
state. T002 is cheap and also unblocked by T001. T008 is the join.

The critical path is `T001 → T003 → T004 → T008 → T009`; T006/T007 are strictly shorter and
should not gate anything.

## Definition of done

- **SC-1…SC-7** (`spec.md` §11) all observed, not inferred.
- **The guard's verbatim first red quoted**, naming `apps/shared/src/realtime/client.spec.ts`
  (T003) — plus the note that its assertions 2 and 3 were green on that run and that this
  proved nothing until T004.
- **T004's both halves quoted** — the 2 → 1 transition and the `vitest list` hit. The
  must-not-flag half is what separates this guard from one that flags everything.
- **T007's two `TS2322` outputs quoted**, with the probe applied to `index.ts`, not to the
  pin.
- **T001's and T005's `vitest run` totals quoted and identical**, and T002's `+2` control
  quoted. Together these are the whole discharge for deleting a test.
- `git status --porcelain` empty after every probe; **no scratch file reaches the index.**
- The GATE NOTE was answered by a human, not by the lane, and the answer is recorded in
  the PR.
- Commits: Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086), each
  building on its own (ADR-0087).
- PR → `develop` with `--base develop` passed explicitly (ADR-0028).
- **`.github/workflows/ci.yml` is not in the diff.**
