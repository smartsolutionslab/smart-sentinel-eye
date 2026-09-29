# Feature Specification: The helpers each spec copies

**Spec:** 288 (next free number after 287, which sits on the unmerged `enhancement/2623-raw-button-to-button-primitive`; 284 appears on no ref this checkout can see and was left alone in case it is reserved elsewhere)
**Feature Branch**: `refactor/2661-e2e-page-layer` (cut from `origin/develop` at `aa08ce16`)
**Created**: 2026-09-28
**Status**: Phase 1 gate passed 2026-09-29. Q1 = C, Q2 = (a), Q3 = (b) + (c), relayed by the orchestrator; recorded in **ADR-0162**. Phases 2-3: [plan.md](./plan.md), [tasks.md](./tasks.md).
**Issue**: [#2661](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2661) — *e2e specs re-type the same locators file to file; no Page Object layer exists*. Label `tech-debt`; no comments. **Lane:** supervised.
**Input**: "Restructuring … around a Page Object (or fixture-based equivalent) convention is a behaviour-preserving refactor … picking the pattern, deciding what lives in `e2e/support/` vs. a new `e2e/pages/`, and sequencing the migration."

**ADRs referenced:** ADR-0108 (Playwright; real Keycloak login per test; specs under `e2e/`), ADR-0109 (parallel worktrees; **`e2e/support/*` is a named contention file**), ADR-0036 (smallest change; no speculative generality), ADR-0037 (phases), ADR-0139 and constitution §Testing (a refactor stays green; characterisation), ADR-0144 (Phase 4a colours).

**ADR gap, now filled by ADR-0162.** No ADR governed how e2e code is organised. ADR-0108 picks the tool and says nothing about locators or page structure; ADR-0109 only names `e2e/support/*` as a contention file. The convention chosen at the gate is recorded in [ADR-0162](../../docs/adr/0162-e2e-helpers-are-plain-functions-per-surface.md), written before Phase 2.

**Colour (ADR-0144 §4a): behaviour-preserving → characterisation.** Every existing spec is its own characterisation test. But this refactor edits the specs themselves, so "the covering tests pass unmodified" cannot be literally true here. Q3 settled what the safety net is instead (ADR-0162 §6).

---

## 1. What was measured (2026-09-28, tree `aa08ce16`)

The issue's evidence was re-measured rather than trusted. Two of its figures do not hold, one is overstated, and it misses the largest cluster of duplication — which turns out to be **deliberate and documented in the code**.

### 1.1 Size of the suite

| Claim in #2661 | Measured | Command / source |
|---|---|---|
| 34 spec files | **30** | `Glob e2e/*.spec.ts` (the only other `*.spec.ts` in the repo are two fixtures under `scripts/fixtures/trace-redaction/`) |
| ~8,000 spec lines | 8,029 — **holds** | `wc -l e2e/*.spec.ts` |
| `e2e/support/`: 15 files, 1,530 lines | **16 files, 1,735 lines** | `wc -l e2e/support/*` |

### 1.2 Nav-link repetition — holds in substance, but most of it is inside one file

`getByRole('link', { name:` appears **72 times in 17 files** (14 specs + 3 `*.setup.ts` seeds), not 75. Of those:

- **~12 are not sidebar navigation** — they click an entity by name (`{ name: original }`, `{ name: cameraName, exact: true }`) or "Back to cameras". A nav helper does not touch them.
- **52 are sidebar-nav clicks in 12 spec files, plus 9 in the three seed setups** — 61 in all, across seven destinations (Cameras, Layouts, Walls, Overlays, System variables, Rules, Audit).
- **19 of the 52 are one locator repeated inside a single file**: `overlays.spec.ts` × 12 (all `/^overlays$/i`), `system-variables.spec.ts` × 7. That is per-file repetition a local constant already solves; it is not evidence *for* a cross-file layer.
- Nearly every nav click is followed by the same page-heading assertion (`getByRole('heading', { name: 'Overlays', exact: true })`). The unit that repeats is **"go to section X and wait until it has rendered"**, not a lone locator.

### 1.3 `GEOMETRY_ERROR_TEST_IDS` — overstated

The array has **four** entries, not five, and is used in **one file only** (`overlays.spec.ts`, 8 references). The #2365 gotcha it encodes has not been rediscovered anywhere. It is a reasonable candidate for a named locator, but today it is a hypothetical duplicate, not a real one.

### 1.4 JWT decoders — understated

The issue names three decoders (`wall-authority`, `wall-outlives-its-session`, `wall-withdrawal`). There are **five in spec files** — plus `wall-survives-a-lockout` and `wall-survives-a-process-death` — **and two more already in `e2e/support/`** (`kiosk-session.ts:readKioskAccessToken`, `management-session.ts:readManagementAccessToken`). So the helper exists, twice, and the specs do not use it. `overlays.spec.ts` likewise reads the management token inline from `sessionStorage` three times (lines 107, 549, 602) although `management-session.ts` does exactly that.

`oidc.user:` storage lookups: **29 occurrences across 14 files.**

### 1.5 What the issue missed: whole functions copied between the wall specs

A search for top-level `function` declarations in spec files shows the densest duplication is not locators at all — it is the **wall session rig**:

| Function | Copies | Files |
|---|---|---|
| `signInAsWallDisplay` | 3 | `wall-outlives-its-session`, `wall-survives-a-lockout`, `wall-survives-a-process-death` |
| `claimsOf` (JWT payload decode) | 4 | the three above + `wall-withdrawal` |
| `openFirstLayout` | 2 + 1 in `support/kiosk-session.ts` | `wall-survives-a-lockout`, `wall-survives-a-process-death` |
| `storedAccessToken` / `expireStoredAccessToken` | 2 each | `wall-survives-a-lockout`, `wall-survives-a-process-death` |
| `issuerOf` | 2 | `wall-survives-a-lockout`, `wall-withdrawal` |
| `registerCamera` | 2 | `camera-detail`, `interaction-states` |
| `fillRuleForm` | 2 | `in-flight-focus`, `rules` |

**This duplication is deliberate.** `wall-survives-a-process-death.spec.ts:43-46` says: *"Copied from `wall-outlives-its-session.spec.ts:18-26` rather than extracted: `e2e/support/*` is an ADR-0109 contention file and the extraction is a separate refactor (ADR-0036)."* `wall-survives-a-lockout.spec.ts:28-35` repeats the reasoning for its five copied functions. So the root cause is not "no shared layer exists" — `support/` is that layer — but that **extracting into it was repeatedly deferred to "a separate refactor"**, and #2661 is that refactor. Any design that concentrates more code in fewer shared files makes the ADR-0109 contention that caused the deferral **worse**, not better.

### 1.6 The copies are not identical

The `signInAsWallDisplay` copies differ in ways that matter to their tests:

- `wall-outlives-its-session` navigates to `'/'` (project `baseURL`), waits **60 s**, and asserts only the picker heading.
- `wall-survives-a-lockout` / `-a-process-death` navigate to an **absolute** `http://localhost:5175/` (a manually launched persistent context inherits no `baseURL`), wait **90 s**, and additionally assert a populated first list item.

A consolidation that picks one copy changes timeouts and assertions for the others. Consolidation must be parameterised, or it is a behaviour change.

### 1.7 Shapes any pattern has to fit

- **14 of 30 specs create pages or contexts beyond the test's own `page`** (`chromium.launch*`, `browser.newContext`, `newPage`) — kiosk + operator pairs, persistent profiles that survive a process kill, second contexts. A layer that only works through the test-scoped `page` fixture does not reach them.
- **Sign-in is the real Keycloak form in every test** (ADR-0108: "not a faked token"). Playwright's usual fixture-era auth pattern — sign in once in a setup project and reuse `storageState` — would stop exercising the login on every test. That is a behaviour change to what the suite proves and is **out of scope** here.
- **Projects are selected by filename** (`playwright.config.ts:56,71,83`: `(kiosk|wall)-.*\.spec\.ts`, `kiosk-.*`, `wall-.*`). New non-spec modules do not match these patterns; a helper file named `*.spec.ts` or `*.test.ts` would be picked up as tests.
- **CI shards e2e four ways** and `e2e-shard-coverage` compares `pnpm test:e2e --list` against the union of the shards (`ci.yml:789-846`). Test titles are the identity CI already checks.
- **No `test.extend` exists anywhere in `e2e/` today.** Seeding and teardown are done with Playwright *projects* (`seed`, `cleanup`), which already give the setup/teardown pairing fixtures are usually adopted for.
- Spec comments are unusually load-bearing: they state which assertion is the red one, which is a counterfactual, which timeout budget applies and why. A layer that moves **assertions** out of the spec file moves them away from that reasoning.

### 1.8 What current Playwright guidance says, and how far it applies

Playwright's own docs present Page Object Models as plain classes holding a `Page`, locators and action methods (playwright.dev/docs/pom — which does not mention fixtures), and separately recommend `test.extend` fixtures for setup/teardown, composition and on-demand initialisation, showing POM classes *exposed through* fixtures (playwright.dev/docs/test-fixtures). The two are complementary, not rivals. The fixture advantages the docs list — setup and teardown together, on-demand initialisation, composition — mostly address **setup/teardown**, which this suite already handles through projects. They address locator duplication only indirectly.

---

## 2. User Scenarios & Testing *(mandatory)*

The "user" here is the engineer (human or agent) writing or changing an e2e spec, and the reviewer reading one. Each story is one cluster of real, measured duplication (§1), independently shippable as one PR that touches a disjoint file set.

### User Story 1 — The wall session rig is written once (Priority: P1)

An engineer writing the next `wall-*` spec imports sign-in, grant reading, grant expiry, claim decoding and issuer lookup instead of copying ~80 lines from a sibling, as three specs have already done. A reviewer sees the rig once.

**Why this priority**: It is the largest measured duplication (§1.5), the one the code itself names as waiting for "a separate refactor", and it subsumes #2154's observation 2 (the JWT decoders). It touches only `wall-*` specs, `kiosk-session.ts`/`management-session.ts` and a new module — one project, no overlap with Stories 2-3.

**Independent Test**: On the branch, `pnpm test:e2e --project=wall --list` prints the same titles as on `develop`; the `wall` project passes on the full stack; `claimsOf`, `signInAsWallDisplay`, `storedAccessToken`, `expireStoredAccessToken`, `issuerOf` and `openFirstLayout` each have exactly one definition under `e2e/`.

**Acceptance Scenarios**:

```gherkin
Scenario: the rig is defined once (happy path)
  Given the five wall specs on develop each declare their own copies of the session helpers
  When the refactor lands
  Then each helper has exactly one definition in e2e/
  And every wall spec that used a copy imports it

Scenario: differing copies keep their differences (conflict)
  Given wall-outlives-its-session signs in with a 60 s budget via the project baseURL
  And wall-survives-a-process-death signs in with a 90 s budget via an absolute URL
  When both use the shared sign-in
  Then each still waits its own budget, navigates to its own URL and makes its own assertions

Scenario: a manually launched context can use the rig (bad request to a fixture-only design)
  Given wall-survives-a-process-death drives a persistent context it launched itself
  When it signs in and reads its grant
  Then it uses the same helpers as a test driving the default page

Scenario: storage is still per app (auth)
  Given the kiosk and wall keep their grant in localStorage (ADR-0131)
  And management-web keeps it in sessionStorage
  When a spec reads a token through the shared layer
  Then it reads the storage its app actually uses, and a mismatch fails as "no token held"
```

---

### User Story 2 — Management-console navigation is named once (Priority: P2)

An engineer opens a console section with one call that clicks the sidebar link and waits for the section's heading, instead of re-typing both locators.

**Why this priority**: 61 sites (§1.2), but a third is per-file repetition and every site is a two-line pattern; lower payoff per changed line than Story 1, and it touches 12 specs + 3 seed setups, so it is the widest contention footprint.

**Independent Test**: `pnpm test:e2e --list` unchanged; the `chromium` and `seed` projects pass; no spec outside the shared layer contains `getByRole('link', { name: /^<section>$/i })` for the seven sidebar sections.

**Acceptance Scenarios**:

```gherkin
Scenario: navigate and arrive (happy path)
  Given an operator is signed in
  When a spec asks to open the Overlays section
  Then the Overlays link is clicked and the Overlays heading is visible before the call returns

Scenario: entity links are not swallowed (conflict)
  Given camera-detail clicks a camera row link by its name
  When Story 2 lands
  Then that click is unchanged, because it is not sidebar navigation

Scenario: an unknown section is a compile error (bad request)
  Given a spec asks to open a section the console does not have
  Then the e2e type-check fails, rather than the test timing out at run time
```

---

### User Story 3 — Repeated form flows are named once (Priority: P3)

Register-a-camera, create-an-overlay-draft, create-a-layout, define-a-variable and fill-a-rule-form each exist once.

**Why this priority**: Real but smaller (`#register-camera-name`, `#layout-name`, `#variable-name`, `#rule-name`: 48 sites in 13 files; `registerCamera` and `fillRuleForm` copied twice each). The flows sit right next to the red/green assertions the spec comments explain, so this is the story most at risk of moving assertion reasoning away from its test (§1.7) — last, after Stories 1-2 have proven the convention.

**Independent Test**: As Story 2, per surface.

**Acceptance Scenarios**:

```gherkin
Scenario: a flow is one call (happy path)
  When a spec registers a camera named "E2E X" at a given URL
  Then the dialog is filled, submitted, and the call returns once the row is visible within the first-write budget

Scenario: the test still owns its assertion (conflict)
  Given in-flight-focus holds the POST and asserts focus while it is in flight
  When it uses the shared flow
  Then the focus and request-count assertions remain in the spec file, not in the shared layer
```

---

### Edge Cases

- **A helper that asserts.** `signInAsOperator` asserts the Cameras heading today; `signInToKiosk` asserts a populated picker. Helpers asserting *arrival* are already the convention; helpers asserting *the thing under test* would hide the red line. The line between them has to be written down (FR-004).
- **The `.test.mjs` exclusion.** `e2e/support/*.test.mjs` are `node:test` files excluded from Playwright (`playwright.config.ts:44-56`). A new directory must not reintroduce that collision.
- **Messages in `expect(..., 'message')`.** The copies carry different failure messages ("the wall display should be holding a grant" vs "the recovered wall display should be holding a grant"). Unifying them changes what a red run says; treat the message as part of the helper's contract, parameterised or kept per call.
- **Two apps, one role name.** Kiosk and wall are the same app on different ports (`:5174`/`:5175`); management-web is a different app. A "page" is per app, not per port.
- **Lint does not catch a dropped `await`.** `e2e/` is linted (`pnpm lint:e2e`, #2219), but `eslint.config.mjs` has no type-aware rules, so `no-floating-promises` is off. A helper whose `expect` is not awaited passes silently. This is why Q3(c)'s deliberate-break run exists. #2154's remaining questions are not touched.

## 3. Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each helper consolidated by this feature MUST have exactly one definition under `e2e/`, and every spec that previously carried a copy MUST use it.
- **FR-002**: The set of tests Playwright discovers MUST be unchanged — identical `pnpm test:e2e --list` output (project, file, title) before and after each story.
- **FR-003**: Every consolidated helper MUST accept the page (or context) it drives as an argument, so specs that launch their own contexts (§1.7: 14 of 30) can use it.
- **FR-004**: Shared helpers MAY assert *arrival* (a heading, a populated list, a stored grant) and MUST NOT assert the behaviour a spec exists to test. Every `expect` a spec's comments identify as its red line, counterfactual or measurement stays in the spec file.
- **FR-005**: Where copies differ (URL, timeout budget, extra arrival assertion, failure message — §1.6), the shared helper MUST preserve each caller's variant; no caller's budget or assertion set may change.
- **FR-006**: The real Keycloak sign-in MUST still run per test as it does today (ADR-0108). Sharing authenticated state across tests is out of scope.
- **FR-007**: New shared modules MUST NOT match any project's `testMatch`, and MUST NOT be named `*.spec.*`, `*.test.*`, `*.setup.ts` or `*.teardown.ts`.
- **FR-008**: Shared e2e code MUST be plain exported functions taking the `Page` or `BrowserContext` they drive, one module per surface in `e2e/support/`. No classes, no `test.extend` fixtures, and no change to the `test` specs import (Q1 = C; ADR-0162 §1-2).
- **FR-009**: Only code with a second caller in a different file is extracted. Repetition within one file is left alone, and specs with no cross-file duplication are not touched. Each user story ships as its own PR (Q2 = (a); ADR-0162 §3).
- **FR-010**: Each story's PR MUST carry, for every test in the files it touches:
  - identical `pnpm test:e2e --list` output before and after;
  - the affected projects green on the full stack before and after;
  - an unchanged ordered assertion inventory, captured by the repository's `expect`-step reporter;
  - for each extracted helper that asserts arrival, one deliberate-break run observed red and then reverted.

  (Q3 = (b) + (c); ADR-0162 §6.)

### Key Entities

- **Surface helper module**: the one place a surface's (an app section's, or a session's) locators and multi-step actions are named. Takes a page; returns nothing or a value read from the page.
- **Session rig**: sign-in, grant reading/expiry, claim decoding — per app, because storage differs (kiosk/wall `localStorage`, management `sessionStorage`).

## 4. Questions put to the Phase 1 gate, and their answers

**Resolved 2026-09-29:**

- **Q1 = C.** Recorded in ADR-0162.
- **Q2 = (a).** Duplicated clusters only, in `support/`, one PR per story.
- **Q3 = (b) + (c).** One correction to (b) as first written: CI's JSON report **cannot** supply the assertion inventory. Playwright's JSON reporter keeps only `test.step` entries and drops every `expect` step (`_serializeTestResult` filters on `category === "test.step"`, checked in the pinned `playwright@1.63.0`). The inventory therefore comes from a small repository reporter that records `expect` steps (plan §4.1).

The analysis below is kept as the record of why.

### Q1 — Which pattern?

| Option | Shape | Trade-off in one line |
|---|---|---|
| **A. Class-based Page Objects** | `new OverlaysPage(page)` with locator fields and action methods, in `e2e/pages/` | Familiar and works with any page/context, but invites moving assertions into classes and would touch all 30 specs to be consistent. |
| **B. Fixtures (`test.extend`), exposing helpers or POs** | a `test` exported from one module; specs take `{ overlaysPage }` as parameters | Composes well and is where Playwright's docs are heading, but binds to the test-scoped `page` (14 specs drive other contexts), changes every spec's `test` import, and one fixtures module becomes the hottest ADR-0109 contention file in `e2e/`. Its main benefit — setup/teardown pairing — is already served by the `seed`/`cleanup` projects. |
| **C. Per-surface function modules extending `support/`** (recommended) | `wall-session.ts`, `management-navigation.ts`, … exporting plain functions that take a `Page`, like `signInAsOperator` and `signInToKiosk` already do | Smallest change, mirrors the existing convention, one file per surface keeps contention low; but nothing stops a future spec re-typing a locator, so it relies on review. |
| **D. C now, A later per surface if earned** | as C; promote a surface to a class once it carries enough locators to warrant one | Defers the choice to evidence; risks two conventions coexisting indefinitely. |

**Recommendation: C.** The measured duplication (§1) is session rigs, a navigate-and-arrive pair, and a handful of form flows — functions, not object graphs. The suite already has this convention (`signInAsOperator`, `signInToKiosk`, `openFirstLayout`, `readManagementAccessToken`), so C is "use what exists", which is what ADR-0036 asks and what the deferral comments in §1.5 were waiting for. It reaches manually launched contexts without adaptation (FR-003) and keeps each surface in its own file, which is what limits ADR-0109 contention. B's strengths answer a problem the project graph already solves, and its cost (a single hot module, a changed `test` import in every file) is exactly the contention that caused the duplication. A is defensible, but its value grows with locator count per surface, and no surface here has a large locator set that crosses files — the only such set measured (§1.3) lives in one file.

### Q2 — Scope and location

- **(a) Duplicated clusters only (recommended)** — Stories 1-3 as ordered, each its own PR on a disjoint file set; specs with no cross-file duplication (e.g. `kiosk-shows-a-label-over-video.spec.ts`'s measurement helpers, `click-to-first-frame.spec.ts`'s clock) are not touched. Modules stay in `e2e/support/`, named per surface.
- **(b) Every spec file** — a uniform convention across all 30; roughly 30 files churned, and every in-flight e2e branch conflicts.
- **(c) As (a), but new modules in `e2e/pages/`** and `support/` kept for seed/teardown/session — a clearer split, at the cost of moving the existing session helpers or accepting two homes for "things specs import".

On sequencing: the issue asks for "no window where both conventions are half-applied". With option C there is no new convention to half-apply — each PR finishes one cluster — so per-story PRs are safe. A single big-bang PR across 30 files is the option most likely to collide with parallel work (ADR-0109) and I would not recommend it.

### Q3 — The characterisation net

CLAUDE.md's rule is that covering tests pass **unmodified**. Here the covering tests *are* what is modified. Candidate evidence, cumulative:

- **(a)** `pnpm test:e2e --list` byte-identical before/after (the identity CI's shard guard already relies on) **plus** the affected projects green on the full stack before and after.
- **(b)** (a) plus **the `expect` inventory per test unchanged**: the same number of assertions, in the same order, with the same matchers and messages — checked from the runtime `expect` steps, not by grepping the source. *(As first written, this proposed reading the steps from CI's JSON report. That report does not contain them — see the resolution above.)*
- **(c)** (b) plus **one counterfactual per extracted helper**: break the UI (or the helper's locator) once and observe the consuming test go red, proving the extraction did not turn an assertion into a no-op.

**Recommendation: (b), with (c) for helpers that assert arrival** (sign-in, navigate). (a) alone would pass a refactor that silently dropped an `expect` into a helper that no longer awaits it — the exact failure mode an extraction of assertion-bearing helpers invites.

### Q4 — ADR authorship (resolved)

Written before Phase 2 as ADR-0162. ADR-0109 needed no amendment: the new modules live under `e2e/support/`, which its existing contention glob already covers.

## 5. Success Criteria *(mandatory)*

- **SC-001**: After Story 1, each of the six wall-rig helpers in §1.5 has exactly one definition under `e2e/` (from 2-4 today).
- **SC-002**: After Story 2, zero spec files outside the shared layer contain a sidebar-nav link locator for the seven sections (from 61 sites in 15 files).
- **SC-003**: For every story, the discovered test list is identical before and after, and the affected Playwright projects pass on the full stack.
- **SC-004**: For every story, the ordered per-test assertion inventory of every affected test is unchanged (noise masked only where two baseline runs on `develop` already disagree), and every extracted arrival-asserting helper has one observed deliberate-break red.
- **SC-005**: No story's diff touches a file outside its own cluster. US1 is file-disjoint from US2 and US3. US2 and US3 share spec files and so run in sequence (plan §6).

## 6. Latency budget impact

**N/A.** Test-code refactor only; no product code, no leg of the §IV path is touched. `kiosk-shows-a-label-over-video.spec.ts` and `click-to-first-frame.spec.ts` measure latency and are out of scope under Q2(a); under Q2(b) their measurement helpers MUST stay in the spec file (FR-004).

## 7. Out of scope

- Sharing authenticated state across tests (`storageState`) — a change to what ADR-0108's e2e proves.
- #2154's open lint questions, and adding type-aware lint rules to `eslint.config.mjs`.
- Changes to seed/teardown projects, `playwright.config.ts` projects, or CI sharding.
- Any product (`apps/*`) change, including adding test ids.

## Assumptions

- The full stack (`aspire run`) is available to run the affected projects before and after each story; "green" means green on that stack, twice where a first run follows machine churn.
- The existing arrival assertions in `signInAsOperator` / `signInToKiosk` define the accepted precedent for helpers that assert (FR-004).
- Spec number 288 is free: checked across every ref this clone has; an unpublished branch elsewhere could still claim it.
