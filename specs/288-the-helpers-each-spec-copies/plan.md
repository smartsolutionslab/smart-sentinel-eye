# Plan 288: The helpers each spec copies

**Spec:** [spec.md](./spec.md) · **ADR:** [ADR-0162](../../docs/adr/0162-e2e-helpers-are-plain-functions-per-surface.md) · **Issue:** #2661 · **Spec branch:** `refactor/2661-e2e-page-layer`
**Gate decisions (2026-09-29):**

- **Q1 = C:** plain functions, one module per surface, in `e2e/support/`.
- **Q2 = (a):** duplicated clusters only, one PR per story.
- **Q3 = (b) + (c):** an unchanged ordered assertion inventory, plus one deliberate-break run for each helper that asserts arrival.

## 1. Technical context

| Item | Value |
|---|---|
| Language / runtime | TypeScript Playwright specs (`e2e/tsconfig.json`), `@playwright/test` ^1.63.0 (installed 1.63.0); reporter and diff are plain `.mjs` run by Node 22 |
| Surfaces touched | `e2e/*.spec.ts`, `e2e/support/*.ts`, three `e2e/support/*.setup.ts` seeds (US2/US3), new `scripts/e2e-assertion-inventory*.mjs` |
| Product code | **None.** No `apps/*`, `src/*`, AppHost, realm or CI workflow change. |
| Bounded context / layers / messaging | None. Test-code refactor. No domain entities, no events, no NetArchTest impact. |
| Existing gates that must stay green | `pnpm lint:e2e` (eslint, `--max-warnings 0`), `pnpm typecheck:e2e`, `pnpm format:check`, `pnpm test:guards`, CI `e2e-shards` 1-4 + `e2e-shard-coverage` |
| Latency (constitution §IV) | **N/A.** No leg touched. The two measuring specs (`kiosk-shows-a-label-over-video`, `click-to-first-frame`) are outside all three stories except for nav-link sites (US2) that sit in their setup, not their measured windows (§5.2). |

## 2. Constitution and ADR check

| Rule | Status |
|---|---|
| ADR-0162 (new): plain functions, per-surface modules, extract only with a second caller in another file, helpers assert arrival only | This plan's design (§5). |
| ADR-0108: real Keycloak sign-in per test | Kept. No `storageState`. Sign-in helpers still drive the form. |
| ADR-0109: `e2e/support/*` is a contention file | Each story owns distinct new modules. US1 is file-disjoint from US2/US3. US2 and US3 share spec files and are sequenced (§6). |
| ADR-0144 §4a / constitution §Testing: colour | **Characterisation (behaviour-preserving)** for all three stories. The one piece of new behaviour, the inventory reporter's diff logic, is a guard and is **red first** (§4.3). |
| ADR-0087: each commit builds alone | Every commit passes `lint:e2e`, `typecheck:e2e`, `test:guards`. Extraction commits are per helper group, not "add module" then "use module" split across a broken middle. |
| ADR-0036: smallest change | Only duplicated code moves. Within-file repetition and single-caller helpers stay. |
| ADR-0086: no `Co-Authored-By` | Applies to every commit. |

No violations.

## 3. Files per story

| Story | New | Edited |
|---|---|---|
| **US1 (PR 1)** | `scripts/e2e-assertion-inventory.mjs`, `scripts/e2e-assertion-inventory.test.mjs`, `e2e/support/wall-session.ts` | `e2e/support/kiosk-session.ts` (optional `timeout` on `openFirstLayout`); `e2e/wall-outlives-its-session.spec.ts`, `e2e/wall-survives-a-lockout.spec.ts`, `e2e/wall-survives-a-process-death.spec.ts`, `e2e/wall-withdrawal.spec.ts`, `e2e/wall-authority.spec.ts` |
| **US2 (PR 2)** | `e2e/support/management-navigation.ts` | the 12 spec files with sidebar-nav sites (spec §1.2) + `seed-bound-overlay-wall.setup.ts`, `seed-live-video-wall.setup.ts`, `seed-published-layout.setup.ts` |
| **US3 (PR 3)** | one module per console section whose flow has ≥ 2 callers in different files; candidates `e2e/support/management-cameras.ts`, `management-rules.ts`, `management-overlays.ts`, `management-layouts.ts`, `management-variables.ts`. **Final list set by T301's classification**, and a candidate that ends with fewer than two cross-file callers is not created. | the classified caller files, a subset of US2's |

`kiosk-shows-a-wall.spec.ts`, `kiosk-*.spec.ts` (other than nav-link sites), `self-hosted-fonts.spec.ts` and the measurement helpers in the two latency specs are **not touched** by any story.

## 4. The characterisation mechanism (built in US1, reused by US2/US3)

### 4.1 Why a reporter

The CI JSON report cannot supply the inventory. The pinned Playwright's `JSONReporter._serializeTestResult` keeps `result.steps.filter(s => s.category === "test.step")`, so every `expect` step is dropped (spec §4, Q3 resolution). A custom reporter receives every step through `onStepEnd`.

### 4.2 `scripts/e2e-assertion-inventory.mjs`

One ES module with two roles:

- **Reporter** (default export, a class implementing `onStepEnd`, `onTestEnd` and `onEnd`). For each test it records the ordered list of steps with `category === "expect"` as `{ title, subtitle }`. `title` is the custom `expect(…, 'message')` if given, else `Expect "<soft|not|poll …>matcher"`. `subtitle` carries the locator/receiver description. **`location` is deliberately not recorded**: an extracted helper moves the `expect` to another file, and that move is the refactor, not a change. Keyed by `project › file › title path`, retry number included; only `retry === 0` results are compared. Output path from `E2E_ASSERTION_INVENTORY_FILE` (default `test-results/assertion-inventory.json`).
  - Invoked as an *additional* reporter: `pnpm test:e2e --project=<p> --reporter=list,./scripts/e2e-assertion-inventory.mjs`. Nothing in `playwright.config.ts` changes.
- **Diff** (named exports `collapseRuns(entries)` and `diffInventories(baselineA, baselineB, after)`, plus a CLI entry when run directly):
  - `collapseRuns` merges *consecutive identical* entries into one. `toPass` blocks and retry loops re-run the same `expect` a variable number of times, and the count is not behaviour.
  - `diffInventories` compares `after` to `baselineA` per test. A test whose two baselines already disagree after collapsing is reported as **noisy** and excluded from pass/fail, but it is listed. A noisy test is not silently green: the PR must name it.
  - Tests present in one run and absent in another are a failure (the `--list` check should already have caught it).
  - Exit 0 = no differences in non-noisy tests; exit 1 = differences, printed as `test › index: before → after`.

The file name ends `.mjs`, not `.test.mjs`, so neither `test:guards` nor Playwright's default `testMatch` loads it as tests. The `scripts/` directory is outside `testDir: './e2e'`.

### 4.3 `scripts/e2e-assertion-inventory.test.mjs` (red first)

`node:test` cases on the pure `collapseRuns`/`diffInventories` exports, fixture objects only:

| Case | Expect |
|---|---|
| identical before/after | no differences |
| one `expect` removed from a test | difference naming the test and index |
| matcher changed (`toBeVisible` → `toHaveCount`) | difference |
| custom message changed | difference (messages are contract, ADR-0162 §4) |
| same expect repeated 3× vs 5× consecutively | no difference (collapsed) |
| baselines disagree on test X, after differs on X | X reported noisy, exit status unaffected by X |
| a test missing in `after` | difference |
| subtitle changed (`exact: true` dropped from a locator) | difference |

Red sequence: the test file is committed alone (module absent → `ERR_MODULE_NOT_FOUND`). Then a stub whose `diffInventories` returns no differences, so every case expecting a difference fails for its own reason. Then the real module. Both red runs are quoted in PR 1 (ADR-0139). Picked up by the existing `test:guards` glob `scripts/**/*.test.mjs`, with no `package.json` edit.

### 4.4 Per-story evidence procedure

1. **Baseline on `develop`** (before any spec edit on the story branch, or on a clean `develop` checkout at the story branch's merge base):
   - `pnpm test:e2e --list > before.list`.
   - Full stack up (`aspire run`). Run the affected projects **twice** with the inventory reporter into `baseline-a.json` and `baseline-b.json`. Twice, because the first run after machine churn can look like a regression.
2. **After** the story's edits: `--list > after.list` and one inventory run into `after.json`.
3. `diff before.list after.list` shows no output. Then `node scripts/e2e-assertion-inventory.mjs baseline-a.json baseline-b.json after.json` exits 0.
4. **Deliberate break per arrival-asserting helper** (§5): change the helper's arrival locator (e.g. heading name `'Pick a layout'` → `'Pick a layoutX'`), run **one** consuming test, and observe it red **on that helper's assertion**. The failure's call log must name the helper's file and line. Revert with `git checkout -- <file>` and confirm `git status` is clean.
5. The PR body quotes: the empty `--list` diff, the diff tool's output, the pass/fail summaries of all three runs, every noisy test by name, and each deliberate-break failure excerpt.

"Affected projects" per story: US1 is `wall` (which pulls in `seed` as a dependency and `cleanup` as teardown). US2 and US3 are `chromium` + `seed`, plus `kiosk` and `wall` if a seed file is edited, because both depend on `seed`.

## 5. Design per story

### 5.1 US1: `e2e/support/wall-session.ts`

Exports, each consolidating the copies in spec §1.5:

| Export | Replaces | Parameters preserving each caller's variant (FR-005) |
|---|---|---|
| `WALL_USER`, `WALL_PASSWORD` | five per-file constants | none |
| `signInAsWallDisplay(page, options?)` | 3 × `signInAsWallDisplay`; the form half of `wall-withdrawal:signIn` and `wall-authority:signInAndReadToken` | `url` (default `'/'`; the persistent-context specs pass `'http://localhost:5175/'`), `timeout` (default `60_000`; lockout/process-death pass `90_000`), `expectPopulatedPicker` (default `false`; lockout/process-death pass `true`, keeping their `'the seed project publishes a layout'` message) |
| `claimsOf(token)` → `Record<string, unknown>` | 4 × `claimsOf`, plus the inline decode in `wall-authority:scopesOf` | none. `wall-withdrawal`'s `Record<string,string>` callers (`issuerOf`, `sessionOf`) narrow at their use. The thrown message stays `'a grant should have a payload segment'`. `wall-authority`'s differing throw text (`'a wall access token should have a payload segment'`) is a thrown error, not an `expect`, and is invisible to the inventory. Take the shared text and note it in the PR. |
| `issuerOf(token)` | 2 copies | none. Keep the stricter `typeof … === 'string' && !== ''` check, which is equivalent for the string claims both callers receive. |
| `storedAccessToken(page, message)` | 2 copies | `message`: each caller's `expect(…, message)` text differs ("the wall display should be holding a grant" / "the recovered wall display should be holding a grant") |
| `expireStoredAccessToken(page, message)` | 2 copies | `message` likewise |

`openFirstLayout`: **reuse `kiosk-session.ts`'s** by adding `options?: { timeout?: number }` (default unchanged, so kiosk callers are byte-for-byte the same at run time). The two wall copies pass `{ timeout: 90_000 }` and are deleted.

**Stays in its spec file** (single caller, ADR-0162 §3): `wall-outlives-its-session:storedGrant`, `wall-withdrawal:sessionOf` and the refresh-token read in its `signIn` wrapper, `wall-authority`'s gateway-origin capture, `wall-survives-a-process-death:stopTracing`, and every `page.evaluate` that writes storage. The specs' header comments that say "copied rather than extracted" are updated to point at `wall-session.ts`, because the reason they give no longer holds.

**Arrival-asserting helpers needing a deliberate break (§4.4 step 4):** `signInAsWallDisplay` (heading, and the populated picker when enabled), `openFirstLayout` (`layout-grid`), `storedAccessToken` and `expireStoredAccessToken` (not-null grant).

### 5.2 US2: `e2e/support/management-navigation.ts`

```ts
export type ConsoleSection = 'Cameras' | 'Layouts' | 'Walls' | 'Overlays' | 'System variables' | 'Rules' | 'Audit';
export async function clickSidebarLink(page: Page, section: ConsoleSection): Promise<void>;
export async function openSection(page: Page, section: ConsoleSection, options?: { exact?: boolean }): Promise<void>;
```

- `clickSidebarLink` is the click only, `getByRole('link', { name: /^<section>$/i })`. It asserts nothing and is used where a site has no heading assertion after its click.
- `openSection` is the click plus `expect(page.getByRole('heading', { name: section, exact })).toBeVisible()`. It is used **only** where the site already has that exact pair. The regex count is 47 of 61 sites (spec §1.2). `exact` defaults to whatever the majority of sites use; T201 records the per-site value, and a site differing from the default passes it explicitly, because `exact` appears in the inventory subtitle.
- A typo'd section is a type error (US2 bad-request scenario).
- Per-file repetition (overlays × 12, system-variables × 7) collapses as a *consequence* of using the shared call. No per-file constant is introduced alongside.
- **Not touched:** entity-name links, "Back to cameras", and any nav click followed by something other than the section heading. Those use `clickSidebarLink` plus their own following assertion, unchanged.
- **Arrival-asserting helper needing a deliberate break:** `openSection`.

### 5.3 US3: per-section flow modules

Selection rule, applied by T301 before any edit:

1. **List** every site filling a create form: `#register-camera-name`, `#overlay-name`, `#layout-name`, `#variable-name`, `#rule-*` (62 sites, spec §1.5 + grep).
2. **Classify** each as *arrange* (the create is setup for what the test proves) or *act* (the create, its in-flight window or its focus is what the test proves). `in-flight-focus.spec.ts` interposes `holdWrites` between fill and submit and asserts focus mid-flight, so its register/define/create sites are **act** and stay inline. So do the `interaction-states` sites that read the name input itself (`:383`, `:421`).
3. **Group** the *arrange* sites by identical step sequence. A flow is extracted only if it has callers in ≥ 2 files **and** they share the same steps and arrival assertion. Differences in values (camera URL, name prefix) become parameters. Differences in steps or arrival (`cell` vs `link` visible after register) are **separate flows or left inline**, never unified.

Known candidates from the Phase 1 read:

- `registerCamera(page, { name, url })`: `camera-detail`, `interaction-states` (same steps, same `link` arrival, differing URL and name).
- `fillRuleForm(page, name)`: `rules`, `in-flight-focus` (identical bodies; fill only, no submit, no assertion, so no deliberate break needed).
- Create-overlay-draft and create-layout-draft sequences in `overlays`, `layouts`, `wall-changes-its-scene`, `spanning-wall` and the seeds. These depend on T301's classification.

Every extracted flow that asserts arrival (e.g. `registerCamera`'s `link` visible with `FIRST_WRITE_TIMEOUT_MS`) gets a deliberate break.

## 6. Sequencing

```
PR 1 (US1 + inventory tooling) ──merge──▶ PR 2 (US2) ──merge──▶ PR 3 (US3)
```

- **PR 1 goes first** because it lands the inventory reporter PRs 2 and 3 need for their evidence. Its files are disjoint from PR 2 and PR 3.
- **PR 2 before PR 3**: they share spec files (`in-flight-focus`, `layouts`, `overlays`, `system-variables`, `rules`, `spanning-wall`, `wall-changes-its-scene`, the seeds). PR 2's edits are mechanical and wide. PR 3's are judgement-heavy and narrower, so PR 3 rebases onto PR 2's result, not the reverse.
- **Parallelism available:** PR 2's *classification and baseline* (T201, T202) can run while PR 1 is in review, because they write no repository files. PR 2's branch is cut after PR 1 merges.
- Each PR's branch is cut from `develop` at its start: `refactor/2661-us1-wall-session-rig`, `refactor/2661-us2-console-navigation`, `refactor/2661-us3-console-flows`. This spec branch carries only the docs and is its own docs PR (or the docs commits ride on PR 1; orchestrator's call).

## 7. Risks

| Risk | Mitigation |
|---|---|
| An extracted `expect` loses its `await`: the inventory still records it and the test still passes | Deliberate break per arrival helper (§4.4 step 4); the failure must point at the helper's line |
| A unified helper silently changes one caller's budget or message | Parameters per §5.1/§5.2; the inventory captures message text; timeouts are code-reviewed against the per-caller table in the PR |
| The `wall` project is slow (tests up to 600 s) and flaky under churn | Two baselines; noise masked only where the baselines themselves disagree, and every noisy test is named |
| A seed edit breaks `kiosk`/`wall` indirectly | Seeds only in US2/US3; those PRs run `kiosk` and `wall` inventories too |
| One machine, one Aspire stack | Baselines and after-runs are sequential on the same stack; never two boots |
