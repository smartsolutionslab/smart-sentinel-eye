# Tasks 322: One variable, one timeline

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2502

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2502 is the tracking issue — no new feature issue.
- **Engineers:** `test-writer` (4a); `backend-engineer` (4b). No frontend, no infra.
- **New ADR:** **no** — the identifier decision is the user's (#2502, 2026-10-09); the mechanism is the
  existing hand-tweak table (spec 206 / #2429).
- **Phase 4a colour: RED** — the audit read pivot changes for three contracts (spec §7).
- **Contracts:** unchanged — all three already carry `Name` (spec §3).
- **device/kiosk:** out of scope, no follow-up recommended — no split exists (spec §6.3).
- **Latency:** N/A (spec §9).
- **Shard filters:** none — no new Integration.Tests class.
- **Spec number:** 322 (321 last on `origin/develop`; no open PR or remote branch claims 322 at writing).

Format: `[ID] [P?] [Story]`. Strictly sequential — T020 depends on T010's red, so no `[P]`.

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Prerequisite (orchestrator)

- [ ] **T001** Re-check spec number 322 across remote branches and open PRs before opening the PR.

## US1 (P1) — Every variable event lands on the variable's name

- [ ] **T010** [US1] (test-writer) In `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`,
  flip rows 17–19 to expect the `Name` sentinel with `MustNotBeIdentifier` = the `Variable` guid, and add
  the `Every_variable_contract_for_one_variable_pivots_on_the_same_identifier` fact (plan §4). Run
  `dotnet test tests/AuditObservability.Application.Tests --filter FullyQualifiedName~V1ResourceMapTests`;
  return verbatim output. **Expected red:** the three rows and the new fact.
- [ ] **T020** [US1] (backend-engineer, depends T010) Add the three hand-tweaks and the shared comment in
  `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs` (plan §2). May not edit
  the tests. Done when: T010's command is green unmodified; the full `AuditObservability.Application.Tests`
  and `Architecture.Tests` projects are green; `dotnet format --verify-no-changes` clean.
- [ ] **T030** [US1] (verify, depends T020) Spec §8 step 2 on the live stack: define, set, archive
  `Spec322_Probe`; quote `GET /audit/variable/Spec322_Probe` (three rows, identifier = name) and
  `GET /audit/variable/<guid>` (empty) in the PR.
