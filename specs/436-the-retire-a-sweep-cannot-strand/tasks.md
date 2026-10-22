# Tasks 436 — The retire a sweep cannot strand

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2783 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing: a lost race stops dead-lettering the retire). T003's
first fact is a premise check that may arrive green; it is declared, not a shortcut.
**Engineer**: `backend-engineer`. **Test-writer**: T001–T003 (4a; returns verbatim output).
**Reviewers**: `backend-reviewer` (mandatory). `security-reviewer`: not required (plan §6).
**Tracking**: feature-level issue #2783. No per-task issues. **New ADR**: none (ADR-0182 Accepted).
**Latency leg**: none (control plane).

Format: `[ID] [P?] [Story] description`. Mechanism premises in spec §2b/§2c are decompile-verified
against Wolverine 6.51.0 at phase 1; plan §2.1's `RetryWithCooldown` slot fall-through likewise
(`FailureActions.RetryWithCooldown` → `RetryInlineContinuation(delay)` slots; `FailureRule` →
`MoveToErrorQueue` once the slots are exhausted).

## 4a — tests first (test-writer)

- [ ] **T001 [US1]** New `tests/StreamDistribution.Infrastructure.Tests/RetireStreamFailurePolicyTests.cs`,
  continuation facts (host built as `CameraEventFailurePolicyCharacterisationTests.DiscoverAsync`, plus
  `opts.Policies.Add<RetireStreamFailurePolicy>()`; reflect on the same two `FailureRuleCollection`
  internals). Assert on the continuation **type and its `Delay`**, not on a type name string only:
  - **A** `CameraRetiredV1` + `DbUpdateConcurrencyException`: attempts 1–3 → `RetryInlineContinuation`
    with 100 ms / 500 ms / 2 s; attempt 4 → `MoveToErrorQueue`.
  - **B** `CameraRetiredV1` + `InvalidOperationException`: attempt 1 → `MoveToErrorQueue`.
  - **C** `CameraRegisteredV1` and `CameraAddressChangedV1` + `DbUpdateConcurrencyException`:
    attempt 1 → `MoveToErrorQueue` (non-terminal / computed-from-state writes still do not retry).
  Expected: A red (`RetireStreamFailurePolicy` absent → compile failure; with a stub class, attempt 1 is
  `MoveToErrorQueue`). B, C green on arrival (negative guards; they go red only under T007 (b)/(c)).
- [ ] **T002 [US1]** Same file, reload fact: a Wolverine host (`StubAllExternalTransports`, no Postgres,
  `ServiceLocationPolicy.AllowedButWarn`, discovery over the StreamDistribution Application assembly,
  the policy added) with `RetireStreamCommandHandler`, `CameraRetiredIntegrationEventHandler`, a
  `FakeRtspGateway`-shaped gateway and a **scoped** fake `IStreamRepository` holding one Healthy stream
  (copy-loaded per instance from a shared seed), whose `SaveAsync` consults a shared script
  (throw `DbUpdateConcurrencyException` on save #1) and which records one entry per instance.
  `await bus.InvokeAsync(cameraRetiredV1)`; assert: **two** repository instances each loaded once,
  the second saved a `Retired` stream, the path removed once. Plus **D**: script throws on every save →
  `InvokeAsync` throws after **four** instances. Expected red: one instance, `DbUpdateConcurrencyException`.
  Caveat to record in the test doc: `InvokeAsync` exercises `Executor.InvokeAsync`'s inline loop; the
  listener path (`ExecuteAsync` → `RetryExecutionNowAsync`) shares `Handler.HandleAsync` and the rule
  set, and T001 pins the latter.
- [ ] **T003 [P] [US1]** New `tests/Integration.Tests/StreamDistribution/RetireHealthRaceIntegrationTests.cs`
  (Aspire fixture; **add the class to a `tests/Integration.Tests/ci-shards/shard-N.filter`** — a missing
  entry fails deterministically). `ResetMediaMtxAsync` + `ResetStreamDistributionAsync`; seed a Healthy
  stream + its MediaMTX path; hand-build `RetireStreamCommandHandler` (spec 318's pattern) over a
  repository decorator that, after `GetByCameraAsync`, runs `ReportStreamHealthCommandHandler`
  (ready observation) on a **separate** `StreamDistributionDbContext`.
  - **IT-1** (premise, green on arrival): the retire's save throws `DbUpdateConcurrencyException`;
    the row is still not `Retired`.
  - **IT-2**: a second attempt on a fresh scope → `Retired`, MediaMTX path absent.
  Mind the live watcher (see `ProvisionRetireRaceIntegrationTests`' doc): the interposed write is what
  forces the conflict, not the watcher's timing. If IT-1 cannot be made to conflict, **stop: the lane
  blocks** (ADR-0182 Decision 1) with the verbatim output.

Depends: T001 → T002 (same file, sequential). T003 **[P]** with both (disjoint files).
T001–T003 → T004 → T005 → T006 → T007 → T008.

## Implement (backend-engineer)

- [ ] **T004 [US1]** `src/StreamDistribution/Infrastructure/RetireStreamFailurePolicy.cs` per plan
  §2.1; doc comment cites ADR-0182 and states how the retire meets each of the four conditions
  (terminal `Stream.Retire`; idempotent early return; fixed transition from the message; reloads —
  the per-attempt scope T002 observes). Register via `configureMore` (plan §2.2). T001 A, T002 green.
  Commit.
- [ ] **T005 [US1]** `CameraEventFailurePolicyCharacterisationTests` per plan §2.3: add the policy to
  `DiscoverAsync` (Theory assertions **unchanged** and green), exempt exactly the one policy path from
  the scan, update the class doc's "Pinned (2)". Call out in the PR as the ADR-0182 movement.
  **Blocked by T004** (the scan goes red the moment T004 lands — T004 and T005 may share a commit so
  every commit builds and passes).
- [ ] **T006** Run `StreamDistribution.*` tests, `Architecture.Tests` (incl.
  `StreamDistributionRegistrationCharacterisationTests` — expected unaffected: `Policies.Add` is not
  a DI registration; if it moves, stop and report), `LayoutComposition.Infrastructure.Tests`
  (`WallSceneSwitchFailurePolicyTests` unmodified, green), and T003 on the Aspire stack.
- [ ] **T007** Counterfactuals (run, record verbatim, revert):
  (a) remove the `configureMore` registration → T001 A, T002 red;
  (b) widen to `OnException<Exception>` → T001 B red;
  (c) apply the rule to every chain (drop the `MessageType` filter) → T001 C red;
  (d) add a second `IHandlerPolicy` file under `src/StreamDistribution` → T005's scan red;
  (e) register the fake repository as **singleton** (a reused scope) → T002 red — proves T002 can tell a
  stale scope from a fresh one; if it stays green, **the lane blocks** (Decision 1: reload unsettled);
  (f) `.Then.MoveToErrorQueue()` first, i.e. `MoveToErrorQueue` on attempt 1 → T001 A red.
- [ ] **T008** Release build, `dotnet format --verify-no-changes`, coverage gates (ADR-0065).

## PR

Quote T001–T003's red output and T007's counterfactuals. Record on #2783 (ADR-0182 Decision 1): the
default is **first-attempt dead-letter, not three attempts** (spec §2b) — the owner may want ADR-0182's
Context corrected; and that `CameraRetiredIntegrationEventHandler.cs:48-53`'s retry comment is
inaccurate (spec §6), for a separate decision. Closing keyword `Closes #2783`.
