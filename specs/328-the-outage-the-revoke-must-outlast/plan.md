# Plan 328 — The outage the revoke must outlast

**Spec**: [spec.md](spec.md) · **Issue**: #2629 · **Phase**: 2 (Plan)

## 1. Context, boundaries, ADR position

- **Bounded context:** Identity only. No contract in `Shared.Contracts` changes
  (`WebhookIntegrationRevokedV1` is consumed as is); no other context is touched; no
  cross-context reference is added (`BoundaryTests` unaffected).
- **Layers:**
  - **Application** — the handler throws a dedicated exception instead of a bare
    `InvalidOperationException`. Application gains **no** Wolverine reference (it has none today;
    the exception is a plain BCL subclass).
  - **Infrastructure** — the failure policy (`IHandlerPolicy`, needs `Wolverine.*`, which
    Infrastructure already has through `ServiceDefaults`) and its registration.
- **No domain change.** No entity, value object, invariant, event or migration. The domain-event →
  integration-event flow is untouched: EventIngestion publishes `WebhookIntegrationRevokedV1`,
  Identity's handler sends `DisableWebhookClientCommand`, exactly as spec 264 built it.

**ADR position — no new ADR, and no amendment to ADR-0088.** Reasons:

1. The direction (option 1, retry with cooldown, this message) is a human decision already
   recorded on the issue. The lane may not write an ADR anyway (ADR-0144).
2. The *shape* is not new: spec 296 already set the precedent — a failure rule is scoped to one
   message's chain through an `IHandlerPolicy` in that context's Infrastructure, registered via
   `AddWolverineForContext(configureMore: …)`, and never as a shared default. It did so without an
   ADR, and this follows it to the letter.
3. The *values* (the ladder) are a tuning of one handler, justified in §3 and recorded in the
   policy's doc comment where the next reader meets them. They bind nothing else.
4. What would change this: a **second** context adopting a retry ladder, or anyone proposing a
   repo-wide `MaximumAttempts`/`OnException` default. That is the point at which ADR-0088 should
   gain a "failure policies" amendment (scoped-by-default, `ScheduleRetry` for downstream-outage
   waits, `RetryWithCooldown` only for sub-second blips). Recorded here so it is not rediscovered;
   not written now (no speculative generality, ADR-0036).

## 2. What Wolverine 6.40.0 actually does (decompiled from the pinned package)

Source: `~/.nuget/packages/wolverinefx/6.40.0/lib/net10.0/Wolverine.dll` via `ilspycmd`, and the
shipped `Wolverine.xml`. Verified, not recalled — the API has moved between majors.

| Concern | 6.40.0 behaviour | Where |
|---|---|---|
| Default budget | `CombineRules`: `MaximumAttempts ?? parent.MaximumAttempts ?? 3`; synthesises a requeue rule with `maximumAttempts - 1` `RequeueContinuation` slots | `FailureRuleCollection.cs` |
| No rule matches | `DetermineExecutionContinuation` falls through to `new MoveToErrorQueue(e)` | same |
| Rule matches, attempt beyond its slots, no infinite source | `FailureRule.TryCreateContinuation` returns `MoveToErrorQueue` **directly** — the ladder's end is deterministic, not dependent on rule order | `FailureRule.cs` |
| Slot selection | slot whose `Attempt == envelope.Attempts` (1-based; `0` coerced to `1`) | same |
| Chain-scoped API | `HandlerChain : IWithFailurePolicies` → `chain.OnException<TException>()` → `PolicyExpression` (also `OnException<T>(Func<T,bool>, string)`) | `ErrorHandlingPolicyExtensions.cs` |
| `RetryWithCooldown(params TimeSpan[])` | ≤ 25 delays; each a `RetryInlineContinuation` → `Task.Delay(delay)` **without a cancellation token**, then `RetryExecutionNowAsync` in the same listener | `FailureActions.cs`, `RetryInlineContinuation.cs` |
| `ScheduleRetry(params TimeSpan[])` | ≤ 25 delays; each a `ScheduledRetryContinuation` → `lifecycle.ReScheduleAsync(now + delay)` | `FailureActions.cs`, `ScheduledRetryContinuation.cs` |
| Reschedule path | native scheduling if the listener supports it, else `Storage.Inbox.RescheduleExistingEnvelopeForRetryAsync` (durable inbox) | `MessageContext.ReScheduleAsync` |
| Scheduled pick-up | `DurabilitySettings.ScheduledJobPollingTime` = 5 s default | `DurabilitySettings.cs` |
| Continuation descriptions | `ScheduledRetryContinuation.ToString()` = `"Schedule Retry in {TotalSeconds} seconds"`; public `TimeSpan Delay` on an internal type; `MoveToErrorQueue` internal, `ToString()` = `"Move to Error Queue"` | continuation sources |
| Dead letter | `MoveToErrorQueue` → `MoveToDeadLetterQueueAsync` + Wolverine's own moved-to-error-queue log + `RecordDeadLetter` metric | `MoveToErrorQueue.cs`, `WolverineRuntime.cs` |

Identity's listener mode: `AddWolverineForContext` is called without `useNativeAcks`, so the
RabbitMQ listener keeps the durable inbox (ADR-0126 opted only AuditObservability out). RabbitMQ
has no native scheduling here → reschedules go to `wolverine_identity`'s inbox. One instance per
service (ADR-0153), so no cross-node ownership question.

**Why `ScheduleRetry` (spec D1).** For a cooldown measured in minutes, an inline delay:
holds the queue's single listener (`listenerCount` 1) for the whole hour; blocks graceful
shutdown (no token on `Task.Delay`) until the host's shutdown timeout; and on a restart replays from
the inbox with whatever attempt count was last persisted. A scheduled retry frees the listener,
survives a restart with its scheduled time, and its attempt count is persisted with the envelope.
Head-of-line blocking is not the deciding factor — other revocations would fail during the same
outage anyway — shutdown and restart behaviour are.

## 3. The ladder

```
attempt:   1    2    3    4    5    6    7    8    9   10   11   12   13   14   15
fails →  +1m  +2m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  +5m  dead-letter
cumulative cooldown after attempt 14: 1 + 2 + 12×5 = 63 min
```

### 3.1 Per-attempt cost already inside each step
Each attempt runs the Keycloak admin `GET` + `PUT` through ServiceDefaults' standard resilience
handler (ADR-0143: both idempotent, so retried): 10 s attempt timeout, 3 retries with exponential
backoff, 30 s total. So the HTTP layer already absorbs the sub-30-second blip; the Wolverine ladder
starts at the minute scale and does not duplicate it with seconds-scale steps.

### 3.2 First steps — 1 min, 2 min
Outlast a Keycloak pod restart (Quarkus start + realm cache) or a short database failover, which
are the likeliest outages and the ones the old ≤ 90 s budget already lost.

### 3.3 Cap — 5 min
The cap is the bound on **post-recovery exposure**: once Keycloak is back, the revoked client stays
enabled (and, per ADR-0160, its tokens stay accepted) until the next attempt fires — at most one
cap, plus the 5 s scheduled-job poll, plus ADR-0160's 5 s snapshot refresh. Five minutes keeps that
an order of magnitude inside the realm's 3600 s token lifetime; a 10–15 min cap would halve the
retry count for a window that matters more than the count. Same reasoning as `MqttBackoff`'s cap
(`MqttConnectionLoop.cs:313-324`): a long cap turns a short outage into a long gap.

### 3.4 Length — 12 steps at the cap (≥ 63 min), no jitter
- One hour spans restart, failover, node drain/reboot and a maintenance window. Beyond it, a
  Keycloak outage is loud on its own — every sign-in fails — so the residual dead letter is the
  documented manual recovery (spec 264 §3: replay Identity's error queue, or disable in the admin
  console), not a silent one. Spec A4 marks this as judgment, not measurement.
- 14 delays is well under Wolverine's 25-delay limit; 15 attempts emit 15
  `WebhookClientDisableFailed` Warnings over the hour — acceptable signal, not noise.
- No jitter: one message type on one instance; simultaneous revocations are a handful, not 250
  clients reconnecting (the case `MqttBackoff`'s jitter exists for). Jitter would also make the
  ladder untestable as exact values.

## 4. Changes — the exact scope

| # | File | Change |
|---|---|---|
| C1 | `src/Identity/Application/EventHandlers/WebhookClientDisableFailedException.cs` (new) | `public sealed class WebhookClientDisableFailedException : InvalidOperationException` with ctor `(string integrationName, string errorCode)` producing today's message verbatim: `$"DisableWebhookClientCommand failed for '{integrationName}': {errorCode}"`. Doc comment: why it exists (so the policy can single out the Keycloak failure), why it derives `InvalidOperationException` (keeps every existing observer, including the unmodified test, true). |
| C2 | `src/Identity/Application/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandler.cs` | Line 62: throw `new WebhookClientDisableFailedException(integrationName, result.Error.Code)`. Update the type's `<summary>` sentence about retries to name the policy (one sentence). Nothing else. |
| C3 | `src/Identity/Infrastructure/WebhookIntegrationRevokedFailurePolicy.cs` (new) | `public sealed class WebhookIntegrationRevokedFailurePolicy : IHandlerPolicy`, mirroring `WallSceneSwitchFailurePolicy`: find the chain whose `MessageType == typeof(WebhookIntegrationRevokedV1)`; `chain?.OnException<WebhookClientDisableFailedException>().ScheduleRetry(1m, 2m, 5m ×12)`. Doc comment carries §3's reasoning in brief, the dead-letter-after-ladder fact, and why `ScheduleRetry` over `RetryWithCooldown`. |
| C4 | `src/Identity/Infrastructure/IdentityInfrastructureModule.cs` | Add `public static void ConfigureMessageHandling(WolverineOptions options)` (guard with `Ensure.That`, then `options.Policies.Add<WebhookIntegrationRevokedFailurePolicy>()`), and pass `configureMore: ConfigureMessageHandling` at `:162-165`. |

Why C4 is a named method rather than a lambda: spec 296's test registers its policy itself, so
deleting the production registration line leaves that test green — its doc claims otherwise.
Exposing the exact delegate production passes lets the test call the same code, so removing the
`Policies.Add` fails the test. The residual (deleting the `configureMore:` argument itself) is
caught only by phase 5's live run; recorded in §8.

`ConfigureMessageHandling` is public because Identity.Infrastructure has no `InternalsVisibleTo`
and the module already exposes public statics (`AddKeycloakAdminClient`).

Untouched by design: `WolverineDefaults.cs` (no shared default), every other chain, the
`WebhookClientDisableFailed` log (still once per attempt), `DisableWebhookClientCommandHandler`,
`Shared.Contracts`.

## 5. Tests — what is red first and what stays green

The configuration cannot be proven through the Aspire stack in CI: provoking it means taking
Keycloak away from the shared `AspireFixture` collection (breaks every other Identity test) and
waiting minutes per slot. Wolverine's in-process chain compilation (spec 296's technique:
`UseWolverine` + `StubAllExternalTransports()` + no `PersistMessagesWithPostgresql`, then read
`HandlerGraph`) proves it exactly and in milliseconds. The live behaviour is observed once, at
phase 5.

### 5.0 Behaviour-free seams first (so 4a can be compile-clean red)

Every commit must build on its own (ADR-0087, CLAUDE.md). The red tests need three symbols that do
not exist. So before 4a, the engineer lands, in one `refactor` commit, **without behaviour**:
C1 (the exception, unused), C3 with an **empty** `Apply` body, and C4 (registration of the empty
policy). Characterisation: `Identity.Application.Tests` + `Identity.Infrastructure.Tests` green
before and after, unmodified. An empty policy adds no rule, and nothing throws the new type yet.

### 5.1 Phase 4a — red (test-writer)

**Test 1 — `tests/Identity.Infrastructure.Tests/WebhookIntegrations/WebhookIntegrationRevokedFailurePolicyTests.cs` (new).**
Discovery helper mirrors `WallSceneSwitchFailurePolicyTests.DiscoverAsync`, but:
`opts.Discovery.IncludeAssembly(typeof(WebhookIntegrationRevokedIntegrationEventHandler).Assembly)`,
then **`IdentityInfrastructureModule.ConfigureMessageHandling(opts)`** (not
`opts.Policies.Add<…>()` — §4), then `opts.StubAllExternalTransports()`.

| Fact | Asserts | Red today because |
|---|---|---|
| `A_failed_disable_is_retried_after_one_minute_then_two_then_five_minutes_until_attempt_fourteen` | single rule on the chain; for `Attempts` 1..14, `TryCreateContinuation` returns true, the continuation's type name is `ScheduledRetryContinuation` and its `Delay` (read by reflection — public property, internal type; culture-free unlike `ToString()`) equals the **hard-coded** expected list `[1m, 2m, 5m×12]` | `chain.Failures` is empty |
| `The_fifteenth_failed_attempt_is_dead_lettered` | `Attempts = 15` → continuation type name `MoveToErrorQueue` | empty |
| `The_rule_matches_the_disable_failure_and_nothing_wider` | matches `new WebhookClientDisableFailedException("w", "KEYCLOAK_UNAVAILABLE")`; does **not** match `new InvalidOperationException()`, `new DbUpdateException()`, `new OperationCanceledException()` | empty (`ShouldHaveSingleItem` fails) |
| `The_exception_the_handler_throws_on_KeycloakUnavailable_is_the_one_the_rule_matches` | run the real `WebhookIntegrationRevokedIntegrationEventHandler` with a stub `ICommandHandler<DisableWebhookClientCommand, …>` returning `KeycloakUnavailable`; catch what it throws; `rule.Match.Matches(caught)` | empty; and the handler still throws the base type |
| `No_other_discovered_chain_gained_a_failure_rule` | every other chain's `Failures` empty; the set of others is non-empty | **green before and after** — scoping guard, not a red obligation (say so in the PR) |

The expected ladder is written out in the test as literals. It must **not** be read from the
policy (an assertion must not check its own input).

**Test 2 — `tests/Identity.Application.Tests/EventHandlers/WebhookIntegrationRevokedIntegrationEventHandlerTests.cs` (append one fact, touch no existing one).**
`KeycloakUnavailable_throws_the_disable_failure_the_retry_policy_matches`:
`(await Should.ThrowAsync<Exception>(…)).ShouldBeOfType<WebhookClientDisableFailedException>()`.
Red today: the handler throws `InvalidOperationException`. The existing
`KeycloakUnavailable_throws_so_Wolverine_retries` stays green before and after (Shouldly's
`ThrowAsync<T>` is an `is T` check — verified in Shouldly 4.3.0 `Should.cs` — and the new type
derives `InvalidOperationException`).

**Required 4a outcome:** four facts in Test 1 and the new fact in Test 2 red **on assertion** for the
reasons above; the scoping fact and every pre-existing Identity test green; zero compile errors.
Anything else → stop and report.

### 5.2 Phase 4b — counterfactuals (engineer, after green; quote each, then revert)

1. Replace `ScheduleRetry` with `RetryWithCooldown` (same delays) → ladder fact red on type name.
2. Change one 5 min to 10 min → ladder fact red on that attempt's delay.
3. Remove the `Policies.Add` from `ConfigureMessageHandling` → all four red facts red.
4. Revert C2 to `throw new InvalidOperationException(…)` → link fact and Test 2 red.

No shard-filter entry is needed: both projects are unit-tier, run by `coverage-check.ps1`, not
`Integration.Tests`.

## 6. Commit plan (each commit builds on its own; ADR-0087, ADR-0030)

1. `refactor(identity): add the seams for a webhook-revocation failure policy` — C1, C3 (empty
   body), C4. Builds; all tests pass.
2. `test(identity): prove a Keycloak outage dead-letters the webhook revocation within the default budget`
   — Tests 1 and 2. Builds; the named facts fail (accepted red/green pair, CLAUDE.md "Builds is not
   passes").
3. `fix(2629): retry the webhook-client disable on a 1-2-5 minute ladder before dead-lettering`
   — C2, C3's body, doc comments. Builds; all tests pass.

## 7. Fallback if D1 is overruled

`RetryWithCooldown(<same delays>)` in C3; in Test 1 the expected continuation type becomes
`RetryInlineContinuation` (also has a public `TimeSpan? Delay`). Accept, in the spec, the listener
hold and the shutdown/restart behaviour §2 describes.

## 8. Risks

- **Discovery needs registrations at startup.** Spec 296's probe showed chains compile lazily, so
  Identity's handlers' DI dependencies should not be needed to *read* `HandlerGraph`. If Identity's
  Application assembly carries something Wolverine resolves at `StartAsync`, the test-writer reports
  it rather than registering production services in the test.
- **Registration escape.** Deleting `configureMore: ConfigureMessageHandling` at the call site is
  not caught by any unit test (the call needs a broker and a database). Phase 5 catches it; a
  reviewer should check the line.
- **Attempt-count persistence across reschedules (spec A2).** If a reschedule reset `Attempts`, the
  ladder would loop at slot 1 indefinitely rather than dead-letter — safer than today, but not what
  is specified. Phase 5 records the slots observed.
- **Revocation while Keycloak is down and a rotate races it** — spec 318's territory; a rotate's own
  revoke-race disable (`RotateWebhookClientCommandHandler.cs:317`) is unaffected.

## 9. Verification (phase 5)

Spec §Independent end-to-end procedure. Record: the old-budget measurement (step 7, settles A1),
which ladder slots fired and when (A2), the post-unpause time to `DisabledWebhookClient`, and the
revoked-client-list entry. Latency: N/A.
