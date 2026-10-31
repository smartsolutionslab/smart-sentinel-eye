# Implementation Plan: The domain model is written down, per bounded context, and the README tells the truth

**Branch**: `469-domain-model-docs` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/469-domain-model-docs/spec.md`

## Summary

Write `docs/domain/` (an overview with a Mermaid context map and glossary, plus nine context pages),
refresh `README.md`, and add one repeatable Node guard, `scripts/domain-docs-trace.mjs`, that proves
every Domain type is accounted for and every cited path exists. Documentation only; no production code.
Source is read by read-only agents, one group of contexts per agent; the orchestrator reviews against the
script and a sample of cited lines.

## Technical Context

**Language/Version**: Markdown + Mermaid (GitHub-rendered); Node 20+ ESM for the guard (`.mjs`, like every
other file in `scripts/`)
**Primary Dependencies**: none new; the guard uses `node:fs`, `node:path`, `node:test`
**Storage**: N/A
**Testing**: `node --test` via the existing `pnpm test:guards` glob (`scripts/**/*.test.mjs`)
**Target Platform**: GitHub markdown rendering; developer machines (Windows/Linux)
**Project Type**: documentation + one repo script
**Performance Goals**: guard runs over ~240 files in well under a second
**Constraints**: FR-011: no change under `src/`, `tests/`, `apps/`, `docs/adr/`, `.specify/memory/`
**Scale/Scope**: 9 context pages (~240 types), 1 overview, 1 README, 1 script + test

## Constitution Check

*GATE: passes before research; re-checked after design.*

| Gate | Result |
|---|---|
| ADR-0144: the lane may not write an ADR or amend the constitution | Complies: nothing under `docs/adr/` or `.specify/memory/` changes (FR-011); disagreements are recorded, not resolved (FR-008). |
| ADR-0036 smallest change / no speculative generality | The guard is the one new mechanism; it exists because "every claim traceable" cannot be checked by eye over 240 types. No generator, no site, no framework. |
| §Testing | Docs-only; the new script is behaviour, so its test is written first and seen failing (T-red), then proved by counterfactual. |
| "Enforce rules, advise preferences" (maintainer's standing preference) | Whether the guard runs against the real repo in CI is **not decided here**: R5 offers the options with costs for the gate. |
| §IV latency | Not touched. |

Post-design re-check: unchanged.

## Research (decisions that shaped the plan)

### R1 — What counts as a "domain type" (FR-004)
A `public` or `internal` declaration of `class`, `record`, `record struct`, `struct`, `interface` or
`enum`, anywhere under `src/<Context>/Domain/**/*.cs`, excluding `obj/` and `bin/`. First-pass count on
`develop`: AuditObservability 22, Automation 27, CameraCatalog 13, EventIngestion 50, Identity 14,
LayoutComposition 51, OverlayDesigner 24, StreamDistribution 21, SystemVariables 18 = **240**. The guard
is the authority and will print the exact list; declarations are matched by regex, so a type the regex
misses (e.g. a generic constraint line break) is a known limit, covered by the counterfactual and by
comparing against `grep -c` once.

### R2 — How "accounted for" is checked
Each page names every type of its context as a backticked `` `TypeName` `` token, either in the body or in
an **Omitted** list with a reason (the guard requires the reason text after the name on that list). It also
checks every cited path (`` `src/…/File.cs` `` or `` `src/…/File.cs:42` ``) exists, and a cited line is
within the file. **Limit, stated plainly:** this proves a type is *mentioned* and a citation *resolves*; it
cannot prove the description is *right*. That is the reviewer's job, helped by R4's sample check.

### R3 — The context map is derived from code, not from the ADR
Integration events are the `V<N>` records under `src/Shared.Contracts/<Publisher>/` (29 files).
Consumer candidates come from other contexts that reference the type. A first scan shows the edges are
real (e.g. `OverlayRevisionPublishedV3` → LayoutComposition, SystemVariables, AuditObservability;
`FabEventIngestedV1` → Automation, AuditObservability) but a reference is not a consumption. One case already
needs reading: `Automation` references `SystemVariableValueRequestedV1`, whose contract folder is
SystemVariables: Automation may be the *publisher*. Each edge is therefore classified by reading the handler
(`Handle(…V1 message …)`) or publish call, and cited. Contexts publishing nothing to Contracts
(Automation has no folder) are noted, not hidden.

### R4 — Who writes what
Read-only source reading is parallelisable and independent per context. Five agents, disjoint output files,
no commits (one branch, one index):

| Agent | Contexts (Domain types) |
|---|---|
| A | EventIngestion (50) |
| B | LayoutComposition (51) |
| C | Automation (27), Identity (14) |
| D | AuditObservability (22), StreamDistribution (21), CameraCatalog (13) |
| E | OverlayDesigner (24), SystemVariables (18) |

Each gets the page template (below), the R1/R2 rules, and the instruction to document only what is in code
and to put anything uncertain in a "Questions" list rather than assert it. The orchestrator writes the
overview, glossary, context map and README, runs the guard, and **samples 3 cited invariants per page**
by reading the cited line. A page that fails a sample is rejected whole, not patched.

### R5 — Decision for the gate: should the guard run against the real repo in CI?
| Option | Behaviour | Cost |
|---|---|---|
| **A. Advisory (default)** | Script + fixture tests are in `test:guards`; the real-repo check is a documented command, not a CI step. | Pages can drift silently; nothing fails. |
| B. In `test:guards` | A PR that adds/renames a Domain type without touching its page fails the frontend-adjacent guard job. | Every Domain PR now owes a docs edit; the guard job needs the `src/` tree and a .NET-only PR would trip a JS job. |
| C. Own path-filtered CI job | Same as B but only on `src/**/Domain/**` changes. | A new workflow to maintain (the `ci.yml` filters have bitten before). |

Default **A**; the maintainer picks at this gate. The script supports all three unchanged (exit code 1 on
violations); only the wiring differs.

### R6 — The README is corrected claim by claim
Known wrong (spec): status (specs 001–004), "frame-accurate sync via PTP" (ADR-0128/0129), Marten
(ADR-0130), Grafana stack (ADR-0118). The remaining 500 lines are walked once: every factual sentence in
Status / What it does / Architecture is tagged *verified (source)*, *changed*, or *unverifiable → removed or
hedged*. The table of changes goes into the PR body (SC-005). Quickstart steps are out of scope (spec).

## Page template (every `docs/domain/<context>.md`)

1. **Purpose** — two sentences, what the context owns and does not own.
2. **Aggregates** — per root: identifier type, lifecycle states and transitions (Mermaid `stateDiagram-v2`
   only where there are ≥3 states), invariants (each with `path:line`).
3. **Value objects** — table: name, wraps, rule enforced (cited).
4. **Domain events** — name, raised by, when; in-process only (ADR-0040).
5. **Integration events** — *published* and *consumed*, each with the contract file and the handler/publisher
   file (R3).
6. **Repositories & services** — the Domain-layer interfaces only.
7. **Omitted** — types deliberately not described, one-line reason each (R2).
8. **Questions** — anything the reader could not establish from code; empty is allowed.

## Project Structure

```text
docs/domain/
├── README.md                 # NEW  context map (Mermaid), glossary, links, ApiGateway + ScenarioSimulator notes
├── audit-observability.md    # NEW  one per context, kebab-case
├── automation.md  camera-catalog.md  event-ingestion.md  identity.md
├── layout-composition.md  overlay-designer.md  stream-distribution.md  system-variables.md
README.md                     # EDIT  status, capabilities, architecture table, pointer to docs/domain
scripts/
├── domain-docs-trace.mjs     # NEW  the guard (R1/R2); exit 1 on a violation
└── domain-docs-trace.test.mjs# NEW  fixture tests, written first, shown red
scripts/fixtures/domain-docs/ # NEW  tiny fake src/ + docs/ trees the test runs against
```

## Delivery: one PR, commits in this order

1. `test(469)`: guard test + fixtures, **red** (script absent). 2. `docs(469)`: guard, green + counterfactual.
3. `docs(469)`: overview skeleton. 4. one commit per context page (reviewed, sample-checked). 5. `docs(469)`:
context map + glossary (needs the pages: edges are classified in the pages). 6. `docs(469)`: README refresh.
7. Guard run over the real repo, output in the PR body. Phases 5 (verify) and 6 (QA) still run: the QA is the
sample check and `/code-review` on the docs diff.

## Verification

- `node scripts/domain-docs-trace.mjs` → 0 unaccounted types, 0 dead paths, 0 dead lines (SC-002).
- Counterfactual: delete one type from a page, then cite a path that does not exist; the guard fails both ways.
- Mermaid: every block parsed with `@mermaid-js/mermaid-cli` run once via `npx` (not a dependency), plus one
  look on GitHub in the PR (SC-003).
- Links: a small check of relative links in `docs/domain/*.md` and the README section (SC-004).
- `git diff --stat` limited to the paths in SC-006.
- `prettier --check` on the new markdown if the repo formats it (the `format:check` glob covers `apps/` and
  `e2e/` only; `docs/` is outside it, so this is advisory).

## Risks

- **A page that sounds right and is wrong.** The guard cannot catch it; R4's sampling and citing every
  invariant to a line are the defence. A sample failure rejects the page.
- **Agent drift between pages** (terminology, section order): the template and one shared glossary pass at
  the end.
- **ADR/code disagreement** (ADR-0044's "9 contexts"): recorded with both citations, not resolved (FR-008).
- **Drift after merge** is real under R5-A; the guard is the cheap way to find it, run on demand.
