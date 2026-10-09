# Spec 326 — The same fifteen seconds everywhere

**Issue:** [#2077](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2077)
— *CI cannot observe first-write fragility at all: retries absorb it and nothing reports a retried
pass*. Decision recorded on the issue (user, 2026-10-08): **option 2 — unify the mismatched
`expect.timeout` values.**
**Branch:** `fix/2077-unify-expect-timeout` (cut from `develop` @ `ba37ca5a`)
**Created:** 2026-10-08 · **Lane:** autonomous (ADR-0144)
**ADRs:** 0108 (Playwright against a live Aspire stack — the config this changes), 0144 (the lane
may not weaken a gate; this change *tightens* one), 0139 §Testing (phase 4a colour), 0036
(smallest change), 0037 (phases).
**Precedent specs:** 066 (per-assertion budgets carry the genuine exceptions), 145 (option 1 —
retried passes reported to the job summary; its config guard pins the value this spec changes).
**New ADR:** no. This implements a decision the owner made on the issue; the only derivation is
*which* of the two existing numbers both environments converge on, and §3 shows the decision's own
stated aim fixes that rather than leaving it to judgment.
**Latency budget (§IV):** N/A for every product leg — test-harness configuration only. The one §IV
measurement in the e2e suite (`click-to-first-frame.spec.ts`) is assessed in §5: unaffected.

**Spec number.** Originally filed as 317; `develop` already carried three specs numbered 317
(`317-the-outage-that-held-the-boot`, `317-the-tab-left-alone`, `317-the-unknown-a-source-holds`),
so this collided and is renumbered to **326**. `develop` now tops out at **325**
(`325-the-guess-space-the-lockout-outlasts`); 326 is confirmed free via
`git ls-tree origin/develop specs/`, with no open PR or worktree claiming it (checked 2026-10-09,
review of #2077). Re-check before opening the PR.

## 1. The premise, re-checked on this tree

| Claim in the issue | On this tree |
|---|---|
| CI `expect.timeout` 30 s, local 15 s | **Confirmed.** `playwright.config.ts:13` — `expect: { timeout: isCI ? 30_000 : 15_000 }`, `isCI = process.env.CI === 'true'` (line 6). The config is at the repo root, not under `e2e/`. |
| CI `retries: 2`, local 0 | **Confirmed**, line 15. **Not changed by this spec** (§6). |
| Nothing reports a retried pass | **Stale.** Spec 145 (PR #2352) added `scripts/summarise-e2e-retries.mjs`, run with `if: always()` after the suite (`ci.yml:818-820`). It is report-only and cannot fail the job. This spec relies on it and does not touch it. |
| First-write cost ~5 s per message type | Since carried by spec 066's per-assertion budget: `FIRST_WRITE_TIMEOUT_MS = 90_000` (`e2e/support/cold-stack.ts:28`), used at **82** assertion sites across **22** files. Explicit `timeout:` arguments override `expect.timeout` in both environments, so none of them is affected by this change. |

### 1.1 Baseline from the last three green `develop` runs

Downloaded `playwright-report-{1..4}-of-4` from runs `37765112945` (`ba37ca5a`), `37763125369`
(`4c2dfa6c`), `37744874692` (`db92e3be`) and read `test-results/e2e-report.json`:

- **Per run, summed over the four shards: 140 expected, 1 flaky, 0 unexpected** — the same in all three.
- The one flaky test is the **same in all three runs**: `overlays.spec.ts` — *operator edits a saved
  draft in place, onto the same revision* — attempt 1 fails at **its explicit 90 000 ms budget**
  (line 293, `getByText('E2E Edited')` not found), attempt 2 passes in ~4 s. Its budget is explicit,
  so this spec does not change its behaviour; it is recorded so phase 5 does not attribute it to
  this change. No issue for it exists (searched 2026-10-08) — flagged to the orchestrator, not
  filed by this spec.
- The JSON report carries no per-assertion durations, so **the baseline cannot name any assertion
  that today takes 15–30 s at the default budget in CI** — those pass on attempt 1 and are invisible
  by construction. That invisibility is the issue; phase 5 is where they become visible.

### 1.2 A second cold-cost population, distinct from spec 066's

§1's table accounts for spec 066's explicit first-write budget sites (82 of them) and finds them
unaffected — they use an explicit per-assertion `timeout:`, not `expect.timeout`. That is not the
only place a default-budget assertion meets a cold stack. Each of the four e2e shards (`ci.yml`
`e2e-shards`) boots its **own** stack from scratch, and the first sign-in/navigation in each shard
pays for Vite's on-demand module transform, Keycloak's first login, and the first JWKS fetch — at
the **default** `expect.timeout`, not an explicit override:

- `e2e/support/sign-in.ts`'s final `expect(page.getByRole('heading', { name: 'Cameras', exact: true })).toBeVisible()`.
- `e2e/support/kiosk-session.ts`'s post-sign-in expects (the *Pick a layout* heading and the first
  list item).

`scripts/wait-for-e2e-stack.sh` only warms `/` and one module before the suite starts (its own
header: "Playwright's CI retries absorb any residual warm-up") — it does not warm the full module
graph or the Keycloak login page, so these sites can still meet a cold path despite the warm-up.
This is a second, genuinely distinct population from spec 066's: spec 066 covers the *first write of
each message type*, carried everywhere by an explicit budget; this one is each shard's *first
sign-in*, running at the default budget this spec changes. Phase 5's verification (§7) sorts any
newly-retried-pass test by whether it is each shard's first test (likely this population, not a
regression) versus a later test (genuine first-write fragility at the default budget — the thing
this issue is actually about).

## 2. User story

**US1 (P1) — Local and CI agree about what "too slow" means.** As an engineer comparing a local e2e
result with a CI result, I want an assertion running at the default budget to give up at the same
point in both environments, so that a slow default-budget site that is red on my machine is not
silently green in CI — and, when CI's retries rescue it, spec 145's summary names it instead of
reporting a clean pass.

### Acceptance scenarios

1. **Happy (local unchanged)** — **Given** `CI` is unset, **When** an assertion without an explicit
   `timeout` never resolves, **Then** it fails reporting `Timeout: 15000ms` (as today).
2. **Happy (CI unified)** — **Given** `CI=true`, **When** the same assertion never resolves,
   **Then** it fails reporting `Timeout: 15000ms` (today: `30000ms`).
3. **Exceptions preserved (spec 066 convention)** — **Given** either environment, **When** an
   assertion carries an explicit `{ timeout: FIRST_WRITE_TIMEOUT_MS }` (or any explicit number),
   **Then** that number governs, unchanged.
4. **Absorption made visible** — **Given** `CI=true` and a default-budget assertion that resolves
   after 15 s but before 30 s on attempt 1, **When** a retry passes, **Then** the run is green and
   spec 145's job summary lists the test under "passed only on retry" (today: a clean pass,
   unreported).
5. **Out-of-scope fence (bad request: "fix the other asymmetry too")** — **Given** this change,
   **Then** `workers: isCI ? 1 : undefined` and `retries: isCI ? 2 : 0` are byte-identical to
   `develop`; a guard fails the build if either moves.
6. **Conflict: one key, one value** — **Given** the config, **Then** there is exactly one top-level
   `expect:` key — a second one nested in a `projects[]` entry would override the unified value per
   project and is refused by the guard.
7. **Auth** — N/A. No endpoint, scope or identity is touched.

## 3. The value, and why it is not a judgment call

**`expect: { timeout: 15_000 }` in both environments — CI comes down to local's value; local is
unchanged; no third number.**

- The decision's stated aim (issue comment, 2026-10-08) is that *"a retry absorbing first-write
  fragility stops being invisible"*. Only lowering CI does that: a CI site taking 15–30 s now fails
  attempt 1, and spec 145 already reports retried passes.
- Raising local to 30 s would make **both** environments blind to the 15–30 s band — the opposite of
  the aim. Spec 066 already recorded this verdict in its out-of-scope list: *"raising the shared
  local budget to 30 s would hide real slowness everywhere to fix it at nine known places."*
- A third number (20 s, 25 s) has no evidence behind it and would be invention.
- Lowering a CI timeout makes CI **stricter**, so ADR-0144's *weakening a gate* bar does not apply
  in this direction; it would apply to the reverse.

The comment justifying the asymmetry (`playwright.config.ts:11-12`, *"CI cold-loads a freshly booted
stack … local runs stay strict"*) is superseded: cold-load slack is what spec 066's explicit
per-assertion budgets now carry, site by site.

## 4. Behaviour classification — **behaviour-changing → red**

No application behaviour changes, but the subject of this change is the **test harness**, and its
observable output changes on purpose: a CI assertion that takes 15–30 s moves from *passed on
attempt 1* to *failed, retried, reported*. That is the point of the decision, not a side effect.

Two further reasons it cannot be characterisation:

- Spec 145's guard (`scripts/summarise-e2e-retries.test.mjs:463-467`) pins
  `expect: { timeout: isCI ? 30_000 : 15_000 }`. Characterisation requires covering tests to pass
  **unmodified**; this one must be edited, which CLAUDE.md names as evidence the behaviour moved.
- CLAUDE.md: ambiguity resolves to red.

So phase 4a: amend the guard to the new value and **observe it red** against the current config,
quoted verbatim in the PR. The scope fences in scenario 5/6 pass before and after — they are fences,
not the red test, and the PR says so.

## 5. `click-to-first-frame.spec.ts` — the §IV assertion the issue's thread flagged

The committed comment at lines 64-75 says the p95 verdict is biased green because `retries: 2`
lets the best of three runs decide. **This change does not alter that.**

- The p95 verdict (`expect(p95).toBeLessThan(P95_BUDGET_MS)`, line 515) is a value assertion; it has
  no polling timeout and `expect.timeout` does not govern it.
- The per-open frame wait is `awaitFirstFrame`'s own in-page `setTimeout` against
  `SAMPLE_BUDGET_MS` — not `expect.timeout`.
- The only default-budget assertions in the file (`expect(link).toBeVisible()` line 392,
  the *Cameras* heading line 407) sit **outside the timed window** — before the clock is armed and
  after `elapsed` is captured. In CI they now give up at 15 s rather than 30 s, which can make an
  attempt fail earlier, never change a measured figure.
- The bias comes from `retries`, which the owner did not decide on and §6 leaves alone.

**Recorded so nobody reads this spec as having addressed it.** Fixing the §IV bias needs a decision
about `retries` (or a per-test `retries: 0` for measurements, as `render-leg-check.mjs` FR-011
did for its own leg) — a separate issue, not this one.

## 6. Out of scope, each with its reason

- **`workers: isCI ? 1 : undefined` (line 16).** A second, *separate* local/CI asymmetry (raised on
  this issue; #2221, now closed, recorded a local-only failure it causes). The owner was not asked
  about it and its tradeoffs differ (CI runner resources, parallel-worker interference). **Not
  touched**, and fenced by a guard (scenario 5). The two are separable: `expect.timeout` decides
  when one assertion gives up; `workers` decides how many files run at once. Neither value reads
  the other, and the edit is one key on a different line.
- **`retries: isCI ? 2 : 0`.** Not part of the decision; spec 145 pins it and that pin stays.
- **`scripts/summarise-e2e-retries.mjs` and its `ci.yml` step.** Already delivered (option 1);
  this spec consumes its output, does not change it.
- **Adding or changing any per-assertion budget.** If CI goes hard red because a default-budget site
  exceeds 15 s on all three attempts, the PR does not merge and the site is reported. Whether it
  earns an explicit budget is spec 066's rule (*first assertion after a distinct kind of write*),
  applied by a human — giving a failing site a bigger number to reach green is ADR-0144's
  *weakening a gate*.
- **Shrinking `test.setTimeout` ceilings** in `spanning-wall.spec.ts` (480 s) and
  `seed-live-video-wall.setup.ts` (600 s), whose arithmetic assumed a 30 s warm-site worst case.
  At 15 s they are conservative; a per-test timeout is a ceiling, never a delay
  (`cold-stack.ts`). Their comments are corrected so they do not state a false figure; the values
  stay.
- **The `overlays.spec.ts` attempt-1 failure at its explicit 90 s budget** (§1.1). Pre-existing,
  unaffected, flagged.

## 7. Independent end-to-end test procedure

1. **Runtime value, before the change (counterfactual).** With a throwaway spec under `e2e/`
   asserting `expect(page.locator('#never')).toBeVisible()` against `about:blank` (no stack needed),
   run it once with `CI=true` and once with `CI` unset: expect `Timeout: 30000ms` and
   `Timeout: 15000ms`. This proves the probe can see the difference.
2. Apply the change; repeat: both report `Timeout: 15000ms`. Delete the throwaway spec; it is never
   committed.
3. `pnpm test:guards` green (the amended spec 145 guard, the fences).
4. **CI on the PR.** All four e2e shards green; read spec 145's *retried outcomes* section per shard
   and record it in the verification note against the §1.1 baseline (1 flaky: the `overlays.spec.ts`
   90 s site). Any **additional** retried-pass test at the default budget is the absorption this
   issue asked to see — record its name; it is the expected outcome, not a regression. A hard red
   (three attempts > 15 s) stops the PR per §6.

## 8. Assumptions and guesses marked

- **A1** — `CI=true` is how GitHub Actions sets the variable; `isCI` already depends on it and stays.
- **A2** — Playwright's `expect.timeout` from the root config applies to every project, since no
  `projects[]` entry sets its own `expect` today (verified by reading lines 32-109). The guard keeps
  it that way (scenario 6).
- **G1** — *Guess:* the PR's CI run surfaces zero to a few new retried passes rather than a hard
  red. Basis: local runs have used 15 s all along, and spec 066 budgeted the known first-write sites.
  Unverifiable before the run (§1.1). Phase 5 records the actual figure. **Caveat:** that basis
  covers spec 066's first-write population only; it does not cover §1.2's cold-sign-in population
  (each shard's first sign-in, at the default budget), which this guess did not separately size.
