# Tasks 326 — The same fifteen seconds everywhere

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Issue:** #2077 (feature-level
issue; already on Project #13 — no per-task issues, per CLAUDE.md Phase 3)
**Engineer:** infra-engineer · **Phase 4a colour:** **red** (spec §4)

Format: `[ID] [P?] [Story] description`. Single story (US1). Strictly sequential except where
marked: every task touches `playwright.config.ts` or depends on its value, so there is nothing to
fan out. No foundational/AppHost work.

## Phase 4a — test-writer (red)

- [ ] **T001 [US1] Runtime counterfactual, before.** Throwaway `e2e/zz-expect-probe.spec.ts`
  (never committed): `await page.goto('about:blank'); await expect(page.locator('#never')).toBeVisible();`.
  Run `pnpm exec playwright test e2e/zz-expect-probe.spec.ts --project=chromium --retries=0` with
  `CI=true` and with `CI` unset. Record verbatim `Timeout: 30000ms` / `Timeout: 15000ms`. Note:
  `CI=true` also selects the json reporter writing `test-results/e2e-report.json` — harmless,
  delete afterwards. Keep the probe file locally for T005; delete it before any commit.
- [ ] **T002 [US1] Amend the spec 145 config guard** in `scripts/summarise-e2e-retries.test.mjs`
  per plan §3.2: `expect.timeout` pinned to `15_000` unconditional; exactly one `expect:` key;
  `workers: isCI ? 1 : undefined` fence plus exactly one `workers:` key; test title updated;
  `retries` and reporter assertions untouched. Run `node --test scripts/summarise-e2e-retries.test.mjs`
  and capture **verbatim** output: the `expect.timeout` assertion **red**, the fences green (say so
  in the PR — fences are not the red test).

## Phase 4b — infra-engineer (may not edit T002's assertions)

- [ ] **T003 [US1]** `playwright.config.ts` lines 11-13 → plan §3.1 exactly (2 comment lines +
  `expect: { timeout: 15_000 },`). Do **not** touch lines 15 (`retries`), 16 (`workers`), 18-20
  (`reporter`) or `isCI`. Depends on T002.
- [ ] **T004 [US1]** Confirm `git diff develop -- playwright.config.ts` changes only lines 11-13 and
  that `retries:` is still on line 15 and `workers:` on line 16 (the citation in
  `click-to-first-frame.spec.ts:64`). Depends on T003.
- [ ] **T005 [US1] Runtime counterfactual, after.** Re-run T001's probe both ways: both
  `Timeout: 15000ms`. Delete the probe and any `test-results/` it produced. Depends on T003.
- [ ] **T006 [P] [US1]** Comment corrections per plan §3.3 in `e2e/system-variables.spec.ts`,
  `e2e/spanning-wall.spec.ts`, `e2e/support/seed-live-video-wall.setup.ts`. **No value changes**
  (`SLOW_WRITE_DELAY_MS`, `test.setTimeout(480_000)`, `setup.setTimeout(600_000)` stay). `[P]` with
  T003-T005 — disjoint files. Prove comment-only by hashing the comment-stripped code before and
  after (memory: *characterise a comment-only change by hashing the code*).
- [ ] **T007 [US1]** `pnpm test:guards` green; `pnpm exec prettier --check` on every touched file;
  `pnpm exec playwright test --list` succeeds (config still loads). Depends on T003, T006.

## Phase 5 — verify

- [ ] **T008 [US1]** PR CI: four e2e shards. Record per shard spec 145's *retried outcomes* section
  against the spec §1.1 baseline (1 flaky — `overlays.spec.ts` at its explicit 90 s budget). List
  any additional retried-pass test by name as newly visible absorption. Hard red → stop per spec §6;
  do not add budgets. Write the verification note on the PR, including T001/T005 verbatim output and
  the spec §5 statement that `click-to-first-frame`'s §IV verdict is unaffected (its bias is
  `retries`, out of scope).

## Commit plan (ADR-0030, each commit builds on its own)

1. `docs(2077): spec 317, one expect.timeout for local and CI` — `specs/317-*/` (renumbered to
   `specs/326-*/` by the review commit; the commit message itself is not amended — see CLAUDE.md
   on not rewriting pushed, reviewed commits).
2. `fix(2077): unify expect.timeout at 15 s in local and CI` — T002 + T003 **in one commit**.
   Rebase-merge lands commits individually (ADR-0087), so a guard-only commit would put a red
   `test:guards` on `develop`; the red is evidenced by T002's verbatim output in the PR body instead.
3. `docs(2077): correct comments that cite the 30 s CI default` — T006.

## Dependencies

T001 → T002 → T003 → {T004, T005}; T006 ∥ T003-T005; {T003, T006} → T007 → T008.
