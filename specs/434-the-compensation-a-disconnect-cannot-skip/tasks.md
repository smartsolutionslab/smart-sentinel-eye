# Tasks 434 — The compensation a disconnect cannot skip

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #3080 · **Phase**: 3 (Tasks)
**Colour**: **red** (behaviour-changing). T001 is a mechanical constructor seam, characterised green.
Test E (T002) is a declared characterisation fact, green before and after.
**Engineer**: `backend-engineer`. **Test-writer**: T002–T004 (4a; returns verbatim output).
**Reviewers**: `backend-reviewer` + `security-reviewer` (mandatory).
**Tracking**: feature-level issue #3080. No per-task issues. **New ADR**: none. **Latency leg**: none.

Format: `[ID] [P?] [Story] description`. No task needs Docker.

## Seam (backend-engineer, green)

- [ ] **T001 [US1]** Add `TimeProvider timeProvider` to `RotateWebhookClientCommandHandler`'s
  constructor (unused yet); update every direct construction in `tests/Identity.Application.Tests`
  to pass one. Run `tests/Identity.Application.Tests` + `tests/Architecture.Tests` before/after:
  green, no assertion edited. Commit 1. **Blocks T002.**

## 4a — tests first (test-writer)

- [ ] **T002 [US1]** In `RotateWebhookClientCommandHandlerTests` (or a sibling
  `RotateWebhookClientCancellationTests` — then add it to the shard filter), with a status lookup
  and Keycloak/commit fakes that honour the token and a request CTS cancelled right after the
  create's `SaveAsync`:
  - **A** revoked post-commit → client disabled in Keycloak and the row, no rotated event, no OCE.
  - **B** revoked + Keycloak disable fails → `WebhookClientRaceDisableFailed` published, commit ran.
  - **C** active post-commit → `WebhookIntegrationRotatedV1` published, commit ran.
  - **D** Keycloak disable never answers; advance the time provider past 30 s → retry message
    published, commit ran.
  - **E** (characterisation) cancelled before the create's save → OCE, nothing committed.
  Expected: A–D red with `OperationCanceledException` (D: hangs/OCE — bound the test with its own
  timeout), E green.
- [ ] **T003 [P] [US2]** In `WebhookIntegrationRevokedFailurePolicyTests`, for **both** chains:
  executing the attempt-15 continuation (Moq `IEnvelopeLifecycle`/`IWolverineRuntime`) records one
  `sse.identity.webhook_client_disable.dead_lettered` measurement (`MeterListener`) with the right
  `message` tag; attempt 14 records none. Expected red (meter/name absent → compile or zero).
- [ ] **T004 [P] [US2]** Same file: the attempt-15 continuation still moves the envelope to the
  error queue (asserted through the dead-letter call the continuation makes on the mocked
  lifecycle — find the member by decompiling 6.51.0's `MoveToErrorQueue`; it is not on
  `IEnvelopeLifecycle`'s documented surface — not through the type name).
  Expected green on arrival (characterisation of the dead-letter itself).

Depends: T001 → T002. T003 ∥ T004 ∥ T002 (disjoint files).

## Implement (backend-engineer)

- [ ] **T005 [US1]** Plan §2.1–2.3: bounded CTS for lookup + inner disable, bound-expiry catch →
  retry, `CancellationToken.None` for publish + commit. T002 A–D green, E green. Commit.
- [ ] **T006 [US2]** Plan §3.1–3.2: decompile-verify the `.Then.MoveToErrorQueue().And(...)` shape
  on Wolverine 6.51.0 first (stop if it does not hold); add the meter class, the `Error`
  `[LoggerMessage]`, the policy line and `AddMeter` registration. T003/T004 green; the two existing
  attempt-15 facts either unmodified or adjusted per plan §3.3 and called out. Commit.
- [ ] **T007** Counterfactuals (run, record, revert): pass the request token back to the publish +
  commit → B, C red; link the bound to the request token → A red; drop `.And(...)` → T003 red; move
  the signal to the retry slot → the attempt-14 negative red.
- [ ] **T008** Release build, `dotnet format --verify-no-changes`, `Identity.*` + `Architecture.Tests`
  green; coverage gates (ADR-0065).

## PR

- [ ] **T009** PR body: verbatim 4a output, counterfactual results, the idempotency consequence
  (spec §2), item 3 recorded as **unverified** (spec §6), Phase 5 note (unit-level only, why).
