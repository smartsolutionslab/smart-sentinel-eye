# Plan 326 — The same fifteen seconds everywhere

**Spec:** [spec.md](spec.md) · **Issue:** #2077 · **Engineer:** infra-engineer (CI / test-harness
config; no product code) · **Phase 4a colour:** **red** (spec §4).

## 1. Constitution and ADR check

| Gate | Verdict |
|---|---|
| ADR-0108 — e2e against a live Aspire stack, config owns no webServer | Unchanged; only one key's value moves. |
| ADR-0144 — the lane may not weaken a gate | This **tightens** CI's default budget; no test deleted, no threshold lowered in the lenient direction, no suppression. The spec 145 guard is **amended to the new value**, not removed. |
| ADR-0139 §Testing — new behaviour starts red | Amended guard observed red before the config edit (T002). |
| §IV latency budget | N/A for product legs; `click-to-first-frame` verdict unaffected (spec §5). |
| §II, boundaries, Aspire composition | N/A — no `src/` change. |
| New ADR | **No** (spec header). |

## 2. Bounded context / layers

None. Files touched are repo-level test configuration, one guard script, and three e2e comments.

## 3. The change

### 3.1 `playwright.config.ts` (lines 11-13 only)

```ts
  // One default assertion budget in both environments (#2077): cold-load
  // slack is carried per assertion (e2e/support/cold-stack.ts), not here.
  expect: { timeout: 15_000 },
```

**Line count of the block is preserved (2 comment lines + 1 value line)** so that existing
line-number citations stay correct: `e2e/click-to-first-frame.spec.ts:64` cites
`playwright.config.ts:15` for `retries`, and the issue thread cites `:16` for `workers`. Lines 15
(`retries`) and 16 (`workers`) are **not edited**. `isCI` stays — `retries`, `workers` and
`reporter` still read it.

### 3.2 Guard — `scripts/summarise-e2e-retries.test.mjs` (spec 145's config test, lines ~450-478)

Amend in place rather than add a new file: this test is already the home of the config pins, and
moving the assertion elsewhere would read as a deleted pin.

- Replace the `expect.timeout` regex with `/expect:\s*\{\s*timeout:\s*15_000\s*\}/` and the
  message with *"expect.timeout must be 15_000 in both environments — spec 326 / #2077"*.
- Add: exactly one `^\s*expect:` line in the file (mirrors the existing single-`retries:` check,
  same reason — a nested `projects[]` `expect` would win at runtime).
- Add the scope fence: `/workers:\s*isCI\s*\?\s*1\s*:\s*undefined/` with message *"workers is out of
  scope for spec 326 (#2077) — a separate asymmetry; do not change it here"*, plus exactly one
  `^\s*workers:` line.
- Rename the test title to say what it now pins: reporter json entry, `retries` untouched,
  `expect.timeout` unified, `workers` untouched.
- `retries` pin and the reporter assertion: unchanged.

The guard reads the artefact; the runtime proof is spec §7 steps 1-2 (memory: *guards that read the
design artefact* — ask the running system once).

### 3.3 Comments that state the old figure (values unchanged)

| File | Lines | Now says | Becomes |
|---|---|---|---|
| `e2e/system-variables.spec.ts` | 7-13 | 20 s is "above the 15 s local default and below CI's 30 s" | above the shared 15 s default and below the 90 s budget, so the test says the same thing in both environments. `SLOW_WRITE_DELAY_MS` stays 20 000 — still in (15 s, 90 s), so the red/green logic of spec 066 Tier 1 holds, now in CI too. |
| `e2e/spanning-wall.spec.ts` | 51-54 | warm sites "paying the ordinary `expect.timeout` ceiling of 30 s each in CI (`playwright.config.ts:12`)" | sized when the CI default was 30 s; it is now 15 s in both environments, so the 480 s ceiling is conservative. `test.setTimeout(480_000)` unchanged. |
| `e2e/support/seed-live-video-wall.setup.ts` | 45-56 | "`expect.timeout` — 30 s in CI (`playwright.config.ts:12`)" and "the same 30 s CI worst case" | same correction; `setup.setTimeout(600_000)` unchanged. |

The stale `:12` citations (the value is on line 13) are dropped in favour of naming
`expect.timeout`, which cannot drift.

Smallest-change note: these are not drive-by edits — each states a figure this spec makes false.
No other comment, budget or test is touched.

## 4. Messaging / entities / invariants

N/A. Invariant introduced: **one top-level `expect` in `playwright.config.ts`, value `15_000`, not
environment-conditional** — enforced by the guard.

## 5. Interaction with existing mechanisms

- **Spec 066 per-assertion budgets** — explicit `timeout:` arguments override `expect.timeout`;
  82 `FIRST_WRITE_TIMEOUT_MS` sites and the other explicit numbers (60 s ×8, 30 s ×7, 90 s ×5,
  45 s ×4, 10 s ×2, 20 s, 15 s) are untouched and behave identically. The explicit `30_000` sites
  were equal to CI's default and now exceed it — they keep 30 s, which is correct: they were written
  as exceptions.
- **Spec 145 summariser** — consumes Playwright's JSON report; unaffected by the value; becomes the
  place newly visible 15-30 s sites appear.
- **`test.timeout` 60 s** — unchanged; a 15 s default budget sits further inside it than 30 s did.

## 6. Risks

| Risk | Handling |
|---|---|
| A CI default-budget site exceeds 15 s on all three attempts → hard red | PR does not merge; site reported. No budget added in this spec (spec §6). |
| New retried passes appear | Expected outcome; recorded in the verification note with names. |
| Someone later "fixes" `workers` alongside | Fence assertion fails `test:guards`. |
| Line-number citations drift | Block line count preserved; verified by T004. |
| A newly-retried-pass test is misread as first-write fragility when it is each shard's cold first sign-in | Two distinct cold-cost populations exist (spec §1.2): spec 066's explicit first-write sites (unaffected, out of scope) and the shard's own first sign-in/navigation (`e2e/support/sign-in.ts`'s final heading assertion, `e2e/support/kiosk-session.ts:26-27`), both of which run at the default budget. `scripts/wait-for-e2e-stack.sh` warms only `/` and one module, not the full module graph or Keycloak's login page, so this population is real. Phase 5 (T008) sorts any new retried pass by position — each shard's first test is this population, not a regression; a later test is the genuine first-write fragility this issue targets. |

## 7. Verification (phase 5)

Spec §7: runtime probe with `CI=true` / unset before and after (throwaway spec, never committed);
`pnpm test:guards`; `pnpm exec prettier --check` on touched files (memory: eslint clean ≠ prettier
clean); PR CI — four e2e shards and the summariser sections compared against spec §1.1's baseline.
