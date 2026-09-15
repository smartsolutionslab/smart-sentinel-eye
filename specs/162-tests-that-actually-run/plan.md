# Plan — Spec 162, tests that actually run

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2397](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2397) · **Branch:** `fix/2397-tests-that-actually-run`
**Spec:** `spec.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour: BEHAVIOUR-CHANGING (new behaviour, observed RED first)** — all of it.
No behaviour-preserving work item exists in this slice, so no characterisation obligation
arises. See §6.
**Latency (§IV): N/A.** No production runtime file changes; the one production file in
scope is types-only and erases to empty JavaScript.
**New ADR required: no** (`spec.md` §5.3). One architecture question is **escalated**, not
decided — `spec.md` §4.

---

## 1. Bounded context and layers — there are none

This is a frontend-workspace and repo-tooling change. No bounded context, no aggregate, no
domain event, no persistence, no HTTP surface. The DDD sections of the usual plan template
are answered in §7 by explicit absence rather than omitted.

### Which engineer, and why it is not infra

**`frontend-engineer`.** The `infra-engineer` brief covers Aspire AppHost wiring, CI
workflows, Docker, Keycloak, the API gateway, observability and k3s/Helm. This change
touches **none** of them — in particular **`.github/workflows/ci.yml` is not modified**:
`test:guards` is already inside the root `test` script and `ci.yml:126` already runs
`pnpm test` in the `frontend` job (`spec.md` §5.3).

What the diff actually consists of is a Node script that drives **vitest's own CLI** to
learn a frontend app's test-collection set, a TypeScript conditional-type assertion over a
frontend interface, and the deletion of a vitest test file. The judgement it needs is about
vitest resolution semantics, TypeScript `keyof`/conditional-type behaviour, and the
`apps/shared` package layout. That is frontend judgement.

`scripts/lint-scope.test.mjs` — the closest precedent, a Node guard driving the ESLint API
over the frontend apps — sits in the same place for the same reason.

## 2. The change, in one sentence

Add a guard that asks each frontend app's vitest which files it collects and fails when a
test-shaped file under that app's `src/` is not among them; resolve the one file that
guard finds; and move the claim its two dead tests were gesturing at into the type system,
where it can fail.

## 3. The guard

### 3.1 Mechanism — ask vitest, per app, through its own CLI

```
npx vitest list --filesOnly --json
```

run with `cwd` set to each app, returns exactly what is needed and nothing else:

```json
[ { "file": "D:/Github/smart-sentinel-eye/apps/shared/src/api/cameras.api.test.ts" }, … ]
```

Verified at phase 1 against `apps/shared` (30 entries, matching the suites that run).
This is the **"ask the tool" half** of `lint-scope.test.mjs`'s distinction: it resolves
through the app's real, shipped config, so a file added tomorrow under a directory no
current glob happens to reach is covered without the guard being edited.

**Do not** parse `vitest.config.ts`, and do not reimplement the include globs. A guard
that reads the config is green whenever the config is right and the resolver ignores it —
memory: *guards that read the design artefact*.

**A-1 (`spec.md` §13) is load-bearing here.** `list` and `run` must resolve identically.
The engineer confirms this once per app by comparing the `--filesOnly` count against the
file count `vitest run` reports; **if they disagree for any app, stop and report** rather
than picking one.

### 3.2 The candidate set — what "should have been collected" means

For each app directory under `apps/` that has a `package.json` with a `test` script:
enumerate files under `<app>/src` recursively whose name matches

```
/\.(test|spec)\.(ts|tsx|mts|cts|js|jsx|mjs|cjs)$/
```

and assert every one of them appears in that app's collected set. `node_modules`, `dist`
and `.vite` are skipped. `e2e/`, `scripts/` and the .NET `src/` tree are never examined —
the guard's roots come from `apps/*/package.json`, so `testDir: './e2e'` (Playwright) is
out of reach by construction (`spec.md` §7.2).

**Discovering the roots rather than listing them is a requirement, not a nicety**
(`spec.md` §7.4, SC-3). A guard that hardcodes `['shared','kiosk-web','management-web']`
goes stale the day a fourth app lands, and goes stale *silently* — which is the failure
mode this whole spec exists to remove.

### 3.3 Path comparison must be normalised

Vitest returns forward slashes even on Windows; `path.join` / `readdir` give backslashes.
Compare with a normaliser (`p.split(path.sep).join('/')`) and resolve both sides to
absolute. Memory: *source-scanning tests need slash normalising* — a backslash literal is
green on Windows and red on Linux CI, and this guard runs on both.

### 3.4 The failure message names paths

The message must print the offending file's repo-relative path, because "a test file is
uncollected" is useless and "apps/shared/src/realtime/client.spec.ts is not collected by
apps/shared's vitest" is actionable. This is also what the phase-5 procedure greps for.

### 3.5 Expected behaviour per app, today

| App | `vitest.config.ts` | Include | Uncollected candidates today |
|---|---|---|---|
| `apps/shared` | yes, narrowed to `*.test.*` | `src/**/*.test.ts(x)` | **1** — `src/realtime/client.spec.ts` |
| `apps/kiosk-web` | **none** — vitest defaults | `**/*.{test,spec}.…` | 0 |
| `apps/management-web` | **none** — vitest defaults | `**/*.{test,spec}.…` | 0 |

The guard must **pass** on the latter two (`spec.md` §7.2). Flagging them would make it a
naming rule wearing a reachability rule's clothes — they collect both suffixes and
therefore have no gap.

## 4. Exact file inventory

### 4a. The guard and its evidence — 1 new file

- `scripts/realtime-collection.test.mjs` *(name at the engineer's discretion; it must end
  `.test.mjs` to be picked up by `test:guards`, and should name the property — e.g.
  `test-collection.test.mjs` — not this feature)*

Header comment follows the `lint-scope.test.mjs` convention: cite `#2397 / spec 162`,
state which assertion asks the tool and which reads an artefact, and record that the
guard's first red **was** the bug.

### 4b. Resolve the file the guard finds — 1 deletion

- `apps/shared/src/realtime/client.spec.ts` — **deleted**.

Not renamed. `spec.md` §3.5: the two assertions are unfalsifiable with respect to their
stated subject, and renaming converts an invisible non-test into a visible one. The
deletion removes provably zero coverage (§6.2), and §5 replaces the claim with one that
can fail.

### 4c. The assertion that can fail — 1 changed file (option A only)

- `apps/shared/src/realtime/index.ts` — add the exhaustiveness pin and a short comment
  recording the ADR-0076 adoption gap (US3).

Keeping the pin **in `index.ts` beside the interface** rather than in a sibling file is
deliberate: it is the thing most likely to be read by someone changing the interface, it
needs no new export entry in `package.json`, and under `spec.md` §4 option (B) it is
deleted in the same breath as the module.

The pin's shape, verified at phase 1 in both directions:

```ts
type Exact<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;

// ADR-0076 promises a v2 transport drops in behind this interface without
// changing consumers. Adding or removing a member breaks that promise, so the
// shape is pinned: tsc fails in BOTH directions, which `keyof`-typed arrays do
// not (a subset satisfies them). See spec 162 §3.3.
const realtimeClientSurfaceIsExhaustive: Exact<
  keyof RealtimeClient,
  'connect' | 'subscribe' | 'disconnect'
> = true;
```

**Two mechanical constraints on this file**, both of which the engineer must check rather
than assume:

- The binding must not be tree-shaken or flagged as unused by ESLint (`--max-warnings 0`).
  Either `export` it or satisfy whatever `no-unused-vars` configuration
  `apps/shared/eslint.config.js` applies. **Do not reach for a disable comment** — if the
  only way to keep it is a suppression, stop and report; ADR-0144 names a new suppression
  as a gate weakening.
- `index.ts` is currently **types-only and erases to nothing**. A `const` makes it emit a
  runtime binding. `apps/shared` builds with `tsc --noEmit` and nothing imports
  `./realtime`, so nothing observes this — but it is a real change to the module's nature
  and the reviewer should see it acknowledged. If it proves awkward, the alternative is a
  type-only formulation with no value binding, e.g. a `type` alias constrained by
  `extends true`, which keeps the erasure property. **Either is acceptable; the erasure
  property is not worth a suppression to preserve.**

### 4d. This spec directory — 3 files

- `specs/162-tests-that-actually-run/{spec,plan,tasks}.md`

### 4e. Explicitly NOT touched

- `.github/workflows/ci.yml` — already runs `pnpm test` (§1).
- `apps/shared/vitest.config.ts` — widening the include is argued against, `spec.md` §5.1.
- `apps/kiosk-web/` and `apps/management-web/` — no vitest config is added, `spec.md` §12.
- `src/Realtime.Abstractions/`, `SmartSentinelEye.slnx`, `apps/shared/package.json`'s
  `"./realtime"` export — all of these are `spec.md` §4 option (B) and need an ADR.
- Any `package.json` dependency list. Nothing is added.

## 5. What the guard asserts, and why each direction is load-bearing

Three assertions. The first two are the pair that makes either mean anything; the third is
the one that survives a fourth app.

1. **`every test-shaped file under an app's src/ is collected by that app's vitest`** —
   the must-flag direction. Asserted against the real tree. Its first run names
   `apps/shared/src/realtime/client.spec.ts`. *Asserting a named path, not merely
   "problems > 0", is what stops a guard that fails on everything from passing.*
2. **`the apps whose configs have no gap report nothing`** — the must-not-flag direction,
   `kiosk-web` and `management-web` (§3.5). *This is the discrimination proof. Assertion 1
   alone is satisfied by a guard that flags every file it enumerates.*
3. **`the guard's roots are derived from apps/*/package.json, not hardcoded`** — asserted
   by construction and checked by the phase-5 scratch-file step. *A guard that names three
   directories is correct today and silently incomplete tomorrow, which is the exact defect
   class under repair.*

Directions 1 and 2 are exercised by the **scratch file** in phase 4a and phase 5: creating
`apps/shared/src/realtime/scratch.spec.ts` must move the count from 1 to 2; renaming it to
`.test.ts` must move it back to 1 *and* make it appear in `vitest list`. Both halves, or
neither proves anything.

## 6. Phase 4a — one colour, and the two traps in applying it

`spec.md` §8 declares **BEHAVIOUR-CHANGING throughout; every artefact observed RED first.**
Two things about this slice make that harder to apply honestly than usual, and both are
handled by construction rather than by care.

### 6.1 The guard's first red is the bug itself

`test-writer` writes `scripts/<guard>.test.mjs` **before** `client.spec.ts` is touched,
runs `pnpm test:guards`, and returns the **verbatim** failure. The expected red is
assertion 1 naming `apps/shared/src/realtime/client.spec.ts`.

This is the cleanest red in the slice: no fixture is needed, because the defect is present
in the working tree. The engineer receives that output as its brief and **may not edit the
guard to reach green** (ADR-0144). Green is reached by 4b's deletion.

Assertions 2 and 3 will be **GREEN in that first run**, and that is expected and proves
nothing on its own — the same trap spec 161's guard header records. They become evidence
only once assertion 1 has been observed red-then-green with the same guard in place, and
once the scratch-file counterfactual has been run. **The PR body must say so**, because a
reader who sees "1 of 3 red" and stops has drawn the wrong conclusion.

### 6.2 The deletion needs a discharge that is not a characterisation

Deleting a test is the shape of a gate weakening, and ADR-0144 forbids that. The discharge
is **not** a characterisation suite — there is nothing to characterise, because the code
never ran. It is a **count comparison**, and it is mandatory:

```
apps/shared, origin/develop:  npx vitest run  →  record the passing total
apps/shared, this branch:     npx vitest run  →  MUST be the identical total
```

If the branch reports fewer, the two tests were executing after all, `spec.md` §1.1 was
wrong, and **the deletion is a block, not an adjustment.**

### 6.3 The pin is green on write, so its red must be constructed in the subject

The `Exact<>` pin compiles clean against the current shape — that is what "the current
shape is correct" means. Green therefore carries no information; a pin over
`Exact<never, never>` is green too.

The red is constructed **in `apps/shared/src/realtime/index.ts`**, twice, and both are
reverted:

| Probe | Edit to `index.ts` | Expected |
|---|---|---|
| gained member | add `reconnect(): void;` to `RealtimeClient` | `TS2322: Type 'true' is not assignable to type 'false'.` |
| lost member | delete `disconnect(): void;` | the same `TS2322` |

Both quoted verbatim in the PR. **A reviewer must confirm from the probe that `index.ts`
was the file edited** — constructing the red by editing the pin instead would reproduce
exactly the defect (`spec.md` §3.4) this spec exists to remove.

`git status` clean after each. Memory: *a restored file keeps its old timestamp* — not an
MSBuild concern here, but the probes must be reverted with `git checkout --`, not by
retyping from memory.

## 7. Messaging, entities, invariants, boundary rules

**None.** No domain event, no integration event, no aggregate, no value object, no
repository, no migration, no `Shared.Contracts` message, no cross-context reference, no
HTTP endpoint, no RabbitMQ queue, no NetArchTest rule. Listed explicitly so the reviewer
can confirm the absence is intended rather than overlooked.

The nearest thing to a boundary rule in scope is the **guard's own scope** (`spec.md` §7.4):
`apps/*` only, roots discovered rather than enumerated.

## 8. Risks

| Risk | Handling |
|---|---|
| `vitest list` resolves differently from `vitest run`, so the guard asks a different question than the gate | A-1. Engineer compares counts per app once; **disagreement is a stop-and-report**, not a choice |
| The `Exact<>` binding trips `no-unused-vars` at `--max-warnings 0` | §4c. Export it or reformulate as a type; **a suppression is a stop-and-report** (ADR-0144) |
| Adding a `const` makes a types-only module emit runtime code | §4c. Nothing imports `./realtime`, so nothing observes it; a type-only formulation is the fallback and is equally acceptable |
| Backslash/forward-slash mismatch makes the guard green on Windows and red on Linux CI | §3.3, normalise both sides. This has bitten the repo before |
| The guard hardcodes three app paths and rots silently | §3.2 / SC-3 — roots derived from `apps/*/package.json`; the reviewer checks this specifically |
| Spawning three vitest processes makes `pnpm test:guards` slow | Accepted. `list` does not execute tests; measure and report if it exceeds a few seconds, do not pre-optimise |
| `spec.md` §4 is answered **(B)** after the tasks are written | The slice is scoped so US1 is untouched by that answer; `tasks.md`'s gate note spells out the reduction |
| A scratch probe file reaches the index | SC-7. `git status --porcelain` empty is a checked criterion, not a habit |

## 9. Delivery

One PR → `develop`, cut fresh from `origin/develop` @ `7803e2d6`. **Not stacked.**

Commits: Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086), each building
on its own (ADR-0087 — rebase-merge lands them individually, so a commit that only compiles
with its successor breaks `git bisect`). Suggested split, each self-contained:

1. `test(shared): guard that every test file under src is actually collected` — 4a, red.
2. `fix(shared): delete two realtime tests that no runner has ever collected` — 4b.
3. `fix(shared): pin the RealtimeClient surface where it can actually fail` — 4c.
4. `docs(specs): spec 162 — tests that actually run` — 4d (may lead).

The PR body quotes: the guard's verbatim first red; the scratch-file both-halves output;
both `TS2322` probes; and the before/after `vitest run` counts from §6.2.
