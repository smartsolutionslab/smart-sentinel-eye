# Tasks: Spec 288, the helpers each spec copies

**Spec:** `spec.md` · **Plan:** `plan.md` · **ADR:** ADR-0162 · **Issue:** #2661 · **Lane:** supervised

**Phase 4a colour, declared here (ADR-0144):**

- **US1, US2, US3: CHARACTERISATION (behaviour-preserving).** The evidence is plan §4.4, not "tests unmodified", because the specs are the edited artefact (ADR-0162 §6):
  - an empty `--list` diff;
  - the affected projects green before and after;
  - an unchanged ordered assertion inventory (two baselines, noise named);
  - a deliberate-break red for every arrival-asserting helper.
- **T001-T004 (inventory tooling): RED.** The diff logic is new behaviour and a guard. It is committed test-first, and two red runs are quoted in PR 1.

**Engineers:**

- `test-writer`: T001, and the baseline captures T101, T201, T302.
- `frontend-engineer`: the extractions.
- `frontend-reviewer`: phase 6.

No security surface: sign-in still uses the same seeded realm users. The credentials move from five spec files to one module. `security-reviewer` is optional.

**Board.** Feature issue #2661 (label `tech-debt`), to be added to Project #13 by hand (CLAUDE.md Phase 3 gate). No per-task issues.

**Contention (ADR-0109).**

- `e2e/support/*` is a contention file set.
- PR 1 owns `wall-session.ts`, `kiosk-session.ts` and `scripts/e2e-assertion-inventory*.mjs`.
- PR 2 owns `management-navigation.ts` and the three seeds.
- PR 3 owns its `management-*.ts` flow modules.
- PR 2 and PR 3 share spec files, so they are **strictly sequential** (plan §6).

**Per-commit rule (ADR-0087).** Every commit passes `pnpm lint:e2e`, `pnpm typecheck:e2e`, `pnpm format:check` and `pnpm test:guards`. Conventional Commits, no `Co-Authored-By` (ADR-0086).

**Stack rules.**

- One Aspire stack per machine.
- Stop the stack before building .NET.
- Baseline runs happen on `develop` code, before any spec edit on the branch.

`[P]` = can run in parallel with the preceding task(s) in the same phase (disjoint files, no ordering dependency).

---

## Phase 0: Docs (this branch, `refactor/2661-e2e-page-layer`)

- [x] **T000** [Docs] ADR-0162, spec.md (gate decisions recorded), plan.md and tasks.md committed on this branch.
- [ ] **T000a** [Docs] Phase 3 gate (orchestrator): put feature issue #2661 on Project #13 with `gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2661`, then verify by `content.url` with `--limit 2000`. Not done by the architect.

---

## PR 1: US1, the wall session rig, plus the inventory tooling

Branch `refactor/2661-us1-wall-session-rig`, cut from `develop`.

### Foundation: inventory tooling (RED). Blocks every characterisation task in all three PRs.

- [ ] **T001** [Found] `scripts/e2e-assertion-inventory.test.mjs`: the eight `node:test` cases in plan §4.3 against `collapseRuns`/`diffInventories`. Commit alone. Red run 1 (`ERR_MODULE_NOT_FOUND`) captured verbatim.
- [ ] **T002** [Found] Stub `scripts/e2e-assertion-inventory.mjs` exporting a `diffInventories` that reports no differences and a pass-through `collapseRuns`. Red run 2 captured verbatim: each case expecting a difference or collapse fails for its own reason.
  - **Depends on:** T001.
- [ ] **T003** [Found] Real `scripts/e2e-assertion-inventory.mjs`:
  - the default-export reporter (plan §4.2: `expect`-category steps, `{title, subtitle}`, no location, `retry === 0`, output path from `E2E_ASSERTION_INVENTORY_FILE`);
  - `collapseRuns`, `diffInventories` (with noisy-test detection) and the CLI entry.
  - `node --test scripts/e2e-assertion-inventory.test.mjs` green, test file unmodified.
  - **Depends on:** T002.
- [ ] **T004** [Found] Smoke-check that the reporter records real steps: with the stack up, `pnpm test:e2e --project=chromium e2e/audit.spec.ts --reporter=list,./scripts/e2e-assertion-inventory.mjs` produces an inventory whose one test lists its `expect` steps in source order (`toBeVisible` heading, …). Quote the JSON in the PR. This proves the reporter hooks `onStepEnd` at all; T003's tests only cover the pure functions.
  - **Depends on:** T003.

### US1 characterisation baseline

- [ ] **T101** [US1] On the branch **before T102** (spec files still identical to `develop`):
  - `pnpm test:e2e --list > us1-before.list`;
  - stack up; `pnpm test:e2e --project=wall --reporter=list,./scripts/e2e-assertion-inventory.mjs` **twice**, into `us1-baseline-a.json` and `us1-baseline-b.json` (`E2E_ASSERTION_INVENTORY_FILE`);
  - record the pass/fail summary of each run.
  - Artefacts stay in the scratchpad, not the repository.
  - **Depends on:** T004.

### US1 extraction (plan §5.1)

- [ ] **T102** [US1] `e2e/support/wall-session.ts`: `WALL_USER`, `WALL_PASSWORD`, `signInAsWallDisplay(page, { url, timeout, expectPopulatedPicker })`, `claimsOf`, `issuerOf`, `storedAccessToken(page, message)`, `expireStoredAccessToken(page, message)`, with bodies taken from the existing copies. In the same commit, switch `wall-survives-a-process-death.spec.ts` to it (its copies deleted; passes the absolute URL, `90_000`, `expectPopulatedPicker: true` and its own messages). Update its "copied rather than extracted" header comment.
  - **Depends on:** T101.
- [ ] **T103** [US1] `e2e/support/kiosk-session.ts:openFirstLayout` gains `options?: { timeout?: number }`, with the default behaviour unchanged. `wall-survives-a-process-death` uses it with `{ timeout: 90_000 }` and its local copy is deleted.
  - **Depends on:** T102.
- [ ] **T104** [US1] `wall-survives-a-lockout.spec.ts`: delete its five copied helpers and its `WALL_*` constants, import from `wall-session.ts` / `kiosk-session.ts` with its own variants (plan §5.1 table), and update its header comment.
  - **Depends on:** T103.
- [ ] **T105** [US1] `wall-outlives-its-session.spec.ts`: `signInAsWallDisplay` (defaults: `'/'`, `60_000`, no populated-picker check), `claimsOf`, `WALL_*`. `storedGrant` stays local (single caller).
  - **Depends on:** T102.
  - **[P] with T104** (disjoint files).
- [ ] **T106** [US1] `wall-withdrawal.spec.ts`: its `signIn` keeps the refresh-token read but calls `signInAsWallDisplay(page)` for the form half. `claimsOf` and `issuerOf` are imported (narrow `'sid'`/`'iss'` at use). `sessionOf` stays local.
  - **Depends on:** T102.
  - **[P] with T104, T105.**
- [ ] **T107** [US1] `wall-authority.spec.ts`:
  - `signInAndReadToken` keeps its gateway-request capture and token read, but calls `signInAsWallDisplay(page)` for the form half;
  - `scopesOf` uses `claimsOf` instead of its inline decode;
  - `WALL_*` imported.
  - **Depends on:** T102.
  - **[P] with T104-T106.**
- [ ] **T108** [US1] Verify SC-001: each of `signInAsWallDisplay`, `claimsOf`, `openFirstLayout`, `storedAccessToken`, `expireStoredAccessToken`, `issuerOf` has exactly one `function` definition under `e2e/` (Grep, quoted in the PR).
  - **Depends on:** T104-T107.

### US1 characterisation evidence

- [ ] **T109** [US1] `pnpm test:e2e --list > us1-after.list`; `diff us1-before.list us1-after.list` must be empty.
  - **Depends on:** T108.
- [ ] **T110** [US1] Stack up; one `--project=wall` run with the reporter into `us1-after.json`; `node scripts/e2e-assertion-inventory.mjs us1-baseline-a.json us1-baseline-b.json us1-after.json` exits 0. Name every noisy test.
  - **Depends on:** T109.
- [ ] **T111** [US1] Deliberate breaks, one at a time, reverted with `git checkout --` and a clean `git status` between each (plan §4.4 step 4):
  1. `signInAsWallDisplay` heading;
  2. its populated-picker check;
  3. `openFirstLayout`'s `layout-grid` test id;
  4. `storedAccessToken`'s not-null (point the key lookup at a prefix that matches nothing);
  5. `expireStoredAccessToken` likewise.

  For each, run one consuming wall test and quote the failure showing the helper's file:line.
  - **Depends on:** T110.
- [ ] **T112** [US1] Local gates: `pnpm lint:e2e`, `pnpm typecheck:e2e`, `pnpm format:check`, `pnpm test:guards`. Memory: *typecheck:e2e fails on a clean develop*. Stash-check before blaming the branch.
  - **Depends on:** T111.

### PR 1 phases 5-7

- [ ] **T113** [US1] Phase 5 note: T004, T109, T110 and T111 quoted, plus CI `e2e-shards` 1-4 and `e2e-shard-coverage` green on the PR's tip SHA.
  - **Depends on:** T112.
- [ ] **T114** [US1] Phase 6: `frontend-reviewer`. The review must compare the plan §5.1 per-caller variant table against the diff (URL, timeout, picker check, messages).
  - **Depends on:** T112.
- [ ] **T115** [US1] Phase 7: PR `--base develop`. The body says `Refs #2661` (**not** `Closes`: US2/US3 remain) and carries the two red runs (T001/T002).
  - **Depends on:** T113, T114.

---

## PR 2: US2, console navigation

Branch `refactor/2661-us2-console-navigation`, cut from `develop` **after PR 1 merges** (needs the reporter).

- [ ] **T201** [US2] Classify all 61 sidebar-nav sites (spec §1.2: 12 specs plus 3 seeds) into:
  - (i) click + section-heading pair: record the heading text, `exact` flag and page variable;
  - (ii) lone click, or click followed by something else.

  Write the table into the PR body draft, not the repository. It can start while PR 1 is in review, since it writes no files.
- [ ] **T202** [US2] Baseline, before T203:
  - `--list > us2-before.list`;
  - reporter runs **twice** for `chromium`, `seed`, `kiosk` and `wall` (the seeds are edited, and `kiosk`/`wall` depend on `seed`), into `us2-baseline-{a,b}.json`.
  - **Depends on:** PR 1 merged, T201.
- [ ] **T203** [US2] `e2e/support/management-navigation.ts`: the `ConsoleSection` union, `clickSidebarLink`, and `openSection(page, section, { exact })` (plan §5.2), with the `exact` default set from T201's majority. In the same commit, migrate `overlays.spec.ts` (10 pairs + remaining lone clicks).
  - **Depends on:** T202.
- [ ] **T204** [US2] Migrate `system-variables.spec.ts`, `layouts.spec.ts` and `audit.spec.ts`.
  - **Depends on:** T203.
- [ ] **T205** [US2] Migrate `in-flight-focus.spec.ts` (16 pairs), `rules.spec.ts` (its local `openRules` keeps its alert assertion and calls `openSection`) and `command-palette.spec.ts`.
  - **Depends on:** T203.
  - **[P] with T204.**
- [ ] **T206** [US2] Migrate `interaction-states.spec.ts`, `kiosk-reconciliation.spec.ts`, `kiosk-shows-a-label-over-video.spec.ts` (nav site outside the measured window only), `spanning-wall.spec.ts` and `wall-changes-its-scene.spec.ts`.
  - **Depends on:** T203.
  - **[P] with T204, T205.**
- [ ] **T207** [US2] Migrate the three seeds: `seed-bound-overlay-wall.setup.ts`, `seed-live-video-wall.setup.ts`, `seed-published-layout.setup.ts`.
  - **Depends on:** T203.
  - **[P] with T204-T206.**
- [ ] **T208** [US2] Verify SC-002: no file outside `management-navigation.ts` contains `getByRole('link', { name: /^(cameras|layouts|walls|overlays|system variables|rules|audit)$/i })` (Grep, quoted).
  - **Depends on:** T204-T207.
- [ ] **T209** [US2] Evidence:
  - empty `--list` diff;
  - `after` inventory for `chromium`, `seed`, `kiosk` and `wall`, diff exit 0, noisy tests named;
  - deliberate break of `openSection`'s heading assertion, observed red in one `chromium` test and reverted.
  - **Depends on:** T208.
- [ ] **T210** [US2] Local gates as T112.
  - **Depends on:** T209.
- [ ] **T211** [US2] Phases 5-7 as T113-T115, with `Refs #2661`.
  - **Depends on:** T210.

---

## PR 3: US3, console form flows

Branch `refactor/2661-us3-console-flows`, cut from `develop` **after PR 2 merges** (shared spec files).

- [ ] **T301** [US3] Classify all create-form sites (plan §5.3 step 1: `#register-camera-name`, `#overlay-name`, `#layout-name`, `#variable-name`, `#rule-*`) as **arrange** or **act**, then group arrange sites by identical steps and arrival assertion. Output: the list of flows with ≥ 2 callers in different files, each with its per-caller parameter values. **Only these flows are extracted.** Every act site and every singleton stays inline. The table goes in the PR body. Expected to include `registerCamera` (`camera-detail`, `interaction-states`) and `fillRuleForm` (`rules`, `in-flight-focus`). Create-draft flows depend on this classification.
- [ ] **T302** [US3] Baseline as T202, for the projects whose files T301 selected (`seed`/`kiosk`/`wall` only if a seed is in the list).
  - **Depends on:** PR 2 merged, T301.
- [ ] **T303** [US3] `e2e/support/management-cameras.ts:registerCamera(page, { name, url })` (arrival: the `link` visible within `FIRST_WRITE_TIMEOUT_MS`), with `camera-detail` and `interaction-states` migrated in the same commit. The name prefix and `Date.now()` stay at the call site.
  - **Depends on:** T302.
- [ ] **T304** [US3] `e2e/support/management-rules.ts:fillRuleForm(page, name)`, with `rules` and `in-flight-focus` migrated.
  - **Depends on:** T302.
  - **[P] with T303.**
- [ ] **T305** [US3] One task per further flow T301 selected (e.g. create-overlay-draft, create-layout-draft), each a new per-section module plus its callers in one commit. Tasks are added to this file by T301's outcome **before** implementation starts. Two flows that share a caller file are sequenced, not `[P]`.
  - **Depends on:** T302.
- [ ] **T306** [US3] Evidence:
  - empty `--list` diff;
  - inventory diff exit 0, noisy tests named;
  - deliberate break for every extracted flow that asserts arrival (`registerCamera`'s link, and each create-draft's row). `fillRuleForm` asserts nothing and needs none.
  - **Depends on:** T303-T305.
- [ ] **T307** [US3] Local gates as T112.
  - **Depends on:** T306.
- [ ] **T308** [US3] Phases 5-7 as T113-T115. The body says `Closes #2661`. Confirm the issue is closed after merge (memory: a PR mention rarely auto-closes it).
  - **Depends on:** T307.

---

## Dependency summary

```
T000 (docs)
PR 1: T001 → T002 → T003 → T004 → T101 → T102 → T103 → T104 ─┐
                                              T102 → T105 [P] ─┤
                                              T102 → T106 [P] ─┼→ T108 → T109 → T110 → T111 → T112 → T113/T114 → T115
                                              T102 → T107 [P] ─┘
PR 2: T201 (may start during PR 1 review) ; PR 1 merged → T202 → T203 → {T204,T205,T206,T207} [P] → T208 → T209 → T210 → T211
PR 3: PR 2 merged → T301 → T302 → {T303,T304} [P], T305… → T306 → T307 → T308
```
