# Spec 328 — The outage the revoke must outlast

**Issue:** #2629 — "No Wolverine failure policy or alerting for the webhook-revocation disable
message; a Keycloak outage dead-letters it silently"
**Branch:** `fix/2629-webhook-revoke-retry-policy` · **Worktree:** `D:\Github\sse-2629`
**Lane:** autonomous (ADR-0144). **The direction was decided by a human** — issue comment,
2026-10-08: *"add a retry-with-cooldown Wolverine failure policy for the webhook-revocation
disable message — the first custom retry policy in this repo, closing the gap directly rather than
just adding DLQ alerting."* This spec chooses the mechanism and the values that decision needs; it
does not re-open the decision. The issue's option 2 (alerting on Identity's error queue in
general) was **not** approved and is out of scope.
**Predecessors:** spec 264 (#2206, the revoke now disables the Keycloak client — its §3
residual-risk row is what this closes), spec 296 (FR-011, `WallSceneSwitchFailurePolicy`, the
repo's first Wolverine failure rule), spec 270 / ADR-0160 (request-time revocation from
`DisabledAt`).
**ADRs:** ADR-0042 / ADR-0057 (Wolverine as dispatcher), **ADR-0088** (Wolverine defaults: per-module
queues, durable Postgres message store), ADR-0126 (which listeners keep the durable inbox — Identity
does), ADR-0143 (HTTP retries; the per-attempt budget below), ADR-0160 (revocation is enforced from
`DisabledAt`), ADR-0153 (one instance per service), ADR-0036 (smallest change), ADR-0037, ADR-0052/0053
(xUnit + Shouldly, sentence names), ADR-0103 (no Testcontainers), ADR-0109 (`[P]`), ADR-0139 / ADR-0144
(red first; no gate weakening).
**Constitution:** §VIII (safe by default at trust boundaries), §Testing (new behaviour starts red),
NFR §Security, NFR §Availability (24/7).
**New ADR needed:** no — see plan §1. The values are recorded here and in the policy's doc comment.
**Latency budget:** N/A. Identity's admin/revocation path; no §IV leg is touched.

---

## Problem (re-verified at `45243305`)

`src/Identity/Application/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandler.cs:57-63`
logs `WebhookClientDisableFailed` (Warning) and throws `InvalidOperationException` on any
`DisableWebhookClientError` other than `WebhookClientNotFound` — in practice
`KeycloakUnavailable`, because `DisableWebhookClientCommandHandler.cs:35-42` maps every non-cancel
exception from `IKeycloakAdminClient.DisableClientAsync` to it.

No failure rule exists on that handler's chain. `IdentityInfrastructureModule.cs:162-165` calls
`AddWolverineForContext` with no `configureMore`, and `WolverineDefaults.cs` sets no
`MaximumAttempts`. In the pinned WolverineFx **6.40.0**, `FailureRuleCollection.CombineRules`
then resolves `MaximumAttempts ?? parent.MaximumAttempts ?? 3` — **three attempts**, two immediate
requeues, then `MoveToErrorQueue` (decompiled, not assumed; plan §2).

Each attempt carries the standard HTTP resilience pipeline on the Keycloak admin client (ADR-0143:
`GET` + `PUT` are idempotent, so retried; 10 s per attempt, 30 s total). So the whole budget for a
Keycloak outage is **≤ ≈ 90 s** (hung connections) and **as little as seconds** (connection refused,
or an open circuit breaker). A Keycloak pod restart alone outlasts that.

What a dead letter costs: ADR-0160 refuses a revoked client's tokens from its `DisabledAt`, and
`DisabledAt` is stamped only **after** Keycloak accepts the disable (spec 264's deliberate
Keycloak-first order). So a dead-lettered revocation leaves the client **enabled in Keycloak, its
row active, its tokens valid (realm `accessTokenLifespan` 3600 s) and re-mintable once Keycloak
recovers** — indefinitely, while the operator who called `DELETE /webhook-integrations/{name}`
holds a `200`.

**Premise correction.** The issue calls this "the first custom Wolverine failure policy in the
repo". It is not: `src/LayoutComposition/Infrastructure/WallSceneSwitchFailurePolicy.cs` (spec 296)
dead-letters a lost concurrency race. It is the first **retry** policy, which is what the
decision comment says. This spec reuses spec 296's shape (a chain-scoped `IHandlerPolicy`
registered through `configureMore`) and its test technique.

## Decision encoded by this spec

| Item | Value |
|---|---|
| Scope | `WebhookIntegrationRevokedV1`'s handler chain in Identity **only** — no shared default, no other chain |
| Matched failure | a new `WebhookClientDisableFailedException` (derives `InvalidOperationException`), thrown where the handler throws today — **not** every exception |
| Mechanism | Wolverine `ScheduleRetry` (durable reschedule through Identity's Postgres inbox) — see D1 |
| Cooldown ladder | **1 min, 2 min, then 5 min × 12** — 14 scheduled retries, 15 attempts in all |
| Outage covered | **≥ 63 min** of cooldown (+ up to 30 s of HTTP retries inside each attempt, ≈ 70 min worst case) |
| Post-recovery exposure | ≤ **5 min** cooldown + ≤ 5 s scheduled-job poll + ≤ 5 s ADR-0160 snapshot refresh |
| After the ladder | `MoveToErrorQueue` — unchanged destination, unchanged Wolverine log and dead-letter metric |
| Jitter | none (plan §3.4) |
| Other failures on the chain (Postgres, cancellation, …) | unchanged: Wolverine's default three attempts |

**Gate question D1 (recommendation taken, reviewer may overrule): `ScheduleRetry`, not the
literally-named `RetryWithCooldown`.** In 6.40.0 `RetryWithCooldown` builds
`RetryInlineContinuation`, which `Task.Delay`s **inside the listener** with no cancellation token —
for minute-scale cooldowns that holds Identity's only listener on this queue for up to an hour,
blocks host shutdown until its timeout, and loses its place on a restart. `ScheduleRetry` builds
`ScheduledRetryContinuation`, which reschedules the envelope in the durable inbox (`wolverine_identity`)
and frees the listener — the same "retry after a cooldown" semantics, surviving a restart.
The decision's substance (option 1: retry with a cooldown, scoped to this message, rather than
DLQ alerting) is unchanged. **Fallback if overruled:** `RetryWithCooldown` with the identical
ladder is a one-token swap in the policy and one string change in the test (plan §7).

**Gate question D2 (recommendation taken): the ladder.** The anchors (plan §3): the first two
steps outlast a pod restart or a brief database failover at little cost; the 5-minute cap bounds
how long a revoked client stays usable *after* Keycloak is back, and keeps that window well
inside the 60-minute token lifetime; one hour of coverage spans restarts, failovers, node drains
and a maintenance window. A Keycloak outage longer than an hour is not silent — every operator
sign-in and token refresh fails with it — so the dead letter it still produces is the documented,
manual recovery path from spec 264, not the silent loss this issue is about.

## Out of scope

- **Option 2 — alerting on Identity's error queue** (or any dead-letter alerting). Not approved.
- A retry policy for any other handler, or a repo-wide default (`opts.Policies.OnException…`,
  `MaximumAttempts`).
- Reordering `DisableWebhookClientCommandHandler` to stamp `DisabledAt` before Keycloak (would
  make ADR-0160 refuse tokens during the outage, but reverses spec 264's deliberate order — a
  separate decision).
- Extending `OrphanedClientSweep` to `sse.kind=webhook` (spec 320 §4.5 excluded it deliberately).
- Retry of non-Keycloak failures on this chain (Postgres blips keep the default three attempts).
- Rewriting spec 264's residual-risk row — it is a record; this PR references it.

---

## User stories

### US1 (P1, the whole slice) — a revocation that waits out a Keycloak outage

**As** the operator who revokes a webhook integration,
**I want** the Keycloak client disable to keep retrying through a Keycloak outage of up to an hour,
with growing pauses, before giving up,
**so that** my `200` means the integration's credential actually stops working once Keycloak is
back, instead of dead-lettering after a minute and staying usable indefinitely.

Independently shippable: one exception type, one policy, one registration line; observable end to
end by pausing Keycloak across a revocation (procedure below).

## Acceptance scenarios

Configuration facts run in `Identity.Infrastructure.Tests` by compiling Identity's real handler
chains in-process (spec 296 technique, no broker, no database, no container). Handler facts run
in `Identity.Application.Tests`.

**AS-1 — a failed disable is retried on the ladder (happy path of the fix).**
```gherkin
Given Identity's handler graph, discovered from the real Identity.Application assembly
  And Identity's own message-handling configuration applied
 When WebhookIntegrationRevokedV1's chain meets WebhookClientDisableFailedException on attempts 1..14
 Then attempt 1 schedules a retry in 1 minute, attempt 2 in 2 minutes,
      and attempts 3..14 each in 5 minutes
```

**AS-2 — the ladder ends in the dead-letter queue (exhaustion).**
```gherkin
Given the same chain
 When WebhookClientDisableFailedException occurs on attempt 15
 Then the message is moved to the error queue, not retried again
```

**AS-3 — the handler throws exactly what the policy matches (the link).**
```gherkin
Given the revocation handler whose disable command returns KeycloakUnavailable
 When it handles a valid WebhookIntegrationRevokedV1
 Then it throws WebhookClientDisableFailedException
  And WebhookIntegrationRevokedV1's failure rule matches that very exception instance
```

**AS-4 — the rule does not widen (bad request / wrong failure).**
```gherkin
Given the same chain
 Then its failure rule does not match a plain InvalidOperationException,
      a DbUpdateException, or an OperationCanceledException
  And a message that can never succeed (unparsable fab, invalid ClientId, client not found)
      is still dropped without throwing (existing facts, unmodified)
```

**AS-5 — no other chain changes (scoping).**
```gherkin
Given Identity's discovered handler graph with the configuration applied
 Then every chain other than WebhookIntegrationRevokedV1's has no failure rule
```

**AS-6 — the existing retry signal still holds (no regression).**
```gherkin
Given the existing WebhookIntegrationRevokedIntegrationEventHandlerTests, unmodified
 Then all of them pass, including KeycloakUnavailable_throws_so_Wolverine_retries
      (which expects InvalidOperationException; the new type derives from it)
```

**Auth.** No auth surface changes. `DELETE /webhook-integrations/{name}` keeps its scope check in
EventIngestion; the disable runs under Identity's Keycloak service account as today.

**Conflict.** No write-conflict surface is added. A retried disable is idempotent: `PUT enabled=false`
on an already-disabled client is a no-op, and a client that disappeared meanwhile resolves to
`WebhookClientNotFound` and is dropped (existing behaviour). Concurrent revoke/rotate races are
spec 318's (`RotateWebhookClientCommandHandler`), unchanged.

## Independent end-to-end test procedure

1. Run the two test classes (plan §5); quote output.
2. *(Phase 5, live; needs the one Aspire stack on this machine and no other run using it.)* Boot the
   AppHost. Create a webhook integration; confirm its Keycloak client `webhook-<name>` is enabled.
3. `docker pause` the Keycloak container. `DELETE /webhook-integrations/<name>` → `200`.
4. Observe at least one `WebhookClientDisableFailed` Warning from Identity. Wait **3 minutes** —
   longer than the old default budget.
5. `docker unpause` Keycloak. Within ≤ 5 minutes of the next scheduled attempt, observe
   `DisabledWebhookClient` in Identity's log, the Keycloak client `enabled: false`, the
   `RegisteredClient` row's `DisabledAt` set, and the client on Identity's revoked-client list.
6. Confirm nothing for this message landed in Identity's dead-letter storage.
7. *(Counterfactual, recommended.)* Same steps 2-6 on `origin/develop` binaries: the message
   dead-letters within ≈ 90 s and the client stays enabled after unpause.

## Assumptions (marked, to be checked at phase 5)

- **A1 — derived, not measured:** the old budget (≤ ≈ 90 s) is computed from Wolverine's decompiled
  default and the standard resilience handler's defaults; step 7 measures it.
- **A2 — derived:** a rescheduled envelope keeps its `Attempts` count across reschedules (the inbox
  persists it), so the ladder advances rather than restarting. Step 5 observes the second slot
  (2 min) only if Keycloak stays paused past attempt 2; record which slots fired.
- **A3:** Identity's listener for this queue is durable (ADR-0126 opted only audit listeners into
  native acks; `IdentityInfrastructureModule` passes no `useNativeAcks`), so `ScheduleRetry` takes
  the durable-inbox path (`MessageContext.ReScheduleAsync`). RabbitMQ offers no native scheduling here.
- **A4:** "about an hour" is a judgment about outage shape (restart / failover / drain / maintenance),
  not a measured incident history — none exists (no production deployment, constitution §Availability).
