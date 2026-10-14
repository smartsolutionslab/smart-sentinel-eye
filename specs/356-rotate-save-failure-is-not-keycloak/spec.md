# Spec 356: A webhook rotation's save failure is not a Keycloak failure

**Feature Branch**: `fix/2913-rotate-502` (cut from `origin/develop` at `452e8635`)

**Created**: 2026-10-14

**Status**: Ready for the Phase 1 gate. No `[NEEDS CLARIFICATION]` remains. Judgement calls are
marked **[JUDGEMENT]** (§6).

**Input**: Issue [#2913](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2913),
*Rotate branch answers 502 KEYCLOAK_UNAVAILABLE for a non-concurrency save failure although Keycloak
was never called.* Found in spec 348 §6 (oddity 1), pinned as-is by
`RotateWebhookClientOrderCharacterisationTests`. The only issue comment is the lane pick-up note.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (the answer to a failed save changes):
Phase 4a is **RED** (constitution §Testing; ADR-0139).

**Spec number.** 356. Checked 2026-10-14 after `git fetch origin`: `origin/develop` (`452e8635`) and
every remote head via `git ls-tree <ref> specs/` — highest is 355. Re-check before the PR is opened.

**ADRs.** ADR-0047 (Result for business failures, **exceptions for infrastructure failures**,
translated to 5xx — the decision this spec applies), ADR-0089 (`ApiError` with HTTP status — an
`ApiError` names a failure the caller can act on, and "Keycloak" is the wrong one here), ADR-0113
(two-layer concurrency; the Layer-2 `DbUpdateConcurrencyException` must keep reaching
`ConcurrencyConflictExceptionHandler`), ADR-0142 (idempotency; an exception releases the claim),
ADR-0037 / ADR-0144 (workflow, lane), ADR-0139 (red first), ADR-0036 (smallest change). **No new ADR
is needed**: ADR-0047 already decides that a database failure is an exception, not a `Result`.

**Latency budget.** N/A — Identity admin endpoint; not on the event-to-overlay path (constitution §IV).

---

## 1. Premise verified (Phase 1 investigation)

| Claim | Finding (at `452e8635`) |
|---|---|
| A rotate-branch save failure answers 502 `KEYCLOAK_UNAVAILABLE` | **Confirmed.** `RotateWebhookClientCommandHandler.RotateOrRegisterAsync` wraps both branches in one `try`; `RollSecretAsync` calls `clients.SaveAsync` *before* `keycloak.RotateClientSecretAsync`. The generic `catch (Exception) when (not OperationCanceledException and not InvalidOperationException and not DbUpdateConcurrencyException)` turns any other save failure into `RotateWebhookClientFailures.KeycloakUnavailable(ex.Message)` → `502`, message `"Keycloak Admin API call failed: <db message>"`. |
| Keycloak was never called on that path | **Confirmed.** Pinned by `A_non_concurrency_save_failure_on_rotate_is_answered_keycloak_unavailable_without_calling_keycloak` (log `["status","lookup","save"]`, `RotateRequests` empty). |
| Same defect elsewhere in the handler | **Yes, on the create branch.** `RegisterAsync` calls Keycloak first, then `clients.SaveAsync` inside the same `try`; a save failure is again answered 502 `KEYCLOAK_UNAVAILABLE` — Keycloak *answered successfully*, the database failed. Pinned by `A_non_concurrency_save_failure_on_create_is_answered_keycloak_unavailable_after_the_client_was_created`. Included here: same `try`, same misattribution, same fix (§6 J1). |
| Other Identity handlers share it | **No.** `DisableDevice`/`DisableKiosk`/… wrap only the Keycloak call and leave `SaveAsync` outside the `try`. This handler is the outlier. |
| Pipeline answer for an escaping exception | `Identity/Api/Program.cs` runs `UseExceptionHandler()` with `AddProblemDetails()` (`ServiceDefaults/AuthenticationDefaults.cs:130`): a unique violation becomes `409 RESOURCE_ALREADY_EXISTS` (`UniqueConstraintExceptionHandler`), a concurrency loss `409` (`ConcurrencyConflictExceptionHandler`), anything else `500` ProblemDetails. `IdempotentRequest` releases the key's claim on an exception (`IdempotentRequest.cs:192-208`). |

## 2. Decision: the status and code

**A persistence failure leaves the handler as an exception; the pipeline answers it.** No new error
code, no `Result` variant.

- **Why not a persistence-specific `ApiError` code (e.g. `503 PERSISTENCE_UNAVAILABLE`)?** ADR-0047
  is explicit: `Result` is for expected business failures the caller handles; "DB unreachable" is
  its own example of an infrastructure failure that is an exception and becomes 5xx. No handler in
  the repository models a database failure as an `ApiError`; inventing one here would be a new
  convention for one endpoint (and would need an ADR).
- **Why 500 rather than 502?** 502 says an upstream the gateway called answered badly. The
  database is this service's own state; its failure is this service's failure.
- **Bonus correctness:** the database's own message stops being echoed in a response body. A
  unique violation now reaches `UniqueConstraintExceptionHandler`'s fixed-text 409 instead of a
  502 that quoted the Postgres message.

## 3. User stories

### US1 (P1) — An operator rotating a webhook client is not told Keycloak failed when the database did

**Why P1**: it is the whole issue. An on-call reading `KEYCLOAK_UNAVAILABLE` checks Keycloak, finds it
healthy, and loses time; the 502 also tells API clients an upstream is flaky when it is not.

**Independent test**: the handler unit tests in §4 — a save failure is observed to propagate out of
`HandleAsync` unconverted, with no `Result` returned.

## 4. Acceptance scenarios

```gherkin
Scenario: AS-1 rotate-branch save failure propagates, Keycloak untouched (happy path of the fix)
  Given webhook client "webhook-qa" exists in fab "munich" at version 3
  And the next save fails with DbUpdateException "database unavailable"
  When the rotation is requested with expected version 3
  Then HandleAsync throws that DbUpdateException unconverted
  And no secret roll was requested from Keycloak
  And nothing was published

Scenario: AS-2 rotate-branch save failure of a non-EF kind also propagates (try extent, not filter)
  Given webhook client "webhook-qa" exists at version 3
  And the next save fails with TimeoutException "db timeout"
  When the rotation is requested with expected version 3
  Then HandleAsync throws that TimeoutException unconverted
  And no secret roll was requested from Keycloak

Scenario: AS-3 create-branch save failure propagates after Keycloak created the client
  Given no webhook client "webhook-qa" exists
  And the next save fails with DbUpdateException "database unavailable"
  When the rotation is requested with no expected version
  Then HandleAsync throws that DbUpdateException unconverted
  And the call log is status, lookup, create, add, save
  And nothing was published

Scenario: AS-4 conflict — the Layer-2 concurrency loss still reaches the 409 middleware (unchanged)
  Given the next save fails with DbUpdateConcurrencyException
  Then HandleAsync throws it unconverted on both branches (existing tests, unmodified)

Scenario: AS-5 Keycloak failures keep their answer (unchanged)
  Given Keycloak's secret roll or client create fails with HttpRequestException / KeycloakClientNotFoundException
  Then the result is 502 KEYCLOAK_UNAVAILABLE with "Keycloak Admin API call failed: <message>"
  And a KeycloakClientAlreadyExistsException on create is still 409 RESOURCE_ALREADY_EXISTS
  And InvalidOperationException / OperationCanceledException still propagate (existing tests, unmodified)

Scenario: AS-6 bad request and auth (unchanged, not touched)
  Invalid integration name -> 400 WEBHOOK_INVALID_INPUT; missing sse.webhooks.write -> 403;
  revoked / unverifiable integration -> 409 / 502 as before. No code on those paths changes.
```

## 5. Functional requirements

- **FR-001** In `RotateWebhookClientCommandHandler`, a failure of `clients.SaveAsync` on either
  branch MUST NOT be converted to `KeycloakUnavailable`; it propagates as thrown.
- **FR-002** Every Keycloak Admin call failure that is answered `KeycloakUnavailable` today keeps
  exactly that code, status and message.
- **FR-003** The call order of both branches is unchanged (rotate: save then roll; create: create
  then add then save) — `RotateWebhookClientOrderCharacterisationTests`' order facts pass unmodified.
- **FR-004** No new error code, no change to `RotateWebhookClientError`, `Shared.Contracts`, the
  endpoint, or the pipeline.

## 6. Judgement calls

- **J1 [JUDGEMENT]** The create-branch save failure is in scope although the issue names only the
  rotate branch: it is the same `try`, the same misattribution, and fixing one branch would leave
  the characterisation file pinning a known-wrong answer next to a corrected one.
- **J2 [JUDGEMENT]** Out of scope: on a create-branch save failure the Keycloak client already exists
  with no local row, so a retry gets `409 RESOURCE_ALREADY_EXISTS`. That orphan exists today too
  (the 502 did not clean it up) and is the orphaned-client sweep's concern. Not changed here.

## 7. Locked tech choices

xUnit + Shouldly + hand-written fakes (ADR-0052, ADR-0054), sentence-style names (ADR-0053), `var`
wherever it compiles. No new test class (no shard-filter entry needed).
