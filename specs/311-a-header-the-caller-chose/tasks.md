# Tasks 311: A header the caller chose

Spec: [`spec.md`](spec.md) · Plan: [`plan.md`](plan.md) · Issue #2283 (feature-level; must be on
Project #13 — verify by `content.url`).

## Phase-3 declarations

- **Colour: behaviour-changing → red first** (ADR-0139). T001–T003 must be observed failing on
  this tree; the verbatim output goes in the PR body. T004 (guard witness) may arrive green —
  state why in the PR.
- **Implementer:** infra-engineer (`src/ApiGateway`, `src/AppHost`). No bounded-context code.
- **ADR:** none required — ADR-0153 clause 2 delegates the choice to #2283. Recommend (do not
  write) a clerical ADR-0106 / ADR-0159 note; put the recommendation in the PR body.
- **Not a weakened gate:** T002 inverts an assertion that pinned the bypass; it becomes stricter.

## Phase 1–3 (architect — done)

## Phase 4a — tests first (test-writer). Run all; quote the red verbatim.

- [ ] **T001 [US1]** `tests/Integration.Tests/ApiGateway/GatewayRateLimitIntegrationTests.cs`:
  add `A_caller_rotating_X_Fab_is_still_refused_once_over_its_limit` — 101 proxied requests,
  `X-Fab: rot-{n}`, assert non-429 count `<= 100`. `finally`: poll until non-429, bounded by
  window + 15 s (plan §5.1).
- [ ] **T002 [US1]** Same file: invert the existing test — `rl-fab-b` after `rl-fab-a` exhaustion
  is `429`; gateway `/health` stays `200`; rename; rewrite doc comment; same `finally` recovery.
  (Depends on T001 — same file.)
- [ ] **T003 [P] [US1]** New `tests/Integration.Tests/ApiGateway/GatewayEdgeHeaderTransformTests.cs`
  (FixtureLogic): every route in `src/ApiGateway/appsettings.json` strips inbound `X-Fab` and
  forwards `Authorization`; route count ≥ 9. Add `Yarp.ReverseProxy` PackageReference to
  `SmartSentinelEye.Integration.Tests.csproj`; add the class to `ci-shards/shard-4.filter`.
- [ ] **T004 [P] [US2]** `tests/Integration.Tests/AppHostReplicaCountTests.cs`: run-mode assertion
  → empty; remove `GatewayException` + #2283 doc paragraph; add the synthetic two-replica witness
  test (plan §5.3).

## Phase 4b — implement (infra-engineer). Tests may not be edited.

- [ ] **T005 [US1]** `src/ApiGateway/Program.cs`: source-only partition key; delete `fabHeader`;
  correct the three comments (FR-001, FR-007).
- [ ] **T006 [P] [US1]** `src/ApiGateway/appsettings.json`: remove `RateLimiting:FabHeader`; add
  `RequestHeaderRemove: X-Fab` to all nine routes (FR-003, FR-004).
- [ ] **T007 [P] [US2]** `src/AppHost/AppHost.cs`: remove the `WithReplicas(2)` block; rewrite
  the HA comment; tidy the spec-232 "per replica" wording (FR-005, FR-007).
- [ ] **T008** Format + Release build (analyzers) + `dotnet test` for Architecture.Tests and the
  FixtureLogic classes; T001–T004 green. Counterfactual: re-add the header to the key, observe
  T001 red, revert (plan §5).

## Phase 5 — verify

- [ ] **T009** Boot the stack; from one client send 101 rotating-`X-Fab` requests through
  `api-gateway`, record the 429 count; confirm `api-gateway` shows one replica in the dashboard.
  Watch the Playwright job for gateway 429s (plan §6). Latency: N/A (REST only).

## Phase 6 — review

- [ ] **T010** `/code-review` + **security-review** (trust-boundary change at the edge).

## Dependencies

- T001 → T002 (same file). T003, T004 parallel with T001/T002 (disjoint files).
- Phase 4b after all of 4a. T005 ∥ T006 ∥ T007 (disjoint files). T008 after T005–T007.
- No foundational (Shared.Kernel / Contracts) task.
