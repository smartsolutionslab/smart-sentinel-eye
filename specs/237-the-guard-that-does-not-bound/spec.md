# Spec 237 — The guard that does not bound

**Issue**: #2385 · **Branch**: `fix/2385-layout-teardown-resignin-bound` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `5cf0f38f` (`origin/develop`, fetched 2026-09-24)
**Context**: e2e test infrastructure only — `e2e/support/` and `scripts/`. No `src/`, `apps/`, contract,
AppHost, CI-workflow or `playwright.config.ts` change.
**Engineer**: `infra-engineer` (4b) after `test-writer` (4a) · **Reviewer**: `infra-reviewer`
**Lane**: autonomous (ADR-0144) — issue carries `agent:ready`
**Phase 4a colour**: **red** (§6) — bound-and-skip replaces guard-without-bound; the teardown's
observable worst case changes.
**ADRs**: ADR-0037 (phases), ADR-0144 (the lane; 4a/4b split; "implements decisions, does not make
them"), ADR-0036 (smallest change), ADR-0139 (§Testing — new behaviour observed red first), ADR-0109
(`e2e/support/**` is contention — §8)
**Constitution**: §IV — **N/A**. No leg of the event→overlay path is touched; this is a Playwright
teardown that runs after the suite. §Testing — behaviour-changing, red first.
**New ADR needed**: **No.** The decision (bound externally, bound-and-skip, do not touch `sign-in.ts`)
was taken and recorded by #2382 / PR #2384; this spec applies it to the second call site.

---

## 1. The premise, re-checked against `5cf0f38f`

| # | Issue claim | Status |
|---|---|---|
| 1 | `archive-e2e-layouts.teardown.ts` has a sweep with its own deadline | **Holds.** `DEADLINE_MS = 5 min` (`:38`), checked at the top of each row (`:56`). |
| 2 | …and a mid-sweep re-sign-in the deadline does not bound | **Holds exactly.** `:80` — `if (await looksSignedOut(page)) await signInAsOperator(page);` runs after the row's deadline check, with no deadline re-check and no bound on the call. |
| 3 | It is guarded by `looksSignedOut` | **Holds.** `:117-120` — a `count()` of a "Sign in" button. The guard decides *whether*; nothing decides *how long*. |
| 4 | The file's own comment records 11.5 min, nothing archived | **Holds.** `:68-72` and `:127-130`. |
| 5 | `archive-e2e-overlays.teardown.ts` has no mid-sweep re-sign-in | **Holds.** Its only `signInAsOperator` call is the initial one at `:46`, before the sweep; no `looksSignedOut`, no recovery path. |
| 6 | `recoverAndRetry` is pure and injectable | **Holds.** `e2e/support/retire-e2e-cameras.recovery.ts:19-38`; its guard is `scripts/retire-e2e-cameras-recovery.test.mjs` (2 cases, run by `pnpm test:guards`). |

**The mechanism** (from #2382, not re-derived): mid-sweep `signInAsOperator` runs on the page whose
session just lapsed, and `react-oidc-context`'s `prompt=none` silent renewal stalls before the sign-in
button appears. The `looksSignedOut` guard dodges the observed trigger only when the button is *not*
yet rendered; once it is, the call proceeds and nothing bounds a stall inside it. An in-place retry
races the same stall, so the remedy is **bound and skip**.

### 1.1 `cleanup.setTimeout` — no reduction needed

This file uses `cleanup.setTimeout(600_000)` (`:41`), **not** the 900 s #2382 reduced. #2382's reason
was that 3 attempts × 900 s = 45 min exceeded the job's budget (e2e job `timeout-minutes: 45`,
`ci.yml:604`; `retries: 2` in CI, `playwright.config.ts:15`). Here 3 × 600 s = 30 min, already inside.
After this fix the realistic worst case is the 5-min deadline + one in-flight row (archive ≤ 50 s of
explicit action timeouts, re-sign-in ≤ 30 s, second archive ≤ 50 s) + the final listing — roughly
7.5 min, inside 10. **Left unchanged**; changing it would be an unmotivated edit (ADR-0036).

## 2. Scope

**In**: bound the layouts teardown's mid-sweep recovery on both ends (spent deadline → do not start;
in flight → capped); relocate `recoverAndRetry` to a neutral module now that it has two consumers.

**Out** — do not do:
- `e2e/support/sign-in.ts` (53 call sites, contention; the issue forbids it).
- `archive-e2e-overlays.teardown.ts` (no such path — §1 row 5).
- The initial pre-sweep `signInAsOperator` in any teardown (fresh page, no lapsed session to race).
- `cleanup.setTimeout` in this file (§1.1).
- Navigation timeouts in `disposableNames` (`page.goto`) — not the defect filed; if it is one, it is
  its own issue.
- Any change to `recoverAndRetry`'s behaviour or signature.

## 3. User story

### US1 (P1) — A layout teardown that honours its own budget

As the maintainer reading CI durations, I want a stalled mid-sweep re-sign-in in the layouts teardown
to be abandoned on schedule, so that a teardown with a 5-minute deadline cannot spend its 10-minute
test ceiling (and then 2 retries) on a tidy-up, and run durations stay a truthful signal.

**Why P1 / only story**: it is the whole defect. The relocation (FR-005) is a mechanical enabler, not
a story.

**Independent test**: `pnpm test:guards` — the new layouts-recovery cases run in < 2 s each without a
browser; a naive (current-shape) implementation fails them, one by assertion and one by its 2 s
test-level timeout.

## 4. Acceptance scenarios

```gherkin
Feature: layouts teardown recovery is bounded

  Scenario: happy — a signed-in page is not re-authenticated, only retried
    Given the sweep is within its deadline
    And the page does not look signed out
    When a row's archive is refused and recovery runs
    Then sign-in is not attempted
    And the archive is retried exactly once
    And the retry's outcome is returned as the row's outcome

  Scenario: happy — a signed-out page is re-authenticated then retried
    Given the sweep is within its deadline
    And the page looks signed out
    When recovery runs and sign-in completes promptly
    Then sign-in is attempted once, then the archive is retried once

  Scenario: conflict — a stalled re-sign-in is abandoned, not awaited
    Given the sweep is within its deadline
    And the page looks signed out
    And sign-in never settles
    When recovery runs with a re-sign-in bound of T ms
    Then recovery proceeds to the retry after ~T ms, not after the test ceiling
    And the row is archived if the retry succeeds, otherwise skipped

  Scenario: bad request — a spent budget starts no new work
    Given the sweep's deadline has passed by the time a row's first attempt is refused
    When recovery would run
    Then neither the signed-out check's sign-in nor the retry is started
    And the row is recorded as skipped
    And the sweep stops and reports the remainder as out-of-time

  Scenario: auth — the guard is preserved
    Given the page looks signed out only after the first attempt's refusal
    Then the decision to sign in is still made by looksSignedOut,
      and it is made inside the bounded attempt, after the deadline check
```

## 5. Functional requirements

- **FR-001** Mid-sweep recovery in `archive-e2e-layouts.teardown.ts` MUST NOT start (no sign-in, no
  retry) when the sweep deadline has passed; the row is then skipped and the sweep marked out-of-time
  (mirrors `retire-e2e-cameras.teardown.ts:123-127`).
- **FR-002** Within budget, the re-sign-in (guard + `signInAsOperator`) MUST be raced against a fixed
  cap `RE_SIGN_IN_TIMEOUT_MS = 30_000`, after which the archive is retried once regardless.
- **FR-003** The `looksSignedOut` guard MUST be preserved: a page that does not look signed out is
  retried without re-signing in.
- **FR-004** Recovery MUST reuse `recoverAndRetry` unmodified — no second implementation of the race.
- **FR-005** `recoverAndRetry` MUST move from `e2e/support/retire-e2e-cameras.recovery.ts` to a neutral
  module `e2e/support/sweep-recovery.ts`, with its guard test moved to `scripts/sweep-recovery.test.mjs`;
  the camera teardown imports it from there. Behaviour and signature unchanged. The name must not match
  `playwright.config.ts`'s `testMatch: /.*.teardown.ts/`.
- **FR-006** `e2e/support/sign-in.ts`, `archive-e2e-overlays.teardown.ts` and this file's
  `cleanup.setTimeout(600_000)` MUST be unchanged.

## 6. Colour and what red looks like

**Behaviour-changing → red.** The change alters what the teardown does on a stalled or late recovery
(abandons instead of waiting to the ceiling). Mirrors #2382's accepted 4a shape (PR #2384): the call
site's recovery is extracted as a pure, injectable function, committed first **in its current,
unbounded shape** and unwired, with its tests — so the red is a real behavioural failure of today's
logic, not a missing import.

The seam: `e2e/support/archive-e2e-layouts.recovery.ts` exporting
`recoverLayout(now, deadline, timeoutMs, looksSignedOut, signIn, retry)`.
Red-commit body (today's logic, verbatim in meaning):
`if (await looksSignedOut()) await signIn(); return { attempted: true, result: await retry() };`

Tests in `scripts/archive-e2e-layouts-recovery.test.mjs`, each `{ timeout: 2_000 }`:

| Case | Setup | Expect | Against today's shape |
|---|---|---|---|
| R1 | deadline spent, signed out | `attempted: false`; signIn and retry not called | **red** — assertion (`attempted` is `true`) |
| R2 | deadline spent, signed in | `attempted: false`; retry not called | **red** — assertion |
| R3 | in budget, signed out, signIn never settles, `timeoutMs` 20 | returns in < 500 ms; `attempted: true`; retry called; result passed through | **red** — 2000 ms test timeout (the hang, at unit scale) |
| G1 | in budget, signed in | signIn not called; retry called once; result passed through | green — the guard, preserved (FR-003) |

Counterfactual after the fix: revert `recoverLayout`'s body to the red shape → R1–R3 red, G1 green;
restore; `git diff` empty.

## 7. Independent end-to-end test procedure

1. `pnpm test:guards` — all green; the moved camera cases (2) and the four new cases pass.
2. `pnpm typecheck:e2e` and `pnpm lint:e2e` clean (if `typecheck:e2e` fails, stash and re-run on
   clean `develop` before blaming the branch — known `@types/node` workspace issue).
3. `pnpm exec playwright test --list --project=cleanup` lists exactly the three original teardowns;
   neither `sweep-recovery.ts` nor `archive-e2e-layouts.recovery.ts` is picked up.
4. Behaviour in CI rests on subsequent runs: no layouts-teardown attempt should exceed ~8 min. Record
   this as a prediction in the PR, not as observed.

## 8. ADR-0109 contention — `e2e/support/**`

Checked 2026-09-24 against every local and `origin/*` branch. Open PRs: **#2564**
(`fix/2221-e2e-worker-contention`) touches only `e2e/kiosk-shows-a-label-over-video.spec.ts`;
**#2568** (`fix/2361-clamp01-zero-size-mismatch`) touches only `apps/shared/src/ui/composites/*`.
**No in-flight branch touches `e2e/support/`, `scripts/retire-e2e-cameras-recovery.test.mjs`,
`package.json` or `playwright.config.ts`.** No collision. Re-check before rebasing if another e2e
issue is taken while this one is parked.

`specs/186-a-trace-that-keeps-its-secrets/tasks.md` mentions the old module path; it is a historical
record and is **not** edited.

## 9. Spec number

`236` is claimed on `origin/fix/2361-clamp01-zero-size-mismatch` (PR #2568, unmerged); `232` and
`233` on other unmerged branches. **237** is free across every local and remote ref at 2026-09-24.
Re-check after any parked-PR merge before this PR merges.
