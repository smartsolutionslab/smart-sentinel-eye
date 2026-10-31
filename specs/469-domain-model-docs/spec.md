# Spec 469 — The domain model is written down, per bounded context, and the README tells the truth

**Feature Branch**: `469-domain-model-docs`
**Created**: 2026-10-31
**Status**: Draft
**Issue:** #3199 (feature-level, on Project #13). Supervised lane, full Spec-Kit cycle (maintainer's choice).
**ADRs:** ADR-0016 (the bounded contexts), ADR-0040 (domain vs integration events), ADR-0044
(Published Language + ACL between contexts), ADR-0045 (rich domain model), ADR-0092 (per-aggregate
Domain folder), ADR-0073 (integration event versioning), ADR-0037 (the phased process).
**No new ADR.** This documents decisions already taken; it makes none (ADR-0144: the lane may not).
**Latency budget:** none — documentation only; no code is touched.
**Phase 4a colour:** not applicable (no behaviour). The verification is a source-traceability check
(SC-002), not a test.

## Problem

**There is no domain-model document.** The domain is described only in pieces: which contexts exist
(ADR-0016), how they talk (ADR-0044), the event split (ADR-0040), the folder layout (ADR-0092). A
reader who wants to know what a `Layout`, an `OverlayRevision` or a `Rule` *is*, what invariants it
holds, which events it raises, and which contexts consume them, must read the code of nine contexts.

Measured on `develop` (2026-10-31): nine bounded contexts have a `Domain` layer — AuditObservability,
Automation, CameraCatalog, EventIngestion, Identity, LayoutComposition, OverlayDesigner,
StreamDistribution, SystemVariables — holding roughly **240** public/internal types (aggregates,
value objects, events, enums, interfaces; a first-pass `grep`, the script in SC-002 is authoritative).
Two more `src/` projects named in ADR-0016's world have **no** Domain layer: ApiGateway and
ScenarioSimulator.

**The README is stale, and in places wrong.** Verified against the repo and the ADRs:

- *Status* says specs `001`–`004` "shipped"; `specs/` holds 461 directories and reaches 467.
- *"frame-accurate sync via PTP"* contradicts ADR-0128/ADR-0129 (the wall is not frame-synced; PTP is
  not the presentation-buffer mechanism; inter-display sync is unbuilt and out of scope).
- *"PostgreSQL (+ Marten for event-sourced contexts)"* — Marten is permitted and **unused** (ADR-0130).
- *"Aspire dashboard + Grafana stack"* — ADR-0118 abandoned the dual-sink plan: one sink per
  environment.
- The capability list predates the rules/automation context, the audit context, the webhook
  integration registry and the scenario simulator.

(The first four were checked now; the README is 505 lines and further drift is likely — FR-009.)

## Scope

**In:**
- `docs/domain/README.md`: a Mermaid context map built from the relationships in ADR-0044 and the
  integration events actually in `Shared.Contracts`; a glossary of the ubiquitous language; one link
  per context page; a short note on ApiGateway and ScenarioSimulator (no Domain layer).
- `docs/domain/<context>.md` for each of the nine contexts above.
- A refresh of `README.md`: status, capabilities, architecture table, and a pointer to `docs/domain/`.
- A repeatable script that proves each Domain type is accounted for (SC-002) and that the page links
  resolve (SC-004).

**Out (explicitly):**
- Any change to code, tests, ADRs or the constitution.
- Documenting Application / Infrastructure / Api layers, HTTP contracts, or the frontend. (The
  *integration events* a context publishes and consumes are in: they are domain language.)
- Shared.Kernel, Shared.CQRS, ServiceDefaults, MigrationRunner, AppHost internals.
- Reconciling disagreements between ADRs and code. Where they disagree, the page records both and
  cites each (FR-008); it does not pick, and it does not edit the ADR.
- The rest of the README beyond the claims in FR-009 (install and quickstart steps are not re-verified
  by this spec).

## User Scenarios & Testing

### User Story 1 (P1) — A new engineer learns what a bounded context contains

As an engineer joining the project, I want one page per bounded context that names its aggregates,
value objects, events and invariants, so I can understand the model without reading every file.

**Why P1:** it is the missing artifact and the whole of the issue's first half.

**Independent test:** pick any aggregate in the code; its page names it, states at least one invariant
it enforces, and cites the file.

**Acceptance scenarios**

```gherkin
Scenario: an aggregate is findable from the docs
  Given an aggregate root in src/<Context>/Domain
  When I open docs/domain/<context>.md
  Then it names the aggregate, its identifier type and its lifecycle states
  And it states the invariants the aggregate enforces, each with a file reference

Scenario: every domain type is accounted for
  Given the Domain layer of one of the nine contexts
  When the traceability script runs
  Then each public/internal type is either named on the page or listed under "Omitted" with a reason

Scenario: events are tied to their consumers
  Given a domain or integration event raised by a context
  When I read its page
  Then I can see whether it stays in-process (domain event) or crosses contexts (integration event)
  And, for an integration event, which contexts consume it
```

### User Story 2 (P2) — A reader sees how the contexts relate

As an architect or reviewer, I want a context map and a shared glossary, so I can see who publishes to
whom and what a term means across contexts.

**Why P2:** it depends on the pages existing (P1), and is the part that ties them together.

**Independent test:** every arrow on the map corresponds to an integration event with a named
publisher and at least one named consumer in the code.

**Acceptance scenarios**

```gherkin
Scenario: the context map matches the code
  Given the integration events in Shared.Contracts
  When I read docs/domain/README.md
  Then each arrow on the map is backed by an event whose publisher and consumer I can find in src
  And no context-to-context project reference is implied (ADR-0044)

Scenario: the glossary resolves a term
  Given a domain term used in two contexts (e.g. "revision", "fab", "wall")
  When I look it up in the glossary
  Then I get one definition per context where the meaning differs, each pointing to its page

Scenario: contexts without a Domain layer are not silently missing
  When I read the overview
  Then ApiGateway and ScenarioSimulator each have a short note saying what they are and why they have no page
```

### User Story 3 (P3) — The README tells a visitor the truth

As someone landing on the repository, I want the README's status, capabilities and architecture table
to match the repository, with a pointer to the domain documentation.

**Why P3:** smaller than P1/P2 and independent of them, but it is what most visitors read first.

**Independent test:** each factual claim in the refreshed README's status, capabilities and
architecture sections has a source (a spec, an ADR, or a file).

**Acceptance scenarios**

```gherkin
Scenario: the four known-wrong claims are corrected
  Given the README claims "frame-accurate sync via PTP", "Marten for event-sourced contexts", "Grafana stack", and "specs 001-004 shipped"
  When the README is refreshed
  Then each is corrected to what ADR-0128/0129, ADR-0130, ADR-0118 and specs/ say
  And none of the corrections is a new decision

Scenario: the README points at the domain docs
  When I read the README's architecture section
  Then it links to docs/domain/README.md
```

### Edge Cases

- A type exists in a Domain folder but is an implementation detail (an internal helper, an
  `IValueObject` plumbing type): it is listed under "Omitted" with that reason, not described as domain.
- An ADR and the code disagree (e.g. ADR-0044 says "9 bounded contexts" while `src/` has more
  projects): the page records both with citations (FR-008).
- A context's Domain layer is partly event-sourced or partly planned: only what exists in code is
  documented; planned work is not described as present (the README's Marten claim is the cautionary case).
- A type is renamed after this lands: the traceability script fails on the next run, which is the
  intended signal that the page is stale.
- Mermaid syntax GitHub does not render: caught by SC-003, not by eye.

## Requirements

### Functional Requirements

- **FR-001**: `docs/domain/README.md` MUST contain a Mermaid context map, a glossary, a link to each
  of the nine context pages, and notes for ApiGateway and ScenarioSimulator.
- **FR-002**: Each of the nine context pages MUST list its aggregates (with identifier type and
  lifecycle/states), value objects, domain events, and the integration events it publishes and consumes.
- **FR-003**: Each aggregate MUST have its invariants stated, each citing the source file (and line or
  method) that enforces it.
- **FR-004**: Every public/internal type in a context's Domain folder MUST appear on its page or in an
  "Omitted" list with a one-line reason.
- **FR-005**: Every arrow on the context map MUST correspond to an integration event whose publisher
  and consumer are named and exist in `src/`.
- **FR-006**: The glossary MUST give a separate definition per context where a term's meaning differs.
- **FR-007**: A traceability script (committed, runnable on a clean checkout) MUST verify FR-004 and
  that every file path cited on a page exists.
- **FR-008**: Where an ADR and the code disagree, the page MUST state both, cite both, and take no side.
- **FR-009**: `README.md` MUST be corrected for at least the four known-wrong claims, and every other
  factual claim in its status, "What it does" and architecture sections MUST be re-checked against the
  repository, with any further error fixed or recorded in the PR.
- **FR-010**: `README.md` MUST link to `docs/domain/README.md`.
- **FR-011**: No file under `src/`, `tests/`, `apps/`, `docs/adr/` or `.specify/memory/` is modified.

### Key Entities

- **Context page**: one `docs/domain/<context>.md`; the unit of FR-002–FR-004.
- **Context map**: the Mermaid diagram in `docs/domain/README.md`; the unit of FR-005.
- **Omitted list**: a per-page list of Domain types deliberately not described, each with a reason.
- **Traceability script**: the check behind FR-004 and FR-007.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All nine context pages exist and are linked from the overview and from the README.
- **SC-002**: The traceability script reports 0 unaccounted Domain types across the nine contexts and
  0 cited file paths that do not exist, and is shown failing when a type is deleted from a page
  (counterfactual).
- **SC-003**: Every Mermaid block renders (validated with a Mermaid parser, and viewed once on GitHub).
- **SC-004**: Every relative link in `docs/domain/` and in the README's new section resolves.
- **SC-005**: Each of the four known-wrong README claims is corrected, and a PR note lists every
  further README claim changed and its source.
- **SC-006**: `git diff --stat` shows changes only under `docs/domain/`, `README.md`, `specs/469-*` and
  the script's location (FR-011).

## Assumptions

- "Domain type" means a `public` or `internal` class, record, struct, interface or enum declared under
  `src/<Context>/Domain/` (the grep above); the script defines it precisely at plan time.
- Documentation is English, like the rest of the repository.
- `docs/domain/` is a new directory and does not collide with an existing path (checked: `docs/`
  holds `adr`, `design`, `runbooks` and two loose files).
- The existing `docs/design/scenario-simulator-m2.md` is linked from the ScenarioSimulator note, not
  duplicated.
- Source-reading is delegated to read-only agents per context, one context per agent, whose output is
  reviewed against the script; the pages are written by the orchestrator or by agents given disjoint
  files, never by two writers on one file.
