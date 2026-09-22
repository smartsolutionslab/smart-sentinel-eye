# Tasks — Spec 213, the reason a rejection already had

**Spec:** `specs/213-the-reason-a-rejection-already-had/spec.md`
**Plan:** `specs/213-the-reason-a-rejection-already-had/plan.md`
**Issue:** #2428 (feature-level; add it to Project #13 by hand — `/speckit-tasks` adds nothing to the board)
**Branch:** `2428-dead-letter-fabricated-reason` (worktree `D:/Github/sse-2428`, base `origin/develop` @ `c4d74b4b`)
**Phase-4a colour:** **RED** — behaviour-changing. New tests must be observed **failing**, and the verbatim output quoted in the PR (ADR-0139, ADR-0144).
**Scope:** backend only, one bounded context (`src/EventIngestion`). No frontend, no migration, no `Shared.Contracts`.

---

## Ordering

```
US1 (P1)   T001 [P] ─┐
           T002 [P] ─┼─► T004 ─► T005 ─► T006 ─► T007
           T003 [P] ─┘   (4b)    (verify) (QA)   (PR)

US2 (P2)   T008 ─► T009 ─► T010        (separate PR, after US1 lands)
```

`T001`–`T003` are the phase-4a red set and **all three must be red-observed before `T004` touches production code.** `T004` is the only production edit in US1.

**Nothing here is foundational.** No `Shared.Kernel`, no `Shared.Contracts`, no AppHost resource, no migration — so there is nothing for an orchestrator to fan out beyond the `[P]` set below, and no other in-flight spec is blocked by this one.

## Parallelism (ADR-0109)

`T001`, `T002` and `T003` own **disjoint files** and are marked `[P]` — one Application test file, one Infrastructure test file, one Integration test file. They can run as three `test-writer`/`test-adversary` passes at once or as one pass; either satisfies the gate provided **each file's colour is reported separately**. A single colour for "the test run" is not a sufficient report.

Everything from `T004` on is strictly serial: one production file set, one reviewer, one PR.

---

## US1 (P1) — A dead letter says why the delivery was actually refused

### Phase 4a — RED (`test-writer`, with `test-adversary` welcome on T002)

#### `[T001] [P] [US1]` — Application: the batch reports the reason it refused on

**File (sole owner):** `tests/EventIngestion.Application.Tests/Commands/IngestEventBatchCommandHandlerTests.cs`

**The one permitted edit to an existing assertion.** Line 115 today reads:

```csharp
result.Refused.ShouldHaveSingleItem().Identifier.ShouldBe(skewed.Identifier);
```

It may gain `.Envelope` and may gain a further claim. **It may not lose the claim it makes.** Rewrite it as:

```csharp
RefusedEnvelope refused = result.Refused.ShouldHaveSingleItem();
refused.Envelope.Identifier.ShouldBe(skewed.Identifier);
refused.Reason.ShouldBeOfType<IngestEventError.OccurredAtTooFarInFuture>();
refused.Reason.Code.ShouldBe("EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE");
```

Do not touch the `ShouldBeEmpty()` assertions at `:52`, `:73`, `:94` — they compile unchanged and must stay exactly as they are.

Add one further test:

| Scenario | Assert | Expected today |
|---|---|---|
| US1-A1 — a batch with **two** skewed envelopes and one healthy one | `Refused` has two entries, each paired with **its own** envelope's identifier; the healthy envelope is absent | **RED** |

US1-A1 exists because a single-refusal test passes against an implementation that pairs every refusal with the *first* error it saw. Write it adjacent to the rewritten case with a comment saying so.

**Report:** `dotnet test tests/EventIngestion.Application.Tests --filter IngestEventBatchCommandHandlerTests` — return the **verbatim** output. State per test which are red and which are green-by-design.

---

#### `[T002] [P] [US1]` — Infrastructure: three endings, three different reasons

**File (sole owner):** `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`

**Do not modify** `Records_and_releases_a_delivery_that_never_stores` (`:69-81`). Its `ShouldContain("not storable after")` covers the `:215` site and **must stay green unmodified** — it is the standing check that the window sentence did not move or change while its call site was rewritten. If it goes red, that is a defect in `T004`, not a test to adjust.

The harness needs one extension: `ScriptedEventRepository` currently fails only by **throwing**, which reaches `Outcome.Failed`. Reaching the `:368` ending needs a delivery whose *single* store returns a non-`EventAlreadyIngested` failure without throwing — which the skewed envelope already does via `IngestEventCommandHandler`'s `catch (ArgumentException)` at `:54-59`. So `:368` is reached by `Skewed(...)` **plus** a batch that threw: set `PoisonPayload` so the batch save fails, and put a skewed delivery in the same batch.

| ID | Scenario | Assert | Expected today |
|---|---|---|---|
| US1-B | **`:194`** — `Skewed(...)` alone, batch succeeds | the single dead letter's `Error.Value` contains `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE` **and** `more than 5 minutes in the future`, and does **not** contain `of retrying` | **RED** |
| US1-C | **`:368`** — `Skewed(...)` in a batch with a poisoned delivery, so the batch throws and the loop falls to singles | the skewed delivery's dead letter contains the code and does **not** contain `of retrying`; the delivery is abandoned exactly once | **RED** |
| US1-D | **`:215` counterfactual, in the same file** — a delivery whose save always throws | its dead letter **does** contain `not storable after` and names the window | green today, **must stay green** |
| US1-E | **conflict** — a delivery whose event is already in the repository, stored via the singles path | **no** dead letter is recorded; the delivery is completed as stored | green today, **must stay green** |
| US1-F | **bad request / the 512 bound** — a rejection whose composed reason would exceed `RejectionReason.MaximumLength` | a dead letter **is** recorded, its `Error.Value.Length` is `<= 512`, and the delivery is abandoned (not left on `carried`) | **RED** |

**US1-C's reachability is confirmed, not assumed.** `Build` never calls `events.Add` for the skewed envelope, so the batch's pending set holds only the poisoned delivery; its `SaveAsync` throws, `TryStoreBatchAsync` returns `None`, and `RetryAsync` then runs over **all** arrived deliveries including the skewed one — whose single store returns `Failure(OccurredAtTooFarInFuture)` without throwing. That is `:368`.

**US1-E needs a redelivery, so it needs `ChannelOverride`.** `ScriptedEventRepository.ExistsAsync` reads `stored`, which only fills after a successful save, and `OneBatchChannel` serves a single batch. Use a `BoundedIngestChannel` and write the same envelope twice, the second after the first has been stored — the pattern `An_event_arriving_behind_a_failing_one_does_not_wait_for_it` already uses for mid-run writes.

**US1-B and US1-D are the load-bearing pair.** An implementation that replaced the window sentence everywhere passes B and fails D; one that changed nothing passes D and fails B. Write them adjacent with a comment saying so, so a later reader cannot delete either as redundant.

**US1-F needs an over-long message.** The existing `IngestEventError` variants cannot produce one (`occurredAt` is a fixed-width timestamp), so drive it through a delivery whose reason composition is forced long — e.g. a locally-declared test-only `IngestEventError` subtype with a 600-character `Message`, injected via a fake handler, or by asserting the composition helper directly if `T004` exposes it as `internal` with `InternalsVisibleTo`. **Pick whichever needs no production-code shape invented for the test's benefit**; if neither is reachable without one, say so in the report rather than inventing a seam.

**Report:** `dotnet test tests/EventIngestion.Infrastructure.Tests --filter PersistenceLoopHostedServiceTests` — **verbatim** output, colour stated per test.

---

#### `[T003] [P] [US1]` — Integration: two rows, two different reasons, one listing

**File (sole owner):** `tests/Integration.Tests/EventIngestion/DeadLetterReasonIntegrationTests.cs` (new)

**Do not modify** `PoisonDeliveryEscapeIntegrationTests.cs`. Its `error.ShouldContain("not storable")` (`:73`) covers the `:215` site against the real stack and must stay green unmodified.

Against the Aspire fixture (ADR-0103; `[Collection(AspireCollection.Name)]`), mirroring `PoisonDeliveryEscapeIntegrationTests`' helpers for publishing and for polling `dead_letters`:

1. Publish one MQTT event to `fab/munich/plc/clock-drift-1` with `occurredAt = UtcNow + 1 hour` and a unique `kind` token.
2. Poll `dead_letters` for a row whose `raw_payload` contains that token.
3. Assert its `error` contains `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE` and does **not** contain `of retrying`. → **RED**
4. Assert **exactly one** row exists for that token after a further 30 s — proving the delivery was acknowledged, not carried. (An over-long or throwing reason would produce a carried delivery; this is the observable difference.)
5. **Auth scenario:** call `GET /dead-letters` with a token holding `sse.events.read` but **only** fab `hamburg`, and assert the munich row is absent. Green today; re-asserted because the reason now embeds a device's `occurredAt` and the scoping is carrying more than it was.

**Report:** verbatim output, colour per assertion group.

---

### Phase 4b — implement (`backend-engineer`)

#### `[T004] [US1]` — Carry the typed reason from each refusal to the writer

**Brief:** the verbatim red output from `T001`, `T002` and `T003`. **You may not edit those tests to make them pass.** If a test looks wrong, stop and report it.

**Files (all of them, and only these):**

| File | Change |
|---|---|
| `src/EventIngestion/Application/Commands/RefusedEnvelope.cs` | **New.** `public sealed record RefusedEnvelope(EventEnvelope Envelope, IngestEventError Reason);` with a doc comment saying why the reason travels with the envelope. |
| `src/EventIngestion/Application/Commands/IngestEventBatchResult.cs` | `IReadOnlyList<EventEnvelope> Refused` → `IReadOnlyList<RefusedEnvelope> Refused`. Update the existing doc comment's last paragraph to say the refusals now carry *why*. |
| `src/EventIngestion/Application/Commands/Handlers/IngestEventBatchCommandHandler.cs` | `Build` returns `Result<EventAggregate, IngestEventError>` (ADR-0047). The catch constructs the error **once**, logs `.Code`, returns `Failure(reason)`. `List<EventEnvelope> refused` → `List<RefusedEnvelope>`. |
| `src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs` | Add the private `Ending` record struct; `StoreOneAsync` returns `Ending`; `CompleteAsync` takes `Ending`; `RecordRejectionAsync` takes a `RejectionReason` and **loses its `retry.Value` read**; add the private `Because` composer. The three call sites per the plan. |
| `src/EventIngestion/Infrastructure/Log.cs` | `IngestAbandoned`'s `TimeSpan window` → `string reason`; update the message template to state the reason. |

**Hard constraints:**

- `RecordRejectionAsync` must contain **no reference to `retry.Value`** when you are done. That deletion is the fix; grep for it before reporting.
- `Ending.Rejected` takes a `RejectionReason` and there is **no** way to construct a rejected ending without one. Do not add an overload, a default, or a nullable.
- The `EventAlreadyIngested` → `Ending.Stored` branch keeps its meaning and its comment. Composing a reason on that branch is a new defect.
- The `:215` sentence is `$"not storable after {window} of retrying"` — **character-for-character unchanged**, moved to `RetryAsync`.
- `Because` truncates to `RejectionReason.MaximumLength` rather than letting `From` throw. The plan states why; put a one-line *why* comment at the truncation, not a *what* comment.
- House rules: no leading underscore on private fields; `Ensure.That` for any argument guard (ADR-0105); explicit collection types with collection expressions (`Dictionary<...> refused = ...` via `ToDictionary` is fine); `CancellationToken` last (ADR-0049).

**Done when:** every `T001`–`T003` test is green **without any of them having been edited**, and `Records_and_releases_a_delivery_that_never_stores` plus `PoisonDeliveryEscapeIntegrationTests` are green **unmodified**.

---

### Phase 5 — verify

#### `[T005] [US1]` — Observe it end to end

Run the spec's **Independent end-to-end test procedure** in full against a booted Aspire stack, including **step 7's counterfactual** — a `:194` row and a `:215` row present in the **same** `GET /dead-letters` listing with **different** reasons. A run that only shows the new reason does not distinguish this fix from one that replaced the sentence everywhere.

Write `specs/213-the-reason-a-rejection-already-had/verification.md` with both `error` values quoted verbatim, and the count check from step 6.

Latency: **N/A** — cite the spec's §Latency section. No leg figure is claimed or owed.

Stack notes that bite here: one Aspire stack per machine; mint tokens from the **proxied** Keycloak endpoint; `dotnet run` in the background swallows the dashboard token — boot with the anonymous flag.

---

### Phase 6 — QA

#### `[T006] [US1]` — Review

`/code-review` on the diff, plus a `backend-reviewer` pass. Security review **not** required — no new trust boundary, no new endpoint, no auth change; the one auth-adjacent fact (a richer reason inside a fab-scoped row) is covered by `T003`'s step 5.

Specific things to put in front of the reviewer:

- Did any test get edited to pass? (`git diff` the three test files against the red commit.)
- Does `RecordRejectionAsync` still read `retry.Value`?
- Is the `:215` sentence byte-identical?
- Coverage gates: Domain ≥ 90%, Application ≥ 80% (ADR-0065). Application gained a record and changed a handler — check, do not assume.

---

### Phase 7 — PR

#### `[T007] [US1]` — Open the PR

`gh pr create --base develop`. Body must carry:

- The **verbatim red output** from `T001`–`T003` (ADR-0139 — the only form of the evidence a later reader can check).
- The two dead-letter `error` values from `T005`, side by side.
- `Phase X: skipped` lines for nothing — all seven phases run.
- Conventional Commits, no `Co-Authored-By` (ADR-0030, ADR-0086).
- A closing keyword for #2428, and **check the issue state after the merge** — a mention alone usually does not close it.

Commits must each build on their own (rebase-merge lands them individually). The natural split is: (1) the Application shape, (2) the loop, (3) the log line — but only if each compiles alone. If it does not, one commit.

---

## US2 (P2) — The exhausted row names its last failure

**Ship after US1 has landed, as its own PR.** Do not fold it into US1's PR — US1 is the reported defect and should not wait on this.

#### `[T008] [US2]` — RED: the exhausted row names its last failure

**File (sole owner):** `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs`

One test: a delivery whose save always throws with a distinctive message; assert the dead letter's `Error.Value` contains `not storable after` **and** that message. **RED.**

`Records_and_releases_a_delivery_that_never_stores` and `PoisonDeliveryEscapeIntegrationTests:73` both use `ShouldContain`, so appending keeps them green — confirm that rather than assume it.

#### `[T009] [US2]` — Implement the append

`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs` only. `failingSince`'s value becomes `IngestFailure(DateTimeOffset Since, string LastError)`; `NoteFailure` takes the exception and records `ex.Message` (not `ToString()` — a stack trace would crowd a 512-char column); `Exhausted` reads `.Since`; the `:215` reason appends `; last failure: {lastError}` through the same truncating `Because`-style path. `NoteFailure` has one caller (`:372`), so the signature change is local.

#### `[T010] [US2]` — Verify, review, PR

Same shape as `T005`–`T007`, scoped to the one new observation: a genuinely-exhausted row now names what it kept failing on.

---

## Not in this feature — file separately if wanted

The pattern sweep (spec §Does this pattern appear elsewhere) cleared the rest of the repo but surfaced four adjacent weaknesses. **None is in scope here**; they are listed so they are not read as this spec's omissions:

- `StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs:140` — a typed `Result` discarded entirely, not logged, not stored. The strongest of the four, and a genuine silent-failure bug.
- `EventIngestion/Api/EventsEndpoints.Writes.cs:366-375` — `catch (Exception ex)` binds `ex` and never uses it.
- `StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:111,126` — literal `last_error` sentences (synthesised from nothing, not substituted for something better).
- `StreamDistribution/Application/Commands/Handlers/ReportStreamHealthCommandHandler.cs:35,43` — generic sentences on the `??` branch only.

Check the board for an existing issue before filing any of these — two issues naming one file without cross-referencing has happened here before.
