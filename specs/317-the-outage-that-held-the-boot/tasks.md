# Tasks 317: The outage that held the boot

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #2170

## Phase-3 declarations

- **Lane:** autonomous (ADR-0144); #2170 carries `agent:ready` (decision recorded by the user on
  the issue, 2026-10-08).
- **Engineers:** `test-writer` (4a) → `backend-engineer` (4b, C#, Identity Infrastructure). No
  frontend, no infra.
- **New ADR:** no — the decision (bounded, kept inline) was made on the issue; the 5 s value is an
  implementation detail justified in spec §4.
- **Phase 4a colour: RED** (behaviour-changing). A Keycloak that never answers is abandoned at a new
  5 s bound with a new Warning; today the same pass waits on the pipeline's 30 s budget and says
  nothing about the wait. Test 1 must be observed failing **at runtime** first. One guard (T002)
  is characterisation, observed green before the change.
- **Latency:** N/A (startup path; not event→overlay).
- **Board:** #2170 is on Project #13 (In Progress, verified 2026-10-08). No per-task issues.
- **Machine:** no Aspire stack needed for 4a/4b (unit tests only). Phase 5 step 2 needs one stack.
- **Granularity:** one PR — the spec commit, then one test+code commit (or tests then code, each
  building on its own).

Format: `[ID] [P?] [Story]`. `[P]` = disjoint files (ADR-0109). Nothing here is foundational to
another context and nothing fans out: the tests and the change are one hosted service.

## Phase 1–3 (architect — done)

- [x] **T000** spec.md, plan.md, tasks.md.

## Phase 4a — tests first (test-writer). Quote every run's output verbatim.

- [ ] **T001** [US1] Fakes in `tests/Identity.Infrastructure.Tests/Fakes/`:
      `SilentKeycloakAdminClient.cs` (enumeration awaits `Task.Delay(Timeout.InfiniteTimeSpan, ct)`,
      counts attempts; other members throw as `UnreachableKeycloakAdminClient` does) and
      `ManualTimeProvider.cs` (minimal hand-written copy of ScenarioSimulator.Tests' — `CreateTimer`,
      `Advance`, event-driven `WaitForPendingTimersAsync`). Plan §3.
- [ ] **T002** [US1] `tests/Identity.Infrastructure.Tests/KeycloakAdmin/KioskPrivilegeSweepBoundTests.cs`:
      `Host_shutdown_during_the_sweep_is_not_mistaken_for_the_bound`. Run on the **unchanged** tree —
      expect **green** (characterisation of FR-004). Depends on T001.
- [ ] **T003** [US1] Same file: `A_keycloak_that_never_answers_is_abandoned_at_the_bound_and_the_boot_continues`,
      composed through `AddIdentityInfrastructure` with last-wins overrides of `IKeycloakAdminClient`,
      `TimeProvider` and `ILogger<KioskPrivilegeSweepHostedService>` (existing `CapturingLogger<T>`),
      built via `ActivatorUtilities.CreateInstance`. The bound (5 s) is a test-owned local. Run on
      the **unchanged** tree — expect **red at runtime** (no timer requested). A compile error is not
      an acceptable red; a green is a phase-4 failure. Depends on T001.

## Phase 4b — the change (backend-engineer). Release build; may not edit T002/T003.

- [ ] **T004** [US1] `src/Identity/Infrastructure/Log.cs`: add `KioskPrivilegeSweepTimedOut(this ILogger, TimeSpan bound)`,
      Warning, beside `KioskPrivilegeSweepFailed`; message per plan §2.2. Depends on T003.
- [ ] **T005** [US1] `src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs`:
      `TimeProvider` constructor parameter; `private static readonly TimeSpan Bound = 5 s` with the
      §4 reasoning in its comment; `StartAsync` per plan §2.1 — bounded CTS on the `TimeProvider`,
      linked to the host token, and the **filtered** OCE catch ahead of the existing one. Update the
      class doc comment to say the pass is bounded and why. Depends on T004.
- [ ] **T006** [US1] Run `Identity.Infrastructure.Tests` (T002 + T003 green, unmodified),
      `Identity.Application.Tests`, `Architecture.Tests`; `dotnet build -c Release` clean. Confirm with
      `git diff --stat` that `KioskPrivilegeSweepStartupTests.cs`, `KioskPrivilegeSweepSteadyStateTests.cs`,
      `IdentityInfrastructureModule.cs` and `KioskPrivilegeSweep.cs` are untouched. Depends on T005.

## Phase 5 — verify (spec §2, procedure)

- [ ] **T007** [US1] Scratch measurement against TEST-NET-3 with the real resilience handler, twice:
      expect ≈ 5000 ms (was 30154 ms) and the `KioskPrivilegeSweepTimedOut` line verbatim. Remove the
      harness before commit. Write the figures into a `verification.md` here.
- [ ] **T008** [US1] One Aspire boot: Identity's console up to `Application started` shows the sweep's
      Keycloak calls before Wolverine's start and **no** timed-out line; record the healthy pass's
      duration against the 5 s bound. Run `KioskPrivilegeSweepStartupIntegrationTests` (unmodified).

## Phase 6 — review

- [ ] **T009** `backend-reviewer` on the diff; specifically the OCE filter (FR-002/FR-004) and that no
      gate was weakened.

## Dependencies

T001 → {T002, T003} → T004 → T005 → T006 → {T007, T008} → T009. T002 and T003 share a file and
run sequentially; T007 needs no stack and may precede T008.
