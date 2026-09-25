# Plan 254: The race the catch mislabels

**Spec**: [spec.md](./spec.md) · **Issue**: #2570 · **Engineer**: `backend-engineer` (phase 4a:
`test-writer`, then `test-adversary` on the race harness)

## 1. Scope and placement

- **Bounded context**: Identity. **Layer**: Application (one command handler). No Domain, Api,
  Infrastructure, `Shared.Contracts`, AppHost, migration or messaging change.
- **Production change: one `when` filter**, in
  `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:139-140`.
  Add `and not DbUpdateConcurrencyException` (`Microsoft.EntityFrameworkCore`; `Identity.Application`
  already references the package in its csproj, and `BoundaryTests` forbids EF in *Domain* only).
  Add one *why* comment line: the Layer-2 loser belongs to `ConcurrencyConflictExceptionHandler`
  (ADR-0113), not to this catch.
- Entities / value objects / invariants: unchanged. `RegisteredClient.Version` stays the Layer-2 token.
- Messaging: unchanged. The loser throws before `events.PublishAsync`, so it publishes nothing, as it
  should. Its `SaveAsync` already rolled back inside `SaveChangesAndFlushMessagesAsync`.
- Boundary rules: unaffected. No cross-context reference.
- Latency: N/A (constitution §IV).

## 2. Colour

**Behaviour-changing → RED first** (ADR-0139, CLAUDE.md phase 4a). This fixes the classification of a
real outcome (502 → 409). AS-1 and AS-4 must arrive red on unchanged `src/`, quoted verbatim.
AS-2 and AS-3 are existing tests that must stay green **unmodified** (FR-002). If one of them has to
be edited, the behaviour moved: block, don't adjust.

## 3. Tests (phase 4a)

### T-A: AS-1, genuine Layer-2 race (the confirmation). File: `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`

Mirror `EventTypeRegistryConcurrencyIntegrationTests.Simultaneous_registrations_of_one_kind_leave_exactly_one_entry`
(`Racers` requests dispatched with `Task.WhenAll`, every answer described in the assertion message).
Reuse this class's own helpers: `CreateAsync`, `Conditional`, `ProblemAsync`, `CanAuthenticateAsync`,
`FindWebhookAsync`, `UniqueIntegrationName`, `DiagnoseAsync`.

Shape. The engineer picks the constants and states them. Starting points: `Racers = 6`, `MaxRounds = 10`.

```
name = UniqueIntegrationName(); version = await CreateAsync(identity, name)
for round in 1..MaxRounds:
    answers = await Task.WhenAll(Racers × identity.SendAsync(Conditional(name, version, Body())))
    record (status, problem title) of every answer
    exactly one 200 per round            -> its (version, clientSecret) is the round's winner
    every non-200 is 409 with title in { WEBHOOK_CLIENT_STALE, AGGREGATE_VERSION_STALE }
                                           (a 502 KEYCLOAK_UNAVAILABLE here is the defect)
    CanAuthenticateAsync(webhook-name, winner.secret) is true
    if any AGGREGATE_VERSION_STALE: layer2Observed = true; break
    version = winner.version
layer2Observed.ShouldBeTrue("inconclusive: no racer reached Layer 2 in N rounds …")   (FR-006)
```

Each `HttpClient`: one `aspire.CreateAdminClientAsync("identity")` shared across racers is fine. HTTP/1.1
opens parallel connections. Do **not** send `Idempotency-Key` (A3). Assertion messages say what a red
*means*: a 502 is spec 254's defect; a 500 means the exception arrived wrapped (A1, so stop); an
inconclusive run means the harness never reached Layer 2 (A2, so report, do not tune blindly).

Update the class doc's first paragraph: it now covers Layer 2 for rotation as well as Layer 1. Do not
rewrite the other paragraphs.

**Expected pre-fix red**: a round containing a 502 `KEYCLOAK_UNAVAILABLE`, failing the "every non-200 is
409" assertion. Only that red confirms the issue. An inconclusive red or a 500 is a stop-and-report
(spec §5).

**Adversary brief (test-adversary)**: attack the harness, not the handler. Can the test pass on buggy
code? (For example, the round loop breaking before asserting a 502, or the 502 check running after
the `break`.) Is the winner's secret checked in every round? Does the inconclusive branch fail rather
than skip? Does the unique name keep it isolated from parallel tests? Prove the vacuity guard by
counterfactual: set `Racers = 1` temporarily and observe the inconclusive message.

### T-B: AS-4, handler contract (deterministic supplement). File: `tests/Identity.Application.Tests/Commands/RotateWebhookClientCommandHandlerTests.cs`

- Fake knob: `tests/Identity.Application.Tests/Fakes/InMemoryRegisteredClientRepository.cs` gets a
  one-shot `FailNextSaveWith` (`Exception?`) property. The precedent is `FakeKeycloakAdminClient.FailNextCall`.
  It is test infrastructure, so it belongs to the test-writer.
- Test `A_rotation_that_loses_the_database_race_lets_the_concurrency_exception_reach_the_middleware`:
  seed a client at version 3, command `Option<int>.Some(3)`, set `FailNextSaveWith = new DbUpdateConcurrencyException("…")`.
  Then `Should.ThrowAsync<DbUpdateConcurrencyException>` on `HandleAsync`, and assert the fake Keycloak
  recorded **no** secret rotation. (Check `FakeKeycloakAdminClient`'s existing call-recording members
  first; reuse them, don't add new ones unless none exist.)
- Expected pre-fix red: `Should.ThrowAsync` fails because the handler *returned* a `KeycloakUnavailable`
  result instead of throwing.
- This test alone does **not** confirm the issue (ADR-0113: a mocked throw proves nothing about the EF
  wiring). It pins the handler contract cheaply and runs without Docker.

### Existing tests that must pass unmodified (characterisation for FR-002)

`RotateWebhookClientCommandHandlerTests` (all, including `Keycloak_transport_failure_returns_KeycloakUnavailable`),
`StaleVersionRejectionTests`, `RegisteredClientConcurrencyIntegrationTests` (existing 7 facts),
`ConcurrencyConflictDeclarationTests`, `CrossFabWebhookRotationIntegrationTests`.

## 4. The fix (phase 4b, `backend-engineer`)

```csharp
catch (Exception ex) when (ex is not OperationCanceledException
                           and not InvalidOperationException
                           and not DbUpdateConcurrencyException)
```

plus `using Microsoft.EntityFrameworkCore;` and one comment line on why. Nothing else. The engineer may
not edit T-A/T-B to reach green.

**Counterfactual (FR-004 / "prove a guard")**: after green, temporarily remove the new
`and not DbUpdateConcurrencyException`. Run T-A and T-B, quote both red, revert, and confirm with
`git diff` that only the intended lines differ.

## 5. Constitution / ADR check

- **ADR-0113**: implements its "one shared mapping" rule for a handler that pre-empted it. It also
  follows ADR-0113's "verification is behavioural" (T-A over the real stack).
- **ADR-0047**: the infrastructure signal is returned to middleware. The handler's `Result` keeps only
  domain and Keycloak failures.
- **ADR-0105 / §II / NRT**: no new guards, primitives or parameters.
- **ADR-0036**: one filter clause. Rejected alternatives (widening to `DbUpdateException`,
  restructuring the `try`) are in spec §4.
- **ADR-0144**: no ADR is written, and no gate is weakened. `ConcurrencyConflictDeclarationTests`
  already declares this route's lost-update 409, so no register row changes.
- **No new ADR required**: the decision (ADR-0113's middleware mapping) exists. This spec conforms one
  handler to it.

## 6. Orchestrator work (no code)

- Board gate: verify #2570 on Project #13 by `content.url`, `--limit 2000`.
- PR body: `Closes #2570`. State that the issue's wrapping mechanism was refuted (spec §1) and name the
  real one. Quote T-A's and T-B's pre-fix reds and the counterfactual.
- Optionally file spec §6 O1 (board-checked first, no `agent:ready`). O2 is recorded only.
