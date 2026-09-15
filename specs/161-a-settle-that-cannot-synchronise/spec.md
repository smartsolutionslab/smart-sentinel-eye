# Spec 161 — a settle that cannot synchronise

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2392](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2392) · **Branch:** `feat/2392-a-settle-that-cannot-synchronise`
**Lane:** autonomous (ADR-0144) — `#2392` carries `agent:ready`.
**ADRs:** **ADR-0150** (waiting is a condition, not a count — the decision this
spec implements; §Decision item 2 is the deliverable), ADR-0139 (rules that fail
the build, not the review), ADR-0144 (the lane; phase 4a's two colours),
ADR-0052 (test stack), ADR-0053 (test naming), ADR-0074 (two apps + `apps/shared`),
ADR-0109 (`[P]` marking), ADR-0037 (the phased workflow).
**Constitution:** §Testing — including the new **Waiting** bullet added by PR
#2395 alongside ADR-0150; §IV (latency budget — **N/A**, see below).

**Latency budget (§IV): N/A.** No production file changes. Nothing under
`src/*/Domain`, `src/*/Application`, `apps/*/src/features` or the streaming path
is touched. No cell of the §IV table moves and no measurement is owed. Everything
this spec changes is an ESLint configuration block, four vitest test files, and a
`node --test` guard.

**No new ADR is required — see §9.** One decision **is** put back to the human at
the phase-3 gate, and it is flagged as such rather than taken (§6).

---

## 0. Dependency on PR #2395 — and why this branch is NOT stacked

ADR-0150 is **Accepted** but PR #2395 is still **open**: neither
`docs/adr/0150-waiting-is-a-condition-not-a-count.md` nor the constitution's
**Waiting** bullet is on `develop` yet (verified 2026-09-15:
`git show origin/develop:docs/adr/0150-…` → not found; the branch
`docs/adr-0150-waiting-is-a-condition` carries both, +186 lines across two files).

The dependency is **documentary only**. Nothing this spec builds imports,
references at runtime, or fails to compile without the ADR file. Per CLAUDE.md
§"Stacked PRs", a stack is for a child that "genuinely cannot build on `develop`
alone"; this one can. **This branch is cut from `develop` and its PR targets
`develop`.** Do not stack it on #2395 — the stacked-PR failure mode
(parent merges → child's PR is closed, unrecoverably) costs more than the
ordering is worth, and the two PRs can land in either order.

---

## 1. What this spec is, and what it is not

ADR-0150 decided *that* a fixed-count settle is a defect and *that* it is enforced
by lint. This spec is the **implementation** of §Decision item 2:

> A fixed-count settle may not immediately precede an assertion. Enforced by an
> ESLint `no-restricted-syntax` rule over `**/*.test.{ts,tsx}`, failing the build
> in the `frontend` bucket rather than warning.

It is **not** a re-litigation of enforce-vs-advise; that is settled. It is not a
change to any component. It does not touch `.github/workflows/ci.yml` (§3).

---

## 2. Premise verification — what is actually true today

Board issues here go stale (memory: *verify the issue premise before planning*),
and #2392 pre-dates the ADR it now implements. Every claim below was re-derived
from the working tree at `36796e84`, not read from the issue.

### 2.1 The ESLint configuration: four flat configs, no `no-restricted-syntax`

| Config | Scope (`files`) | Invoked by |
|---|---|---|
| `apps/shared/eslint.config.js` | `src/**/*.{ts,tsx}` | `eslint src --max-warnings 0` |
| `apps/management-web/eslint.config.js` | `src/**/*.{ts,tsx}` | `eslint src --max-warnings 0` |
| `apps/kiosk-web/eslint.config.js` | `src/**/*.{ts,tsx}` | `eslint src --max-warnings 0` |
| `eslint.config.mjs` (root) | `e2e/**/*.ts`, `playwright.config.ts` | `eslint e2e playwright.config.ts --max-warnings 0` |

All four are **flat config** (ESLint 9.18.0). There is **no `.eslintrc`** anywhere.
`no-restricted-syntax` is **not used anywhere in the repository** — grep over every
`*.js`, `*.mjs`, `*.json`, `*.ts` outside `node_modules` returns nothing. This is
a greenfield rule, not an extension of an existing one.

**Test files are already in scope.** Vitest suites are co-located under `src/`
(`src/ui/composites/CameraViewer.test.tsx`), so `src/**/*.{ts,tsx}` already
matches them and `eslint src` already lints them. **No scope change is needed** —
only a new, narrower config block layered on top.

### 2.2 `pnpm lint` does gate the `frontend` bucket, at `--max-warnings 0`

`.github/workflows/ci.yml`, job `frontend` ("frontend — lint + typecheck + test",
`ubuntu-latest`, `timeout-minutes: 15`), step order: Format check → **Lint
(`pnpm lint`)** → Typecheck → Test.

```
"lint": "pnpm -r --filter \"./apps/**\" lint && pnpm lint:e2e"
```

and each app's `lint` is `eslint src --max-warnings 0`. So a rule registered at
**`error`** fails the step, and a rule registered at `warn` *also* fails it
(`--max-warnings 0`). ADR-0150 asks for a build failure; registering at `error`
delivers it and states the intent honestly. **Confirmed: no CI change is
required.** The `frontend` bucket already runs the gate.

### 2.3 The real population — larger, and more delicate, than #2392 assumed

#2392 and the brief both describe the population as "five `flushConnect` call
sites in `CameraViewerCameraSwap.test.tsx`, plus six sibling suites that drain
microtasks". **The name `flushConnect` is shared by both populations**, and that
is the single most important fact in this spec.

| File | Helper body | Sound? | `await flushConnect()` sites |
|---|---|---|---|
| `apps/shared/.../CameraViewerCameraSwap.test.tsx` | `for (i<10) { await new Promise(r=>setTimeout(r,0)); for (j<5) await Promise.resolve(); }` | **NO — fixed-count macrotask** | 6 |
| `apps/shared/.../CameraViewer.test.tsx` | `for (i<12) await Promise.resolve();` | yes — microtask-only | 11 |
| `apps/shared/.../CameraViewerMedia.test.tsx` | `for (i<12) await Promise.resolve();` | yes | 6 |
| `apps/shared/.../FrameCapture.test.tsx` | `for (i<12) await Promise.resolve();` | yes | 10 |
| `apps/shared/.../OverlayEditorBackdrop.test.tsx` | `for (i<12) await Promise.resolve();` | yes | 2 |
| `apps/management-web/.../OverlayEditorDialog.test.tsx` | `for (i<12) await Promise.resolve();` | yes | 4 |
| `apps/shared/src/streaming/WhepClient.test.ts` | `flushMicrotasks`: `for (i<10) await Promise.resolve();` | yes | n/a — already named for what it does |

**One name, two opposite semantics, in the same directory.** ADR-0150's Context
records that the correct idiom was written into the very file it governs and the
next author reached for the wrong one anyway. §2.3 shows *why* that was so easy:
the wrong instrument and the right one are spelled identically, and a reader
copying `flushConnect` from a neighbouring suite has no way to know which one they
copied.

`e2e/` uses `*.spec.ts`, not `*.test.ts`. ADR-0150's stated glob
(`**/*.test.{ts,tsx}`) therefore **excludes the Playwright suites**. That is
recorded as a limit in §7, not silently widened.

---

## 3. The `i < 0` observation, re-run — and a stronger probe

ADR-0150 §Implementation Notes: *"The `i < 0` observation should be re-run before
implementation. If the five kept sites still contribute nothing, delete them and
the rule's exception population is empty."*

Run on `36796e84`, `apps/shared`, `npx vitest run src/ui/composites/CameraViewerCameraSwap.test.tsx`:

| Helper body | Runs | Result |
|---|---|---|
| `for (let i = 0; i < 10; …)` — as shipped | 1 | **7 passed (7)** |
| `for (let i = 0; i < 0; …)` — ADR-0150's probe | 3 | **7 passed (7)**, all three |
| `async function flushConnect() { /* empty */ }` — **stronger probe** | 2 | **7 passed (7)**, both |

**The result is stronger than ADR-0150 recorded.** The ADR concluded the kept
sites "contribute nothing beyond a single act flush", because `i < 0` still leaves
an `await act(async () => {})`. Removing the `act` as well — an entirely empty
function body — still passes 7 of 7. **All six surviving `flushConnect` call sites
in that file are completely inert.** Not "nearly inert"; inert.

### 3.1 The counterfactual that makes the above mean something

Green on its own proves nothing (memory: *prove a guard by counterfactual*; and
*a finished watcher is not a green run*). If no-opping the helper is always green,
the probe measures nothing. So the same probe was applied to the **sound**
helpers — the ones ADR-0150 says a blanket ban would condemn:

Replacing the microtask `flushConnect` body with an empty function in
`CameraViewer.test.tsx`, `CameraViewerMedia.test.tsx`, `FrameCapture.test.tsx`
and `OverlayEditorBackdrop.test.tsx`, then running all four:

```
 Test Files  2 failed | 2 passed (4)
      Tests  5 failed | 27 passed (32)
 FAIL  CameraViewer.test.tsx > Retries rejected connections with exponential backoff capped at fifteen seconds
 FAIL  CameraViewer.test.tsx > Suspends retries while stream health is Offline and reconnects on recovery
 FAIL  CameraViewer.test.tsx > Aborts the session and releases it on unmount
 FAIL  FrameCapture.test.tsx > Closes the session and issues the WHEP DELETE once a frame is captured
 FAIL  FrameCapture.test.tsx > Closes the session when the editor unmounts mid-capture
```

**The microtask drains are load-bearing. The macrotask settle is inert.** ADR-0150
asserted the first and suspected the second; both are now measured. The probe
discriminates, so the inert result is a finding rather than an artefact.

*(Every probe above was reverted; `git status` clean before any artefact was
written.)*

---

## 4. Hard question 1 — does the syntactic form catch the historical defect?

**Yes. All four of them, verified by running a prototype of the rule against the
pre-spec-159 file rather than by reading it.**

The four assertions that failed CI run 34956788262, at `44ee5737`
(`git show 44ee5737:apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx`):

| Failing test | settle | assertion | flagged |
|---|---|---|---|
| Never resolves to camera A when every stream read for camera B fails | `:422` | `:427` `expect(screen.queryByText('Connecting…')).toBeNull()` | **yes** |
| … when the gateway refuses camera B's stream read with 403 | `:456` | `:459` `expect(screen.queryByText('Connecting…')).toBeNull()` | **yes** |
| Reads "Stream is offline" rather than Connecting when the new camera answers Offline | `:473` | `:479` `expect(screen.getByText('Stream is offline'))` | **yes** |
| Reads Viewer error, not Idle, on a first mount whose stream read fails | `:553` | `:555` `expect(screen.getByText('Viewer error'))` | **yes** |

ESLint output over that exact historical file, with the prototype rule:

```
ScratchPre159.test.tsx
  243:13  error  B: counted timer-yield loop                  no-restricted-syntax
  355:5   error  A: settle immediately precedes an assertion  no-restricted-syntax
  417:5   error  A: settle immediately precedes an assertion  no-restricted-syntax
  427:5   error  A: settle immediately precedes an assertion  no-restricted-syntax   ← failure 1
  453:5   error  A: settle immediately precedes an assertion  no-restricted-syntax
  459:5   error  A: settle immediately precedes an assertion  no-restricted-syntax   ← failure 2
  479:5   error  A: settle immediately precedes an assertion  no-restricted-syntax   ← failure 3
  522:5   error  A: settle immediately precedes an assertion  no-restricted-syntax
  555:5   error  A: settle immediately precedes an assertion  no-restricted-syntax   ← failure 4
  573:5   error  A: settle immediately precedes an assertion  no-restricted-syntax
✖ 10 problems (10 errors, 0 warnings)
```

**The defect did not wear a different shape.** It wore exactly the shape ADR-0150
describes. One precision worth recording, because it is the kind of thing that is
assumed rather than checked: **three of the four have an intervening comment**
between the settle and the assertion. Comments are lexical trivia, not AST nodes,
so esquery's adjacent-sibling combinator (`+`) sees straight through them —
"immediately precedes" means *next sibling statement*, not *next line*. Verified,
not assumed: failures 1, 2 and 3 are all comment-separated and all flagged.

---

## 5. Hard question 2 — what identifies "a fixed-count settle helper" to a linter?

**The call site cannot answer this, and keying on the name is actively wrong here.**

An ESLint call-site selector sees `await flushConnect();`. It cannot see the
callee's body. §2.3 established that in this repository one name denotes both the
defective instrument and the sound one. So a name-keyed adjacency rule does not
discriminate — measured, over the tree as it stands today:

```
apps/management-web/.../OverlayEditorDialog.test.tsx     2 errors   ← all SOUND
apps/shared/.../CameraViewer.test.tsx                   10 errors   ← all SOUND
apps/shared/.../CameraViewerCameraSwap.test.tsx          4 errors   ← the defect
apps/shared/.../CameraViewerMedia.test.tsx               5 errors   ← all SOUND
apps/shared/.../FrameCapture.test.tsx                    5 errors   ← all SOUND
✖ 26 errors
```

**22 of 26 hits are the microtask drains that §3.1 proved load-bearing and that
ADR-0150's Alternatives section explicitly refuses to condemn.** Shipping the
name-keyed rule unchanged would do precisely what the ADR rejected the blanket ban
for doing.

### 5.1 Two selectors, and what each can and cannot see

Both were prototyped and run against the real tree; neither is a sketch.

**Selector A — the adjacency rule (ADR-0150 §2, literally).**

```
ExpressionStatement[expression.type='AwaitExpression']
                   [expression.argument.type='CallExpression']
                   [expression.argument.callee.name=/^(flushConnect|settle|pump|spin)$/]
  + ExpressionStatement:has(CallExpression[callee.name='expect'])
```

*Sees:* an awaited call to a **reserved name**, whose next sibling statement
contains `expect(...)`. `:has()` means it covers `expect(x).not.toBe(y)` and
`await expect(x).resolves…` as well as the plain form.
*Cannot see:* the callee's body; a settle inside a wrapper (`goLive()`); a settle
two statements before the assertion; a `waitUntil` whose condition is already true
(ADR-0150 §3's own named blind spot); anything in `e2e/*.spec.ts`.
*Discrimination within the defective file is real, not incidental:* it flags 4 of
the 6 sites in `CameraViewerCameraSwap.test.tsx` and correctly leaves `:541`
(followed by `setStreamAnswer(…)`, not an assertion) and `:284` (inside
`realWait`) alone.

**Selector B — the shape rule.**

```
ForStatement[test.right.type='Literal'] AwaitExpression > NewExpression[callee.name='Promise']
```

*Sees:* a counted loop (numeric-literal bound) whose body awaits a
`new Promise(…)` — i.e. a fixed number of **timer-phase** yields.
*Population over the whole tree, measured:* **exactly one — `CameraViewerCameraSwap.test.tsx:261`**, the inert helper. Zero false positives.
*Correctly does not match,* each verified against the real files:
- the microtask drains — `await Promise.resolve()` is a `CallExpression`, not a `NewExpression`;
- `waitUntil`'s deadline poll — a `WhileStatement`, not a counted `ForStatement`. **The sanctioned idiom stays legal, by construction;**
- `realWait(ms)` — one deliberate `new Promise(setTimeout)` with no loop at all;
- `WhepClient.test.ts`'s nine bare `await new Promise(r => setTimeout(r, 0))` — no loop;
- the counted `fireEvent.keyDown` loops in `OverlayEditorKeyboard.test.tsx` / `OverlayEditorUndo.test.tsx` (×10, ×160) — no `await`.

**Selector B is the discriminator that the experiment in §3 validates**: it draws
its line exactly where the measurement draws it — timer-phase counted yields
(inert, unsound) on one side, microtask counted drains (load-bearing, sound) on
the other — with no name list and nothing repo-specific.

### 5.2 Considered and rejected

- **An allowlist** ("an awaited call before an assertion must be an approved
  waiter"). Would flag `await goLive()`, `await user.click(…)`, `await act(…)` —
  hundreds of legitimate sites. Noise at that scale is how a rule gets disabled.
- **A custom ESLint rule correlating a call to its in-file declaration** (flag
  `await X()` before an assertion only when `X`'s body has the timer-loop shape).
  This is the semantically right rule and it is genuinely expressible, since every
  one of these helpers is declared in the file that calls it. It is **out of
  scope**: ADR-0150 §2 names `no-restricted-syntax`, and a bespoke rule plugin is a
  larger decision than this issue carries. Recorded as the honest upgrade path
  alongside the ADR's own CI-budget job.

---

## 6. `[DECISION REQUIRED]` — the human gate at phase 3

**ADR-0144 forbids the lane from deciding this. It is surfaced, not taken.**

Selector A alone is faithful to ADR-0150 §2's text but, once §7's cleanup has run,
**matches nothing and can only ever match a helper someone chooses to name from a
reserved list.** It is a reserved-name guard. It is evaded by naming your helper
`waitABit`.

Selector B has teeth and discriminates exactly on the measured line — but it bans
the macrotask fixed-count settle **outright**, not merely before an assertion.
ADR-0150 weighed "ban the fixed-count settle outright" and **rejected** it, on the
ground that a blanket ban "would condemn correct code" — meaning the microtask
drains. **Selector B does not match those** (§5.1, measured), so the ADR's stated
reason for rejection does not apply to it. And ADR-0150's own Implementation Notes
point directly at it: *"If the five kept sites still contribute nothing … the
rule's exception population is empty — which would make 'ban it outright' viable
after all."* §3 shows the population is not merely empty but inert.

Nonetheless, **Selector B is strictly stronger than what §2 accepted**, and
adopting it is a scope decision the lane may not make.

> **The question for the human, answerable in one line:**
> **(A)** ship Selector A only — literal §2, a reserved-name guard with no
> current population; or
> **(A+B)** ship both — A for §2's letter, B for the teeth, on the strength of
> ADR-0150's Implementation Notes and §3's measurement.
>
> **The architect's recommendation is (A+B).** A alone is close to theatre, and
> ADR-0139's whole point is rules that fail the build rather than rules that look
> as though they do. If (A+B) is judged to exceed ADR-0150 as accepted, that is a
> **BLOCK** for a one-paragraph amendment to ADR-0150's §2, not a judgement call
> for phase 4.

Everything downstream is written for **(A+B)**. Choosing (A) deletes T007, T008's
`must-flag-shape` fixture and the `TIMER_SETTLE_LOOP` selector; nothing else moves.

---

## 7. User stories

### US-1 (P1) — a fixed-count settle before an assertion fails the build

*As the engineer who will write the next viewer test, I want the wrong
synchronisation instrument to fail `pnpm lint` on my own machine, so that I do not
discover it as a CI-only red that blocks two unrelated PRs for a day.*

This is the whole slice, and it is independently shippable: one PR, observable end
to end by running `pnpm lint` (§8).

**Prerequisite cleanup, inside the same slice** — the rule cannot be turned on
over a tree that violates it 26 times:

1. **Delete the inert helper.** Remove `flushConnect` from
   `CameraViewerCameraSwap.test.tsx` and all six call sites. §3 measured them as
   contributing nothing, including the `act` flush; ADR-0150's Implementation
   Notes instruct exactly this. The suite must stay at **7 passed**.
2. **Break the name collision.** Rename the five sound microtask helpers
   `flushConnect` → **`flushMicrotasks`**, matching the name
   `WhepClient.test.ts:180` already uses for the identical body. This is the
   substantive half of the cleanup, not cosmetics: §2.3 shows the shared name is
   how the unsound instrument borrowed the sound one's credibility. Reuse of the
   existing in-repo name is deliberate — no new convention is invented.

### US-2 (P2) — the rule's blind spots are written down, not discovered

*As a reviewer, I want the rule's limits stated where I will read them, so that I
keep the obligations the rule cannot.*

ADR-0150 §3 makes this an explicit deliverable, on the record of §II drifting
twice and §IV recording a built leg as unbuilt. A comment block above the rule
names, at minimum: the wrapper blind spot; the already-true condition; the
two-statements-away gap; and `e2e/*.spec.ts` being outside the glob.

---

## 8. Acceptance scenarios (Gherkin)

### Happy path — the rule fires on the defect

```gherkin
Scenario: the historical defect fails the lint step
  Given a test file containing `await flushConnect();` whose next sibling
        statement is `expect(screen.queryByText('Connecting…')).toBeNull();`
  When `pnpm lint` runs
  Then ESLint reports `no-restricted-syntax` at the assertion statement
   And the message names the sanctioned idiom (waitFor / findBy* / waitUntil)
   And the process exits non-zero
```

```gherkin
Scenario: the defective instrument fails wherever it is written  # (A+B) only
  Given a test file containing
        `for (let i = 0; i < 10; i += 1) { await new Promise((r) => setTimeout(r, 0)); }`
  When `pnpm lint` runs
  Then ESLint reports `no-restricted-syntax` at the awaited `new Promise`
   And it does so whether or not an assertion follows
```

### Conflict — the sanctioned and the sound must NOT fire

```gherkin
Scenario: a deadline poll is not a fixed-count settle
  Given a helper that polls a condition in `while (!condition())` against a
        wall-clock deadline, awaiting `new Promise((r) => setTimeout(r, 10))`
  When `pnpm lint` runs
  Then no `no-restricted-syntax` problem is reported
```

```gherkin
Scenario: a bounded microtask drain before an assertion stays legal
  Given `await flushMicrotasks();` — whose body is
        `for (let i = 0; i < 12; i += 1) { await Promise.resolve(); }` —
        immediately followed by `expect(FakePeerConnection.instances).toHaveLength(2);`
  When `pnpm lint` runs
  Then no `no-restricted-syntax` problem is reported
  # N microtask rounds DO bound an N-deep microtask chain, with no wall-clock
  # dependence (ADR-0150, Alternatives). §3.1 measured these as load-bearing:
  # five tests go red without them.
```

```gherkin
Scenario: a counted loop that drives input is not a settle
  Given `for (let i = 0; i < 160; i += 1) { fireEvent.keyDown(label, { key: 'ArrowRight' }); }`
  When `pnpm lint` runs
  Then no problem is reported          # no `await` in the loop body
```

```gherkin
Scenario: a settle that drives, rather than synchronises, stays legal
  Given `await flushMicrotasks();` whose next sibling statement is
        `setStreamAnswer(CAM_B, () => errorResponse(403));`
  When `pnpm lint` runs
  Then no problem is reported          # driving forward is not synchronising
```

### Bad request — the rule must not match everything

```gherkin
Scenario: an ordinary awaited helper before an assertion is untouched
  Given `const pcA = await goLive();` immediately followed by
        `expect(pcA.closed).toBe(false);`
  When `pnpm lint` runs
  Then no problem is reported
  # If this fires, the rule matches everything and discriminates nothing.
```

### Auth / scope — the analogue for a lint rule is *rule scope*

```gherkin
Scenario: production sources are outside the rule
  Given a production module under `apps/shared/src/streaming/` containing a
        counted loop awaiting a timer promise
  When `pnpm lint` runs
  Then no `no-restricted-syntax` problem is reported
  # The rule governs test synchronisation, not production code. Its config
  # block is scoped to `src/**/*.test.{ts,tsx}`.
```

```gherkin
Scenario: the rule is registered in every app that has tests
  Given apps/shared, apps/management-web and apps/kiosk-web
  When `ESLint.calculateConfigForFile` is asked about a `*.test.tsx` in each
  Then `no-restricted-syntax` resolves to severity `error` in all three
  # kiosk-web has no violation today; a rule absent there is a gap that
  # only shows up as the first CI-only red in a kiosk suite.
```

---

## 9. Independent end-to-end test procedure

Runnable by a reviewer with no knowledge of the implementation, start to finish,
without reading any artefact this spec produced.

```sh
# 1. The gate is real and currently green.
pnpm lint && echo "LINT GREEN"

# 2. The rule flags the defect. Reconstruct the historical file and lint it.
git show 44ee5737:apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx \
  > apps/shared/src/ui/composites/ScratchPre159.test.tsx
cd apps/shared && npx eslint src/ui/composites/ScratchPre159.test.tsx; cd ..
# EXPECT: non-zero exit, `no-restricted-syntax` at (at least) lines 427, 459,
#         479 and 555 — the four assertions that failed CI run 34956788262.
rm apps/shared/src/ui/composites/ScratchPre159.test.tsx

# 3. The rule does NOT flag the sound population. This is the discrimination
#    proof: step 2 alone is also satisfied by a rule that flags everything.
cd apps/shared && npx eslint src/ui/composites/CameraViewer.test.tsx \
                             src/ui/composites/FrameCapture.test.tsx; cd ..
# EXPECT: exit 0, no problems. These files use flushMicrotasks before
#         assertions and must stay legal.

# 4. The sound helpers are load-bearing — so step 3 is protecting something.
#    Blank the body of flushMicrotasks in CameraViewer.test.tsx, then:
cd apps/shared && npx vitest run src/ui/composites/CameraViewer.test.tsx; cd ..
# EXPECT: 3 failed. Revert.

# 5. The guard test proves 2 and 3 without a human driving it.
pnpm test:guards
# EXPECT: the settle-rule guard passes, asserting both the must-flag and the
#         must-not-flag fixtures.

# 6. Nothing regressed.
pnpm lint && pnpm typecheck && pnpm test
```

---

## 10. Locked tech choices

| Concern | Choice | Why |
|---|---|---|
| Rule mechanism | ESLint `no-restricted-syntax`, flat config | ADR-0150 §2 names it |
| Severity | `error` | ADR-0150: "failing the build … rather than warning". `--max-warnings 0` would fail on `warn` too; `error` states the intent |
| Rule scope | a new block, `files: ['src/**/*.test.{ts,tsx}']`, in each of the three app configs | ADR-0150's glob; layered over the existing `src/**/*.{ts,tsx}` block, which already matches test files |
| Guard harness | `node --test` over `scripts/**/*.test.mjs`, driving the `ESLint` Node API | **Existing pattern** — `scripts/lint-scope.test.mjs` already does exactly this (`new ESLint({ cwd })`, `calculateConfigForFile`, `isPathIgnored`). Already wired into `pnpm test` → the `frontend` bucket |
| Sound-helper name | `flushMicrotasks` | **Existing in-repo name** for the identical body (`WhepClient.test.ts:180`). Not a new convention |
| CI | **unchanged** | `frontend` already runs `pnpm lint` and `pnpm test` (§2.2) |

---

## 11. Success criteria

- **SC-001** `pnpm lint` exits non-zero on a file containing the historical defect
  shape, and names the sanctioned idiom in the message.
- **SC-002** `pnpm lint` exits **zero** on `CameraViewer.test.tsx`,
  `CameraViewerMedia.test.tsx`, `FrameCapture.test.tsx`,
  `OverlayEditorBackdrop.test.tsx` and `OverlayEditorDialog.test.tsx` after the
  rename — the sound population stays legal.
- **SC-003** `CameraViewerCameraSwap.test.tsx` passes **7 of 7** with
  `flushConnect` and all six call sites deleted.
- **SC-004** The four suites in §3.1 pass unmodified after the rename —
  `32 passed (32)`.
- **SC-005** `no-restricted-syntax` resolves to `error` for a `*.test.tsx` in all
  three apps, asserted by `calculateConfigForFile`, not by grepping the config.
- **SC-006** The guard asserts **both** directions and fails if either the
  must-flag fixture stops being flagged **or** the must-not-flag fixture starts
  being flagged.
- **SC-007** `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm format:check` all
  green.
- **SC-008** Files changed are exactly those listed in `plan.md` §4. No production
  source, no `ci.yml`, no ADR, no constitution edit.

---

## 12. Out of scope

- The CI reduced-settle-budget job (ADR-0150, Alternatives — "worth revisiting").
- A custom ESLint rule that reads the callee's declaration (§5.2).
- Extending the rule to `e2e/*.spec.ts` (§2.3 — outside ADR-0150's glob).
- `waitUntil`-whose-condition-is-already-true (ADR-0150 §3 — explicitly a
  reviewer obligation, not a lintable one).
- Any change to `useWhepSession`'s dual-owner state machine (#2157).

## 13. Assumptions, marked

- **A-1** ADR-0150's `**/*.test.{ts,tsx}` is read as "test files in the three
  app packages", implemented as `src/**/*.test.{ts,tsx}` per package because each
  app config is rooted at its own package. `e2e/*.spec.ts` is excluded (§2.3).
- **A-2** Renaming the sound helpers is treated as behaviour-preserving. §3.1's
  counterfactual makes this checkable rather than assumed: the four suites must go
  from 5-failed-when-blanked to 32-passed-after-rename.
- **A-3** The reserved-name list in Selector A (`flushConnect|settle|pump|spin`) is
  a judgement call, not a derived set. Its only current member with any history is
  `flushConnect`. Recorded as a guess.
