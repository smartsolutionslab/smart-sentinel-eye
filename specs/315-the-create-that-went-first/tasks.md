# Tasks 315: The create that went first

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2598

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2598 carries `agent:ready`.
- **Engineers:** `test-writer` (4a: baseline and reproduction runs only, writes no test) →
  `backend-engineer` (4b: the `InitializeAsync` edit). No frontend, no infra.
- **New ADR:** no — no retry predicate, timeout value or resilience option changes.
- **Phase 4a colour: CHARACTERISATION (green).** No product code changes and no fact's assertion
  changes; the thing under test is untouched, only the class's setup. A fact or assertion that has
  to be edited means behaviour moved — block.
- **Latency:** N/A.
- **Board:** #2598 is on Project #13 (In Progress, verified 2026-10-08). No per-task issues.
- **Machine:** one Aspire stack only. Each isolated `--filter` run boots a fresh fixture (~8 min).
- **Granularity:** one PR, one code commit plus the spec commit; each builds on its own.

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files (ADR-0109). Nothing here is parallel: every
code task touches the same file and the runs share the single stack.

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — characterise and reproduce (test-writer). Writes nothing; quote output verbatim.

- [ ] **T001** [US1] On the unchanged branch, quiet machine: run the whole class
      (`--filter FullyQualifiedName~RegisteredClientConcurrencyIntegrationTests`) — expect 8/8, or
      stop. Then run `A_rotation_that_loses_the_database_race_at_layer_2_is_told_it_conflicted_not_that_keycloak_is_down`
      alone three times on fresh boots. Record pass/fail and the test duration of each run, and any
      `TimeoutRejectedException` stack verbatim. Write the summaries into spec.md §6.
      Not reproducing is a valid outcome — record it; do not retry until it fails.

## Phase 4b — the edit (backend-engineer). Release builds only.

- [ ] **T002** [US1] `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`
      `InitializeAsync` (lines 49-54): after the `Running` wait, create the admin client with
      `aspire.CreateAdminClientAsync("identity", cts.Token)` and `await ListWebhooksAsync(identity)`.
      Add the *why* comment from plan §2. Nothing else in the file changes. Depends on T001.

## Phase 4c / 5 — verify (backend-engineer, then /verify)

- [ ] **T003** [US1] `dotnet build -c Release` and `dotnet format --verify-no-changes` clean. Whole
      class 8/8 with `git diff` showing only `InitializeAsync` changed. The T001 fact alone three
      times on fresh boots, plus `A_created_client_is_listed_with_the_version_its_next_rotation_needs`
      alone once. Record verbatim in spec.md §6. Depends on T002.
- [ ] **T004** [US1] **Residual gate (spec §4).** If any post-change isolated run still fails with
      `TimeoutRejectedException` at `CreateAsync`: do **not** raise a timeout, do **not** make the
      POST retryable, do not add a throwaway create. Stop, record timings, and report it as a
      product finding (the create's Keycloak-admin or commit leg exceeding 10 s on a warmed
      Identity) — `agent:blocked` with the verbatim output. If T001 never reproduced, the PR body
      states the fix is preventive and its effect undemonstrated. Depends on T003.
