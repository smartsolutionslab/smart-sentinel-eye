# Tasks 469 — The domain model is written down, per bounded context, and the README tells the truth

Spec: [spec.md](spec.md) · Plan: [plan.md](plan.md) · Issue #3199 (feature-level issue, on Project #13).
Engineer: **docs + one Node script** (no backend, no frontend app code). Phase 4a colour: the guard is new
behaviour → **red first (T001–T002)**; the pages are documentation, verified by the guard and a sample
check, not by a test. Enforcement: **R5-B**, the guard runs in `test:guards` (maintainer's choice).

One PR. Guard commands: `node scripts/domain-docs-trace.mjs [--context <Name>] [--root <dir>]`;
`node --test scripts/domain-docs-trace.test.mjs`. Commits: no `Co-Authored-By` (ADR-0086).

## Phase 1 — Foundational: the guard (blocks every page; FR-004, FR-007, SC-002)

- [x] **T001** (test-writer) Create `scripts/domain-docs-trace.test.mjs` and fixture trees (built in a temp dir by the test itself, so no `scripts/fixtures/domain-docs/` is committed) under
  `scripts/fixtures/domain-docs/` **before** the script exists. Fixture cases (a tiny fake `src/Ctx/Domain/`
  plus `docs/domain/ctx.md`): clean tree passes; a type missing from the page fails and the message names the
  type and the page; an `Omitted` entry with no reason fails; a cited path that does not exist fails; a cited
  `path:line` past end-of-file fails; a type named only inside a fenced code block still counts as **not**
  mentioned (R2 limit stated in the test name); `obj/` and `bin/` files are ignored. **Fixture cases only**,
  no real-repo case yet (it would be red until the pages exist, breaking bisect). Run
  `node --test scripts/domain-docs-trace.test.mjs`; **quote the red output verbatim** (expected: script not
  found). Test-only commit `test(469): … (red)`.
- [x] **T002** Create `scripts/domain-docs-trace.mjs` per plan R1/R2: declaration regex for
  `public|internal` class/record/record struct/struct/interface/enum under `src/<Context>/Domain/**/*.cs`;
  backticked-name check against `docs/domain/<kebab-context>.md`; `Omitted` entries need a reason; cited
  `` `path` `` and `` `path:line` `` resolved; prints per-context counts and the exact type list with
  `--verbose`; exit 1 on any violation, message = *type, context, page*. T001 passes **unmodified**. Depends on T001.
- [x] **T003** **Counterfactual (SC-002).** Break the script three ways, one at a time: ignore `obj/`
  exclusion; stop requiring a reason on `Omitted`; resolve paths without checking the file exists. Run
  T001 each time; **quote each failure**; restore (`git diff` empty). Not committed. Depends on T002.
- [x] **T004** Run the guard with `--verbose` against the real `src/` with **no pages yet**: it must report
  every type of all nine contexts as unaccounted. Compare the per-context totals with plan R1 (22, 27, 13, 50,
  14, 51, 24, 21, 18 = 240); explain any difference in the commit message (the regex, not the plan, is then
  authoritative). Writes the exact inventory to the scratchpad for T006–T010. Depends on T002.

## Phase 2 — User story 1 (P1): one page per context (FR-002, FR-003, FR-004, FR-008)

**Goal:** every aggregate, value object, event and invariant of the nine contexts is findable and cited.
**Independent test:** `node scripts/domain-docs-trace.mjs --context <Name>` exits 0 for that context.

- [x] **T005 [US1]** (orchestrator) Dispatch the five source-reading agents **in one message** (plan R4),
  each with: the page template (plan §Page template), the R1/R2 rules, the T004 inventory for its contexts,
  and the instruction to document only what is in code, cite `path:line` for every invariant, and put
  anything unestablished under **Questions**. Disjoint output files, **no commits** (one branch, one index).
  Depends on T004.
- [x] **T006 [P] [US1]** Agent A writes `docs/domain/event-ingestion.md` (50 types).
- [x] **T007 [P] [US1]** Agent B writes `docs/domain/layout-composition.md` (51 types).
- [x] **T008 [P] [US1]** Agent C writes `docs/domain/automation.md` (27) and `docs/domain/identity.md` (14).
- [x] **T009 [P] [US1]** Agent D writes `docs/domain/audit-observability.md` (22),
  `docs/domain/stream-distribution.md` (21), `docs/domain/camera-catalog.md` (13).
- [x] **T010 [P] [US1]** Agent E writes `docs/domain/overlay-designer.md` (24) and
  `docs/domain/system-variables.md` (18).
- [x] **T011 [US1]** (orchestrator) Review **each** page: run the guard with `--context`; then read **three**
  cited invariants per page at the cited line and confirm the page's claim. A page that fails a sample is
  **rejected whole** and re-dispatched, not patched. Resolve **Questions** by reading the code or leave them
  listed. One commit per page: `docs(469): the <context> domain model`. Depends on T006–T010.

## Phase 3 — User story 2 (P2): the context map and glossary (FR-001, FR-005, FR-006)

**Goal:** a reader sees who publishes to whom and what a term means per context.
**Independent test:** every arrow on the map has a named publisher and consumer file.

- [x] **T012 [US2]** (orchestrator) Create `docs/domain/README.md`: purpose, link to each of the nine pages,
  a note each for **ApiGateway** and **ScenarioSimulator** (what they are, why no page; link
  `docs/design/scenario-simulator-m2.md`), and a line saying decision 016's nine contexts are exactly the
  nine documented (no FR-008 case; corrected after checking). Depends on T011.
- [x] **T013 [US2]** (orchestrator) Build the edge table from the pages' *published/consumed* sections (plan
  R3): one row per integration event with publisher, consumers, and the handler/publish file for each; resolve
  the `Automation` / `SystemVariableValueRequestedV1` direction by reading. Render it as a Mermaid
  `flowchart` in `docs/domain/README.md` plus the table below it. Depends on T012.
- [x] **T014 [US2]** Glossary in `docs/domain/README.md`: terms that appear in two or more contexts
  (revision, fab, wall, layout, overlay, rule, …) with one definition per context where the meaning differs,
  each linking its page. Terms are taken from the pages, not invented. Depends on T013.
- [x] **T015 [US2]** Parse every Mermaid block with `npx -y @mermaid-js/mermaid-cli` (not added as a
  dependency) and record the result; open the overview once on GitHub in the PR (SC-003). Depends on T014.

## Phase 4 — User story 3 (P3): the README tells the truth (FR-009, FR-010)

**Goal:** every factual claim in the README's status, capabilities and architecture sections has a source.
**Independent test:** the claim table has no row marked *unverified*.

- [x] **T016 [P] [US3]** Build the **claim table** for `README.md` (every factual sentence in Status, "What it
  does", "Architecture at a glance"): claim · verdict (*verified* / *wrong* / *unverifiable*) · source (spec,
  ADR, or file). The four known-wrong claims are rows 1–4 (specs 001–004; PTP frame-accurate, ADR-0128/0129;
  Marten, ADR-0130; Grafana, ADR-0118). Walk the rest of the 505 lines for those three sections only
  (quickstart is out of scope). Written to the scratchpad, summarised in the PR body (SC-005).
- [x] **T017 [US3]** Edit `README.md`: correct *wrong* rows, remove or hedge *unverifiable* ones, refresh the
  status line from `specs/` and the contexts list from `src/`, and link `docs/domain/README.md` from the
  architecture section (FR-010). No new decision is introduced; where a correction needs an ADR citation, cite
  it. Depends on T016; the link depends on T012.

## Phase 5 — Wire the guard into CI (R5-B) and close

- [x] **T018** Add the **real-repo case** to `scripts/domain-docs-trace.test.mjs` (runs the guard over the
  live tree and fails with the guard's own message plus "this test reads the live tree: it fails when a Domain
  type and its page disagree"). This is the only change that puts the guard into `pnpm test:guards`; no
  `package.json` or `ci.yml` change (existing `scripts/**/*.test.mjs` glob). Run `pnpm test:guards` locally.
  Depends on T011, T017.
- [x] **T019** Whole-repo run: `node scripts/domain-docs-trace.mjs --verbose` → 0 unaccounted types, 0 dead
  paths, 0 dead lines across all nine contexts (SC-002). Relative-link check over `docs/domain/*.md` and the
  README's new section (SC-004). `git diff --stat origin/develop` shows only `docs/domain/`, `README.md`,
  `specs/469-*`, `scripts/domain-docs-trace*`, `scripts/fixtures/domain-docs/`, `.specify/feature.json` (SC-006,
  FR-011). Depends on T018.
- [x] **T020** (verify, phase 5) Write `specs/469-domain-model-docs/verification.md`: the T019 outputs as
  observed, the three sampled invariants per page, the Mermaid result, and the README claim table summary.
  Depends on T019.
- [x] **T021** (QA, phase 6) `/code-review` on the docs + script diff; address or accept each finding in
  writing. `/security-review` is not needed (no auth, secrets or boundaries). Depends on T020.
- [ ] **T022** (orchestrator) PR to `develop` (`gh pr create --base develop`): body quotes T001's red output,
  T003's three failures, T019's results and the README claim-table summary. Add a note that **in-flight PRs
  that add a Domain type will go red on rebase** (R5-B cost). Park; do not wait on CI. **Immediately before
  merging, rebase on `develop` and re-run the guard** (a Domain type may have landed meanwhile). Close #3199
  with a closing keyword. Depends on T021.

## Dependencies and parallelism

T001→T002→T003→T004 strict (T001 must be seen red first). T006–T010 are the only true parallelism (five
agents, disjoint files); T011 serialises the review and the commits. T016 can run any time after T004 and is
independent of the pages. T018 must come **after** T011 and T017 or the real-repo case is red on this branch.

## Explicitly not tasks

No code under `src/`, `tests/`, `apps/`. No ADR or constitution change. No `package.json`/`ci.yml` edit.
No documentation of Application/Infrastructure/Api layers, HTTP contracts or the frontend. No README
quickstart re-verification.
