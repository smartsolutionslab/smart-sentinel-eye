# Plan 356: A webhook rotation's save failure is not a Keycloak failure

**Spec**: [spec.md](./spec.md). **Issue**: #2913.

## 1. Constitution and ADR check

| Rule | Status |
|---|---|
| ADR-0047 — infrastructure failures are exceptions → 5xx | Applied: the fix lets the save's exception escape. |
| ADR-0089 — `ApiError` names a failure the caller acts on | Unchanged hierarchy; `KeycloakUnavailable` now only means Keycloak. |
| ADR-0113 — Layer-2 loss reaches `ConcurrencyConflictExceptionHandler` | Preserved; now by construction (the save is outside the `try`). |
| ADR-0142 — idempotency | `IdempotentRequest` already releases the claim on a thrown exception (the concurrency path proves it today). |
| No cross-context references; no new runtime resource | Nothing added. |
| §IV latency | N/A. |

## 2. Bounded context and layers

Identity, **Application layer only**:
`src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs`.
No Domain, Infrastructure, Api, Contracts or migration change. No entity / value object / event
changes; no messaging change (`WebhookIntegrationRotatedV1` is still published only after a
successful save).

## 3. The change

Narrow the `try` in `RotateOrRegisterAsync` to the Keycloak Admin calls only:

- **Rotate branch** (`RollSecretAsync`): `aggregate.Rotate(clock)` and `clients.SaveAsync` run
  outside the `try`; only `keycloak.RotateClientSecretAsync` is inside it.
- **Create branch** (`RegisterAsync`): only `keycloak.CreateClientAsync` is inside the `try`;
  `RegisteredClient.Register`, `clients.Add`, `clients.SaveAsync` run outside it.
- The `catch` clauses (`KeycloakClientAlreadyExistsException` → `WebhookClientNameConflict`; generic
  filter → `KeycloakUnavailable`) keep their mapping. The `and not DbUpdateConcurrencyException`
  exclusion and its comment become dead (nothing in the `try` saves) and are removed; the existing
  concurrency facts must still pass unmodified, which is the proof.
- Order within each branch is unchanged (spec FR-003). Shape is the engineer's call (e.g. a small
  helper that wraps one Keycloak call and maps its exceptions) within ADR-0084's advisory limits;
  the `Microsoft.EntityFrameworkCore` using goes if nothing else needs it.

**Rejected: adding `and not DbUpdateException` to the filter.** It fixes the EF case and leaves any
other save exception (a raw `NpgsqlException`, a timeout) misattributed. Spec AS-2 exists to tell the
two fixes apart.

## 4. Tests (Phase 4a, RED)

Existing file `tests/Identity.Application.Tests/Commands/RotateWebhookClientOrderCharacterisationTests.cs`
(class already in the shard filter — no filter change):

- **Replace** `A_non_concurrency_save_failure_on_rotate_is_answered_keycloak_unavailable_without_calling_keycloak`
  with `A_non_concurrency_save_failure_on_rotate_propagates_and_never_calls_keycloak` (AS-1).
- **Add** `A_save_failure_that_is_not_an_ef_exception_on_rotate_propagates_and_never_calls_keycloak`
  (AS-2, `TimeoutException`).
- **Replace** `A_non_concurrency_save_failure_on_create_is_answered_keycloak_unavailable_after_the_client_was_created`
  with `A_non_concurrency_save_failure_on_create_propagates_after_keycloak_created_the_client` (AS-3).
- **Edit** the class `<summary>` paragraph that says the rotate save failure is "pinned as it is and
  not as it ought to be", and the `// Pinned as it is…` comment above the replaced test.

Each new test asserts `Should.ThrowAsync<TException>` with the exact message, the call log, and
(rotate) `Keycloak.RotateRequests` empty / (create) `Bus.Published` empty. Expected red today: the
handler *returns* a `KeycloakUnavailable` result, so `ThrowAsync` finds nothing thrown.

Replacing the two characterisation facts is the behaviour change itself, not a weakened gate: their
own comments record them as pinned-as-is pending this issue. Every **other** test in both
`RotateWebhookClient*Tests.cs` files must pass **unmodified**.

## 5. Risks

- A shared `DbContext` after a failed save: the idempotency release runs on the store; that path is
  already exercised by the concurrency loss and `ReleaseQuietlyAsync` does not throw. No change.
- Integration-level proof is not added: provoking a non-concurrency `SaveAsync` failure against the
  real Aspire stack is not feasible without fault injection; the 500 is the stock
  `UseExceptionHandler` + `AddProblemDetails` behaviour, already in use for every unhandled exception.
