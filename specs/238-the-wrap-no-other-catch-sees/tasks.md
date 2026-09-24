# Tasks 238 — The wrap no other catch sees

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #2435 (feature-level issue; no per-task issues)
**Engineer**: `backend-engineer` · **Phase 4a colour**: **CHARACTERISATION, observed green** — no
production change; a red arrival refutes an audit premise and stops the slice (spec FR-004).

Format: `[ID] [P?] [Story] description — file(s)`.
`[P]` = owns files no other open task touches (ADR-0109). No foundational task: nothing in
Shared.Kernel/Contracts, AppHost or Aspire resources changes.

## Phase 4a — tests (test-writer; return verbatim output)

- [ ] **T001** [US1] Add the connection-interceptor harness and T1 + T2 per plan §2–§3, and one
  class-doc paragraph naming spec 238. First confirm `PostgresException`'s public constructor against
  the pinned Npgsql — `tests/ServiceDefaults.Tests/OutboxBacklogHealthCheckTests.cs`
- [ ] **T002** [US1] Run `dotnet test tests/ServiceDefaults.Tests --filter FullyQualifiedName~OutboxBacklogHealthCheckTests`
  on unchanged production code. Expected verbatim: **3 passed** (the existing test + T1 + T2).
  Any red: stop, quote it, report — A1 or A2 is false (spec §7). Depends on T001.

## Phase 4b — prove the guards (engineer; T001's tests may not be edited except transiently below)

- [ ] **T003** [US1] Counterfactuals per plan §4: (1) T2's SQLSTATE → `57P03`, run T002's command,
  quote T2 red, revert; (2) drop `&& IsUnreachable(inner)` at `OutboxBacklogHealthCheck.cs:129`, run,
  quote T1 red and the existing test green, revert. Finish with `git diff --exit-code src/` and
  T002's command green again — `OutboxBacklogHealthCheck.cs` (transient only),
  `OutboxBacklogHealthCheckTests.cs` (transient only). Depends on T002.
- [ ] **T004** [US1] `dotnet build -c Release` (analyzers as errors) and `dotnet format --verify-no-changes`
  clean for `tests/ServiceDefaults.Tests`; full `dotnet test tests/ServiceDefaults.Tests` green.
  Depends on T003.

## Orchestrator (no code) — can run any time, in parallel with the above

- [ ] **T005** [P] File O2 (composed health-check registration guard) per plan §6; no `agent:ready`;
  add to Project #13.
- [ ] **T006** [P] File O1 (suspected `KEYCLOAK_UNAVAILABLE` misattribution,
  `RotateWebhookClientCommandHandler.cs:139`) per plan §6; board-checked first; add to Project #13.
- [ ] **T007** [P] Board gate: verify #2435 on Project #13 by `content.url`, `--limit 2000`.

## Phase 5

- [ ] **T008** Verification note: test-verified (the subject is exception classification, which the
  unit harness drives directly; there is no running-system behaviour to observe that the tests do not).
  Quote T002 and both T003 counterfactuals. No latency figure — not on the §IV path. PR body closes
  #2435 and links T005/T006's issues. Depends on T004, T005, T006.

## Dependencies

```
T001 → T002 → T003 → T004 ─┐
T005 ──────────────────────┼→ T008
T006 ──────────────────────┘
T007 (independent)
```

T001–T004 share one test file and are strictly sequential. T005–T007 touch no files.
