# Plan — Spec 161, a settle that cannot synchronise

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2392](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2392) · **Branch:** `feat/2392-a-settle-that-cannot-synchronise`
**Spec:** `spec.md`
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Phase 4a colour: BEHAVIOUR-CHANGING (new behaviour, observed RED first)** for
the rule and its guard; **BEHAVIOUR-PRESERVING (characterisation, observed
GREEN)** for the two test-file cleanups. Both colours appear in one slice and
they are kept apart deliberately — see §6.
**Latency (§IV): N/A.** No production file changes.

---

## 1. Bounded context and layers — there are none

Nothing in `src/` (the .NET tree) is touched. No bounded context, aggregate,
value object, domain event, integration contract, repository, migration, DI
registration or NetArchTest rule is engaged. `Shared.Contracts` is untouched, so
the no-cross-context-reference rule has nothing to say.

This change lives in three places, none of which is a layer in the DDD sense:

| Place | What it is |
|---|---|
| `apps/*/eslint.config.js` | frontend **build-gate configuration** |
| `apps/*/src/**/*.test.tsx` | frontend **test layer** |
| `scripts/*.test.mjs` | the repo's **guard harness** (`node --test`) |

Recorded so the absence reads as deliberate rather than as an omission a reviewer
has to chase.

### Which engineer, and why it is not infra

**`frontend-engineer`.** The `infra-engineer` brief covers Aspire AppHost wiring,
CI workflows, Docker, Keycloak, the gateway, observability and k3s/Helm. This
change touches **none** of those — in particular **`.github/workflows/ci.yml` is
not modified** (`spec.md` §2.2: the `frontend` bucket already runs `pnpm lint`
and `pnpm test`). The ESLint configs are per-app frontend files sitting beside
the code they lint, and the bulk of the diff is React/RTL test code whose
correctness turns on `act`, effect flushing and microtask-vs-timer semantics.
That is frontend judgement, not infrastructure.

---

## 2. The change, in one sentence

Delete the one fixed-count **macrotask** settle in the repository (measured inert),
rename the five fixed-count **microtask** drains to the name that already exists
for them (measured load-bearing), and then turn on an ESLint rule that fails the
build if either the shape or the adjacency comes back.

Order matters and is not negotiable: **the rule cannot be registered over a tree
that violates it 26 times.**

---

## 3. The rule

### 3.1 The config block

Added to **all three** app configs — `apps/shared`, `apps/management-web`,
`apps/kiosk-web` — as a new entry appended after the existing `src/**/*.{ts,tsx}`
block, so it layers rather than replaces. Flat config merges later blocks over
earlier ones for matching files.

```js
// ADR-0150 — waiting is a condition, not a count. Constitution §Testing,
// "Waiting". A fixed count of event-loop yields is not a bound on work that
// advances in another phase of the loop: it passes on the author's machine and
// fails under CI contention. #2392 / spec 161.
//
// WHAT THIS RULE CANNOT SEE — stated, not assumed away (ADR-0150 §3):
//   * a settle hidden inside a wrapper: `await goLive()` where goLive() settles.
//   * a settle two or more statements before the assertion it guards.
//   * a `waitUntil` whose condition is already true on entry, so the loop never
//     iterates — a settle that cannot fail, wearing the sanctioned idiom's
//     clothes. Spec 159 shipped one and review caught it. No syntactic rule
//     will find it; reviewers keep that obligation explicitly.
//   * anything under `e2e/` — those are `*.spec.ts`, outside ADR-0150's glob.
// The rule is NECESSARY, NOT SUFFICIENT.
{
  files: ['src/**/*.test.{ts,tsx}'],
  rules: {
    'no-restricted-syntax': [
      'error',
      {
        // Selector B — the instrument itself. A counted loop that yields to the
        // TIMER phase. Deliberately does not match `await Promise.resolve()`:
        // N microtask rounds DO bound an N-deep microtask chain, with no
        // wall-clock dependence, and those drains are load-bearing here
        // (spec.md §3.1 — five tests go red without them).
        selector:
          "ForStatement[test.right.type='Literal'] AwaitExpression > NewExpression[callee.name='Promise']",
        message:
          'A fixed count of timer yields is not a bound on work that advances in ' +
          'another phase of the event loop (ADR-0150). Poll the condition against ' +
          'a wall-clock deadline — waitFor / findBy* / waitUntil — or drain ' +
          'microtasks with flushMicrotasks() if that is genuinely what you need.',
      },
      {
        // Selector A — ADR-0150 §2, literally: a settle may not IMMEDIATELY
        // PRECEDE an assertion. Reserved-name guard; see the limits above.
        selector:
          "ExpressionStatement[expression.type='AwaitExpression']" +
          "[expression.argument.type='CallExpression']" +
          "[expression.argument.callee.name=/^(flushConnect|settle|pump|spin)$/]" +
          " + ExpressionStatement:has(CallExpression[callee.name='expect'])",
        message:
          'A fixed-count settle may not immediately precede an assertion ' +
          '(ADR-0150 §2). Driving fakes forward is fine; synchronising an ' +
          'assertion is not. Wait for the condition: waitFor / findBy* / waitUntil.',
      },
    ],
  },
},
```

**Both selectors were prototyped and run against the real tree** (`spec.md` §4,
§5.1). They are not sketches. Measured populations on `36796e84`:

| Selector | Today's tree | Pre-159 tree (`44ee5737`) |
|---|---|---|
| B (shape) | **1** — the inert helper | 1 — the same helper |
| A (name + adjacency) | 26 — of which **22 are sound** | 9, including **all four** historical failures |

After §4's cleanup both go to **0**, which is the normal resting state of a
regression guard.

### 3.2 Two AST facts the implementation depends on

1. **`+` is the adjacent-sibling combinator over AST nodes, and comments are not
   AST nodes.** Three of the four historical failures have an intervening comment
   and are flagged anyway. "Immediately precedes" = *next sibling statement*.
   Verified, not assumed.
2. **`:has()` is supported by esquery**, which is what makes Selector A cover
   `expect(x).not.toBe(y)` and `await expect(x).resolves…` without enumerating
   matcher chains. A `[expression.callee.object.callee.name='expect']` form would
   miss `.not`.

### 3.3 The severity is `error`, not `warn`

`eslint src --max-warnings 0` would fail the build on `warn` too. `error` is
chosen because ADR-0150 asks for a build failure and the registration should say
so rather than depend on a flag in a sibling `package.json` that a future edit
could drop.

### 3.4 `kiosk-web` gets the rule despite having no violation

It has vitest suites (`apps/kiosk-web/src/features/cell/*.test.ts`) and zero
current violations. A rule present in two of three apps is a gap that surfaces as
the first CI-only red in a kiosk suite. `SC-005` asserts all three via
`calculateConfigForFile`.

---

## 4. Exact file inventory

Line numbers are against `36796e84`.

### 4a. Delete the inert macrotask settle — 1 file

| File | Edit |
|---|---|
| `apps/shared/src/ui/composites/CameraViewerCameraSwap.test.tsx` | delete `flushConnect` (`:258-268`) and its docblock (`:236-257`); delete the six call sites `:284` (inside `realWait`), `:400`, `:466`, `:515`, `:541`, `:678`; update `realWait`'s docblock, which currently explains why it *ends with* `flushConnect` |

Measured inert (`spec.md` §3): the suite passes **7 of 7** with the helper body
emptied entirely, ×2 runs. Deleting is therefore a removal of dead code, not a
behaviour change — but it is still characterised (§6).

### 4b. Break the name collision — 5 files

Rename `flushConnect` → `flushMicrotasks`. **Body unchanged.** The target name
already exists in the repo for the identical body
(`apps/shared/src/streaming/WhepClient.test.ts:180`).

| File | helper | call sites |
|---|---|---|
| `apps/shared/src/ui/composites/CameraViewer.test.tsx` | `:84` | 12 |
| `apps/shared/src/ui/composites/CameraViewerMedia.test.tsx` | `:161` | 6 |
| `apps/shared/src/ui/composites/FrameCapture.test.tsx` | `:168` | 10 |
| `apps/shared/src/ui/composites/OverlayEditorBackdrop.test.tsx` | `:150` | 2 |
| `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` | `:524` | 4 |

Each helper also gains a one-line comment stating *why* it is sound — that N
microtask rounds bound an N-deep microtask chain with no wall-clock dependence.
That sentence is the thing the shared name destroyed.

### 4c. The rule — 3 files

`apps/shared/eslint.config.js`, `apps/management-web/eslint.config.js`,
`apps/kiosk-web/eslint.config.js`.

### 4d. The guard and its fixtures — 3 new files

| File | Purpose |
|---|---|
| `scripts/settle-rule.test.mjs` | the guard (`node --test`), mirroring `scripts/lint-scope.test.mjs` |
| `scripts/fixtures/settle-rule/must-flag.fixture.txt` | the defect shapes |
| `scripts/fixtures/settle-rule/must-not-flag.fixture.txt` | the sanctioned + sound shapes |

**`.txt`, not `.tsx`, deliberately.** A `.tsx` fixture under `scripts/` would be
picked up by `pnpm format:check` (`prettier --check "{apps,e2e}/**"` — actually
outside that glob, but the risk is real for any future glob widening) and, worse,
a fixture placed under `apps/*/src/` would be linted by `pnpm lint` itself and
fail the build it is testing. The guard reads the `.txt`, writes it to a temp
path that the config's `files` glob matches, and lints it via the ESLint Node API
with `overrideConfig` off — i.e. through the **real** app config, so the guard
proves the shipped configuration and not a copy of it.

### 4e. This spec directory — 3 files

`spec.md`, `plan.md`, `tasks.md`.

**Nothing else. In particular: no `.github/workflows/ci.yml`, no production
source, no ADR, no constitution edit** (`SC-008`).

---

## 5. What the guard asserts, and why each direction is load-bearing

`scripts/settle-rule.test.mjs`, three tests:

1. **`the rule flags a fixed-count settle before an assertion`** — lint
   `must-flag.fixture.txt`. Assert the **count** and the **rule id** of the
   reported problems, and that the messages name a sanctioned idiom.
   *Asserting a count, not merely "more than zero", is what stops a rule that
   matches everything from passing.*
2. **`the rule does not flag the sanctioned or the sound idiom`** — lint
   `must-not-flag.fixture.txt`. Assert **exactly zero** problems.
   *This is the discrimination proof. Test 1 alone is satisfied by
   `selector: "*"`.*
3. **`the rule is registered at error in every app that has tests`** —
   `new ESLint({ cwd: app }).calculateConfigForFile(<a real *.test.tsx>)` for
   each of the three apps; assert `no-restricted-syntax` severity is `error` (2).
   *Asks ESLint, rather than grepping the config file. Memory: a guard that reads
   the design artefact proves the design was written down, not that it holds.*

### Fixture contents

`must-flag.fixture.txt` — each case on a known line, asserted by line number:

- the counted timer-yield loop (Selector B);
- `await flushConnect();` then `expect(…)` (Selector A, plain);
- `await flushConnect();` then a **comment**, then `expect(…)` (proves the
  comment-transparency of `+`, which three of the four historical failures
  depended on);
- `await flushConnect();` then `expect(x).not.toBe(y)` (proves `:has()` covers
  the negated chain).

`must-not-flag.fixture.txt`:

- `waitUntil`'s `while`-loop deadline poll awaiting `new Promise(setTimeout)` —
  **the sanctioned idiom**;
- `realWait` — a single un-looped `new Promise(setTimeout)`;
- `await flushMicrotasks();` immediately before `expect(…)` — **the sound
  drain**;
- `await flushMicrotasks();` immediately before a non-assertion statement;
- `const pc = await goLive();` immediately before `expect(pc.closed)…` — an
  ordinary awaited helper;
- `for (let i = 0; i < 160; i += 1) { fireEvent.keyDown(…); }` — a counted loop
  with no `await`;
- a bare `await new Promise((r) => setTimeout(r, 0));` with no loop.

---

## 6. Phase 4a — two colours in one slice, kept apart

ADR-0144: the architect declares the colour at phase 3, and ambiguity resolves to
red. This slice has both, and mixing them would let the refactor's green hide the
rule's missing red.

### 6a. BEHAVIOUR-CHANGING → **RED first**, for the rule and the guard

The new behaviour is *"`pnpm lint` fails on this shape"*. A lint rule's failing
test is a **fixture the rule must flag**.

`test-writer` writes `scripts/settle-rule.test.mjs` and both fixtures **before**
any `eslint.config.js` is edited, runs `pnpm test:guards`, and returns the
**verbatim** failure. The expected red is test 1 finding **0** problems where it
asserts N, and test 3 finding `no-restricted-syntax` **undefined** rather than
`error`. That output is the brief for the engineer and is quoted in the PR body
(ADR-0139).

**Test 2 (must-not-flag) will be GREEN in that first run** — a rule that does not
exist flags nothing. That is expected and is not evidence of anything; it only
becomes evidence once test 1 is red-then-green. The PR body must say so, because
a reader who sees "1 of 3 red" and stops has drawn the wrong conclusion.

**The engineer may not edit the fixtures or the guard to reach green.**

### 6b. BEHAVIOUR-PRESERVING → **characterisation, observed GREEN**

The 4a-deletion and the 4b-rename must not move any test's behaviour. The
covering tests already exist and are the suites themselves. They are captured
passing **before** the change and must pass **unmodified** after:

```
CameraViewerCameraSwap.test.tsx                          7 passed (7)
CameraViewer + CameraViewerMedia + FrameCapture
  + OverlayEditorBackdrop                               32 passed (32)
OverlayEditorDialog.test.tsx (management-web)            (capture the count)
```

**An assertion that has to be edited is evidence the behaviour moved: block, do
not adjust.**

Green alone cannot discriminate here — it is exactly the trap ADR-0150's own
`i < 0` note describes — so the characterisation carries a mandatory
**counterfactual** (T002). Without it, "the rename is safe" and "these tests
assert nothing" produce identical evidence.

---

## 7. Messaging, entities, invariants, boundary rules

**None.** No domain event, no integration event, no aggregate, no value object,
no `Result<T, Error>`, no `Option<T>`, no `Ensure.That`, no handler
deconstruction, no idempotency key, no EF migration, no Wolverine queue, no
NetArchTest rule. Listed explicitly so the reviewer can confirm the absence is
intended rather than overlooked.

The only invariant this change introduces is the lint rule itself, and it is
stated in §3.1 together with what it cannot see.

---

## 8. Risks

| Risk | Handling |
|---|---|
| **The wrong rule gets built** — Selector A alone is a reserved-name guard with an empty population | `spec.md` §6 puts the A-vs-A+B choice to the human at the phase-3 gate. The lane may not decide it (ADR-0144) |
| Deleting `flushConnect` from the swap file breaks it under CI load, where the local probe cannot see | The six sites are inert *including the `act` flush* — there is no timing left to lose. The remaining waits in that file are `waitUntil` deadline polls, which are load-independent by construction |
| The rename is a 34-call-site sed and silently changes something | T002's counterfactual + unmodified characterisation (§6b). A rename is a compile-checked operation; `pnpm typecheck` catches a missed site |
| A false positive blocks an unrelated PR | Measured: Selector B's population over the whole tree is 1, Selector A's is 0 after cleanup. ADR-0150: "a false positive is cheap; a false negative costs a day" |
| The fixture files get linted by the rule they test, or formatted | `.txt` under `scripts/fixtures/`, outside both `eslint src` and the `format:check` glob (§4d) |
| PR #2395 does not merge, leaving this PR citing an ADR not on `develop` | Documentary only (`spec.md` §0). Both PRs target `develop` and land in any order. **Do not stack** |

---

## 9. Delivery

**One PR**, `feat/2392-a-settle-that-cannot-synchronise` → **`develop`**
(`gh pr create --base develop`). Every commit must build on its own — rebase-merge
lands them individually (ADR-0087). The natural commit sequence is T003 / T004 /
T005 / T006+T007, each self-contained; the rule commit must come **after** both
cleanups or `pnpm lint` is red at that commit.

Conventional Commits (ADR-0030), **no `Co-Authored-By`** (ADR-0086).
