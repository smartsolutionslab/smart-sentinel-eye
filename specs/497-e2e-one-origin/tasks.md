# Tasks: The end-to-end suite on the one origin

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md) | **Contract**:
[contracts/one-origin-module.md](./contracts/one-origin-module.md) | **Issue**: #3247 (epic #3248), on Project #13 (Todo; checked 2026-11-14
through the issue's `projectItems`). No per-task issues.

**Delivery (owner, Q2 A, staged by the origin a reference points at; plan decision 1).** One spec, **three PRs**, each cut from an up-to-date
`develop`: **stage 1** the console (`:5173`) plus the new support module, the guard and the warm-up, from this branch, so the spec documents ride in
it; **stage 2** the kiosk (`:5174`); **stage 3** the wall display (`:5175`). A stage begins only after the previous one is **merged** and CI on
`develop` is green; rebase-merge renames SHAs (ADR-0087), so each later branch is cut from the new `develop`, not stacked on the old one. Every task
and PR names its stage.

**Phase 4a colour.** Red first for new behaviour (the guard, its counterfactuals, the warm-up, the helper-assuming-the-root premise). The suite move
itself is a **behaviour-preserving move: characterisation**, green before on `develop` and green after with assertions **unmodified**; the one
assertion edited (stage 2, T020) is declared. A red that arrives green is a phase-4 failure: stop and report.

**Never touched**: product source, `src/AppHost`, the realm, the gateway, `apps/**` (the guard only *reads* it); which spec runs in which project
(`testMatch`, `testIgnore`, `dependencies`, `teardown` stay as they are); the fixed ports and everything that serves them (Q1 A); any spec's
intent. Never weaken, delete or skip a spec to reach green. Never `git stash` or `git checkout -- .`. **One Aspire stack per machine; stop it before
building.** **This machine has twice run out of memory under a full stack**: local stack runs are one or two specs on `--workers=1`; if the
harness stops a run for memory, record what was not run and **do not retry it**. No `Co-Authored-By`. Release build is not needed (no .NET changes);
`prettier --check`, `eslint --max-warnings 0` and `tsc -p e2e/tsconfig.json --noEmit` before pushing.

**§IV: legs touched none.**

Format: `[ID] [P?] [Story] Description`. `[P]` = disjoint files from every other `[P]` task in the same phase. Stories: US1 the console suite at the one origin,
US2 the kiosk and wall suites at their paths, US3 the guard, US4 the warm-up, US5 the fixed ports unchanged. **Stage** is in brackets.

## Phase 1 — Baseline (blocks everything)

- [ ] **T001** [Setup] Baseline on `497-e2e-one-origin`, clean tree. Record in `verification.md`: (a) per project,
      `pnpm exec playwright test --list --project=<chromium|seed|kiosk|wall|cleanup>` saved to a scratch file (kept for the diffs in T013, T023,
      T031; no stack needed); (b) `pnpm test:guards` green, with the count; (c) from the **last green `develop` CI run**, the e2e shard totals and the
      number of retried ("flaky") tests, as SC-005's baseline; (d) the exact count of hard-coded origin lines the plan states (25 in `e2e/`, 5 in the config,
      the bare `'5174'` in `kiosk-identity-refused.spec.ts:56`, 3 in `scripts/wait-for-e2e-stack.sh`) by re-running the search. If any disagrees, stop and
      correct the plan first.

## Stage 1 — the console (`:5173`), the support module, the guard, the warm-up

### The guard (US3), red first

- [ ] **T002** [S1] [US3] (test, **red**) Add `scripts/e2e-origins.test.mjs` **before** the scanner exists: (a) fixture cases: a scratch file with
      `http://localhost:5174` is reported with `file:line`; a bare `toBe('5174')` is reported; `localhost:5180` is reported anywhere except
      `e2e/support/origins.ts`; a `PERMANENT` entry is not reported; a `MIGRATION` entry is not reported; a `MIGRATION` entry whose occurrence is gone
      **fails** naming it ("remove it"); `*.test.*`, `src/AppHost`, `tests/**/*.cs`, `docs/` and `specs/` are not scanned; (b) a **real-tree** test: the
      scanner over the repo reports nothing outside the allow-lists. Run it: red because the module does not exist; quote the failure.
- [ ] **T003** [S1] [US3] Add `scripts/e2e-origins.mjs` per the contract: the scan rules, the scanned and not-scanned lists, an exported
      `findHardCodedOrigins(root, allow)` and the two allow-lists. Start with **both lists empty** and run T002 again: the fixture cases go green and
      the **real-tree test is red on today's tree**, listing every offending `file:line` (expect 31 lines in `e2e/` and the config plus the readiness
      script's, and the `apps/**` non-test occurrences the plan names). **Quote that listing**: it is the observation FR-004 asks for.
- [ ] **T004** [S1] [US3] Fill the allow-lists: `PERMANENT` with the three entries and their reasons (the readiness script's upstream waits,
      `apps/management-web/vite.config.ts`, `apps/management-web/src/app/auth.ts`), and `MIGRATION` with one entry per **stage 2 and stage 3** occurrence
      (kiosk `:5174`, wall `:5175`, and the two `goto('/')` helpers). The real-tree test now fails naming **only the stage-1 references**
      (the `:5173` and `:5180` ones); quote that shorter listing.

### The warm-up (US4), red first

- [ ] **T005** [P] [S1] [US4] (test, **red**) Add `scripts/warm-one-origin.test.mjs`: start a stub HTTP server (200 for `/` and `/kiosk/`, 404 for
      `/wall/`), run `scripts/warm-one-origin.sh` against it with a short retry budget, and assert a non-zero exit with a message **naming `/wall/`**; a
      second case with all three answering passes. Run it before the script exists: red; quote it.
- [ ] **T006** [P] [S1] [US4] Add `scripts/warm-one-origin.sh`: takes the origin, requests `/`, `/kiosk/` and `/wall/` through it with bounded retries, prints
      each path as it answers, and on failure exits non-zero naming the path that did not. T005 goes green. Make it executable and LF-ended.
- [ ] **T007** [S1] [US4] Depends on T006. In `scripts/wait-for-e2e-stack.sh` call the warm-up after the existing waits and before it reports ready (FR-006);
      the existing upstream waits stay (they are the `PERMANENT` entry); name the one origin and the upstream ports as constants at the top. Run
      `bash -n scripts/wait-for-e2e-stack.sh`.

### The move (US1)

- [ ] **T008** [S1] [US1] Add `e2e/support/origins.ts` exactly as the contract states (`ONE_ORIGIN`, `KIOSK_PATH`, `WALL_PATH`; no environment override).
- [ ] **T009** [S1] [US1] Depends on T008. In `playwright.config.ts`: the default `use.baseURL`, the `seed` project and the `cleanup` project
      use `ONE_ORIGIN` (imported); the `kiosk` and `wall` projects keep their ports for now (their `MIGRATION` entries). Nothing else in the config changes:
      `testMatch`, `testIgnore`, `dependencies` and `teardown` are untouched. Update the config's comments that name `:5173`.
- [ ] **T010** [P] [S1] [US1] Depends on T008. Replace the `:5173` literal with `ONE_ORIGIN` in `e2e/console-live-scene-switch.spec.ts` (2),
      `rule-switches-a-wall.spec.ts` (1), `wall-changes-its-scene.spec.ts` (1), `kiosk-overlay-tile-view-matches-the-wall.spec.ts` (1),
      `kiosk-paints-a-reordered-overlay.spec.ts` (1), `kiosk-reconciliation.spec.ts` (1), `kiosk-shows-a-label-over-video.spec.ts` (1),
      `kiosk-shows-shapes-over-video.spec.ts` (1) and `kiosk-shows-two-labels-over-video.spec.ts` (1), and the `5180` literal in
      `one-origin-sign-in.spec.ts`. **Only the origin changes**: no assertion, timeout or step is touched. The `kiosk-*` and `wall-*` specs
      keep their own `:5174` and `:5175` contexts (their entries).
- [ ] **T011** [S1] [US1] Read-through (FR, edge case "one browser, three apps, one storage"): for each spec T010 touched, read its use of
      `browser.newContext`, `context.newPage` and the default `page`, and record in `verification.md` whether any **single context** opens the console and
      another app. A hit is fixed by giving the second app its own context (a change to that spec, named in the PR); none is the expected result and is
      still recorded.
- [ ] **T012** [S1] [US3] Depends on T004, T009, T010. Run T002's real-tree test: **green**, with the stage 1 migration entries needing no deletion (there
      were none for `:5173`); quote it. Then delete one `MIGRATION` entry by hand to see the **ratchet** fail ("remove it" for a stale entry is the
      other direction: add back an entry for an occurrence that is gone and see it fail), and restore. Quote both.

### Proof (US1, US5)

- [ ] **T013** [S1] [US1] **Which spec runs where is unchanged (FR-008, SC-002).** Re-run T001(a)'s `--list` per project, `diff` against the baseline:
      **empty** for all five projects. A difference is a stop.
- [ ] **T014** [S1] [US1] **Small local run.** Boot fresh (quickstart step 6; stop first; one stack), `scripts/wait-for-e2e-stack.sh`, and confirm its
      **new warm-up output** names the three paths answering. Then, one worker, one at a time: `e2e/cameras.spec.ts` (`chromium`, now at `:5180`) and
      `e2e/console-live-scene-switch.spec.ts` (two contexts, both the console). Record the results. If memory goes critical, stop, stop the stack, and
      record what was not run. Stop the stack afterwards and verify nothing is left.
- [ ] **T015** [S1] [US5] The fixed ports are untouched by construction: `git diff --stat origin/develop...HEAD` shows **no file outside** `e2e/`,
      `scripts/`, `playwright.config.ts` and `specs/`; record it.

### Stage 1 polish and PR

- [ ] **T016** [S1] [Polish] `prettier --check` (the new files and the touched specs), `eslint e2e playwright.config.ts --max-warnings 0`,
      `tsc -p e2e/tsconfig.json --noEmit`, `pnpm test:guards`. Nothing suppressed.
- [ ] **T017** [S1] [Polish] `/verify`: write the stage 1 section of `verification.md` from T001 to T015 **as observed**, including both guard reds, the
      warm-up red, the `--list` diffs, what ran locally and what **did not**. State the latency leg: none.
- [ ] **T018** [S1] [Polish] Phase 6: `/code-review` with `infra-reviewer` (read-only: the config's unanchored routing, the readiness script, whether
      CI would catch a regression, the guard's exceptions) and `frontend-reviewer` for the specs. `/security-review` is **not** warranted: no origin
      list, credential or scope changes, and the PR body says why. Findings resolved or accepted in writing.
- [ ] **T019** [S1] [Polish] Phase 7: `gh pr create --base develop` with the template, **titled and described as stage 1 of 3**, the red output of T003,
      T004 and T005 quoted, `Refs #3247` (**not** `Closes`: the issue closes with stage 3), the retry-summary baseline. Read **all** CI buckets; for the e2e shards read
      the retry summary and compare with T001(c). **Stage 1 is the first CI run of every console spec at `:5180` through the gateway and the proxied
      remotes**: a failure there is a finding about the one URL; diagnose and record, and if it needs product code, stop and put it to the owner as its
      own spec. Merge per the standing authorization only when every bucket is green and the run tested the PR tip.

## Stage 2 — the kiosk (`:5174`) (cut from the new `develop`)

- [ ] **T020** [S2] [US2] Branch `497-stage-2-kiosk` from `origin/develop` **after stage 1 has merged and `develop`'s CI is green**; confirm the guard
      and `origins.ts` are there. **Red 1 (the premise, FR-001, no stack):** run
      `node -e "console.log(new URL('/', 'http://localhost:5180/kiosk/').href)"` and quote `http://localhost:5180/`: a base with a path does not survive a
      leading `/`. **Red 2 (the guard):** delete this stage's `MIGRATION` entries (the `:5174` ones and the kiosk `goto('/')` helpers); the real-tree
      test goes red naming exactly those `file:line`s; quote it.
- [ ] **T021** [S2] [US2] **Red 3 (the helper at the one origin, on a booted stack).** Boot fresh (as T014), and run **one** kiosk spec,
      `e2e/kiosk-shows-a-wall.spec.ts` (`kiosk` project), with the project's base URL temporarily pointed at `ONE_ORIGIN` and the helper's `goto('/')`
      **unchanged**: it must fail where the helper lands on the console, not the kiosk (the sign-in screen is the console's). Quote the failure; revert the
      temporary base URL (no commit). If memory forbids the stack, say so and rest on Red 1, naming the gap.
- [ ] **T022** [S2] [US2] The move. `playwright.config.ts`: the `kiosk` project's `baseURL` becomes `ONE_ORIGIN`. `e2e/support/kiosk-session.ts`, `kiosk-comes-back`,
      `kiosk-touch-target`: `goto('/')` on a kiosk becomes `goto(KIOSK_PATH)`. The `:5174` contexts in `kiosk-comes-back`, `kiosk-identity-herd`,
      `kiosk-touch-target`, `rule-switches-a-wall`, `spanning-wall` and `wall-changes-its-scene` (×2) become `ONE_ORIGIN` with the kiosk opened at
      `KIOSK_PATH`. Only origins and paths change.
- [ ] **T023** [S2] [US2] **The declared assertion edit.** `e2e/kiosk-identity-refused.spec.ts:56`, `expect(new URL(page.url()).port, …).toBe('5174')`, becomes
      an assertion that the page's **origin is `ONE_ORIGIN`** (and not the identity provider's), keeping the message ("a refused screen must not be
      redirected to the provider"). Same property; the PR calls this out as the one edited assertion, with Red 3's output showing the old form could not
      hold at the one origin. Read the remaining assertions of the file unchanged.
- [ ] **T024** [S2] [US2] Read-through (as T011) for every spec stage 2 touched, **and** every spec that now runs a kiosk and the console on one origin: record
      any single context that opens two apps.
- [ ] **T025** [S2] [US3] The guard goes green with no stage 2 entries left; quote it. `--list` per project against T001(a): **empty diff**.
- [ ] **T026** [S2] [US2] Small local run on a fresh stack, one worker: `e2e/kiosk-shows-a-wall.spec.ts` and `e2e/kiosk-comes-back.spec.ts`
      (`kiosk`, now at `:5180/kiosk/`), then stop the stack. Record what ran and what did not.
- [ ] **T027** [S2] [Polish] Static checks (as T016), `/verify` (stage 2 section), `/code-review` with `frontend-reviewer` and `infra-reviewer`, and
      the PR **titled stage 2 of 3**, `Refs #3247`, Reds 1 to 3 quoted, the edited assertion called out. All buckets read; the retry summary compared with
      the baseline; merge only when green on the PR tip.

## Stage 3 — the wall display (`:5175`) (cut from the new `develop`)

- [ ] **T028** [S3] [US2] Branch `497-stage-3-wall` from `origin/develop` after stage 2 merged and green. **Red 1:** `new URL('/', 'http://localhost:5180/wall/').href`
      is the root (quoted). **Red 2:** delete this stage's `MIGRATION` entries (the `:5175` ones and the wall helper's `goto('/')` default); the guard goes red naming
      them; quote it. **Red 3**, as T021 with `e2e/wall-outlives-its-session.spec.ts` (`wall`), one worker, if the stack fits in memory; otherwise say so.
- [ ] **T029** [S3] [US2] The move. `playwright.config.ts`: the `wall` project's `baseURL` becomes `ONE_ORIGIN`. `e2e/support/wall-session.ts`: the default `url`
      becomes `WALL_PATH`. `wall-outlives-its-session` (base and `goto('/')`), `wall-survives-a-lockout` and `wall-survives-a-process-death` (their
      `WALL` constants become `ONE_ORIGIN + WALL_PATH`), `wall-withdrawal` (×4): origins and paths only. **These three persistent-context specs keep
      their stored grant under the one origin now**: read each for anything keyed to the old origin and record it.
- [ ] **T030** [S3] [US2] Read-through (as T011) for every spec stage 3 touched, with particular attention to `wall-withdrawal` (two wall contexts) and the
      persistent contexts.
- [ ] **T031** [S3] [US3] The `MIGRATION` array is **empty**: delete it and the code that reads it, leaving `PERMANENT`. The guard goes green; quote it. `--list` per
      project against T001(a): **empty diff**. `git grep -nE "localhost:51(7[0-7])|\b517[3-7]\b" -- e2e playwright.config.ts scripts .github` prints only
      the `PERMANENT` files (quote it): SC-001.
- [ ] **T032** [S3] [US2] Small local run on a fresh stack, one worker: `e2e/wall-outlives-its-session.spec.ts`, then stop the stack; record what ran.
- [ ] **T033** [S3] [Polish] Static checks, `/verify` (stage 3 and a **final summary** against the spec's FR and SC, stating what was and was not
      observed across the three stages), `/code-review`, and the PR **titled stage 3 of 3**, **`Closes #3247`**, all buckets read, retry summary compared.
- [ ] **T034** [S3] [Polish] After the merge: comment on the epic (#3248) that phase 3 is delivered and that, per Q1 A, **the fixed ports stay** (the
      decision ADR-0192 decision 7 left to this phase); verify #3247 closed and the branches deleted; tick the phase in the epic body only if the owner wants
      its body edited, otherwise say so in the comment.

## Dependencies and order

```text
T001 → T002 → T003 → T004 ─┐
       T005 → T006 → T007 ──┤
       T008 → T009, T010 ───┴→ T011 → T012 → T013 → T014 → T015 → T016 → T017 → T018 → T019 ── merge ──┐
                                                                                                         ↓
                      T020 → T021 → T022, T023 → T024 → T025 → T026 → T027 ── merge ──→ T028 → T029 → T030 → T031 → T032 → T033 → T034
```

Within a stage, tasks marked `[P]` touch disjoint files. The **stages are strictly sequential**: each starts from a `develop` that contains the previous one
and whose CI is green. Each stack run (T014, T021, T026, T028, T032) is a **separate boot**: never two at once, and stopped before the next build.

## Implementation strategy

**Stage 1 is the smallest increment that is worth shipping** and carries the new machinery (the module, the guard, the warm-up) plus the console's
move; after it, nothing in the suite names `:5173`. Stages 2 and 3 are mechanical and each shrinks the guard's allow-list to its proof. The one
deliberate risk is stage 1's CI run, the first of the console suite at `:5180`; it is taken first so a product finding surfaces before the kiosk
and the wall are touched.
