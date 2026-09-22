# Spec 213 — The reason a rejection already had

**Issue:** #2428 — *Every dead-lettered event records the same fabricated 'not storable after retrying' reason, even when it was refused outright on the first attempt*
**Branch:** `2428-dead-letter-fabricated-reason` (worktree `D:/Github/sse-2428`)
**Base:** `origin/develop` @ `c4d74b4b`
**Phase-4a colour:** **RED** (behaviour-changing). The `error` column of a dead-letter row — and the `GET /dead-letters` payload built from it — changes content for two of the three paths that write one. A test that arrives green is a phase-4 failure, not a shortcut (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0047** (`Result<T, Error>` — the shape the refusal is carried in), **ADR-0089** (`ApiError(Code, Message, HttpStatusCode)` — the typed error already holds both halves of the sentence this spec needs), ADR-0038/0046/0066 (hand-written value objects; `RejectionReason` is one), ADR-0141 (`Option<T>` for absences), ADR-0105 (`Ensure.That`), ADR-0093 (Application folder layout — where `RefusedEnvelope` lands), ADR-0050 (`[LoggerMessage]` source-gen), ADR-0052/0053/0054 (xUnit + Shouldly, sentence-style names, hand-written builders), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0036 (smallest possible change), ADR-0037 (phases), ADR-0109 (`[P]` markers), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane).
**Prior specs this amends:** spec 006 FR-014/FR-015 (the future-skew rule and the dead-letter capture it feeds), spec 018 FR-008 (record before release), spec 020 FR-008/FR-009/FR-010 (the batch, the singles fallback and the retry window this loop is built from).
**Constitution:** §II (value objects — no new primitive reaches a domain model), §Testing (red for new behaviour), §IV (latency budget — **N/A**, see below).

**No new ADR is required.** Nothing here decides architecture. Every type this spec moves a value through already exists and is already the repo's stated convention: `Result<T, IngestEventError>` (ADR-0047), `ApiError`'s `Code`/`Message` pair (ADR-0089), `RejectionReason` (ADR-0038). No new dependency, no new pattern, no new bounded context, no contract in `Shared.Contracts`.

---

## Problem

Every line number, type, signature and test assertion below was **re-read in the working tree at HEAD `c4d74b4b`**. The line numbers the issue quotes are still exact.

### One sentence, written from three places, true in one

`src/EventIngestion/Infrastructure/Ingress/PersistenceLoopHostedService.cs:287-304` — `RecordRejectionAsync` takes a delivery and nothing else, and builds the reason from a configuration value:

```csharp
private async Task<bool> RecordRejectionAsync(
    IngestDelivery delivery, CancellationToken cancellationToken)
{
    EventEnvelope envelope = delivery.Envelope;
    TimeSpan window = retry.Value.MaximumRetryWindow;
    ...
        RejectionReason.From($"not storable after {window} of retrying"),
```

`window` is `IngestRetryOptions.MaximumRetryWindow` — a setting. It is not evidence about *this* delivery. It is the same string for every row regardless of what happened, and `RecordRejectionAsync` is the sole writer for all three endings that reach it.

The three call sites, confirmed:

| Site | Path | Did it retry? | What it knew and threw away |
|---|---|---|---|
| `:194` | `StoreArrivalsAsync` — batch reported the envelope `Refused` | **No.** First pass, refused during the build. | `IngestEventFailures.OccurredAtTooFarInFuture(...)`, constructed at `IngestEventBatchCommandHandler.cs:153` purely to log `.Code`, then dropped. |
| `:368` | `StoreOneAsync` — a non-`EventAlreadyIngested` failure | **No.** Rejected on the attempt that produced it. | `result.Error` — an `IngestEventError`, logged at `:360` as `.Code`, then discarded by the ternary at `:366-368`. |
| `:215` | `RetryAsync` — `Exhausted(...)` is true | **Yes.** This is the only site the sentence describes truthfully. | Nothing typed; the failures here are thrown exceptions. |

### The typed reason exists at both discard sites and is provably available

`IngestEventBatchCommandHandler.Build` (`:133-156`) returns `Option<EventAggregate>` — a shape that can say *whether* it failed and structurally cannot say *why*:

```csharp
catch (ArgumentException)
{
    logger.BatchEnvelopeRejected(
        envelope.Identifier, envelope.Source, envelope.Device,
        IngestEventFailures.OccurredAtTooFarInFuture(envelope.OccurredAt.Value).Code);
    return Option<EventAggregate>.None;      // the error is constructed, read for .Code, and dropped here
}
```

`IngestEventBatchResult` (`src/EventIngestion/Application/Commands/IngestEventBatchResult.cs:18`) is, in full:

```csharp
public sealed record IngestEventBatchResult(IReadOnlyList<EventEnvelope> Refused);
```

**Confirmed: there is no field carrying a reason today.** The issue's claim is exact.

`StoreOneAsync` (`:352-368`) holds a `Result<EventIdentifier, IngestEventError>`. `IngestEventError` derives from `ApiError(Code, Message, HttpStatusCode)` (`IngestEventErrors.cs:6`), so at the moment of discard the code holds *both*:

- `"EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE"`
- `"occurredAt 2026-09-22T11:04:00.0000000+00:00 is more than 5 minutes in the future; check the source's clock."`

The second string is, almost verbatim, the post-mortem `RejectionReason`'s doc comment asks for (`src/EventIngestion/Domain/DeadLetter/RejectionReason.cs:6-8`: *"Why a delivery was rejected, in terms an operator can post-mortem"*). It is computed, it is one variable away from the row, and it is thrown on the floor.

### What the operator sees

`ListDeadLettersQueryHandler.cs:34` projects `deadLetter.Error.Value` straight into `DeadLetterDto.Error`, and `GET /dead-letters` (`EventsEndpoints.cs:97`, scope `sse.events.read`, fab-scoped) returns it unchanged. Nothing between the row and the operator's screen reinterprets the string — so the row *is* the diagnosis.

**The concrete failure (the issue's, re-derived and confirmed reachable):** a PLC's clock is an hour fast. `Event.Ingest` throws `ArgumentException` on the future-skew rule for every one of its events. They are refused by the batch on the first pass, dead-lettered without a single retry, and every row reads:

> `not storable after 00:05:00 of retrying`

An operator reads a storage outage. The actual answer — *this device's clock is wrong* — was computed, logged once at debug/warn into a log stream nobody is grepping a week later, and left out of the only durable artefact built for exactly this question.

### The other dead-letter writer already does the right thing

`MqttSubscriberHostedService.cs:145` passes `result.Error ?? "unknown parse failure"` through to `RejectionReason.From(error)` at `:317` — a real reason from the parse that produced it. So the table already carries free-form, writer-supplied prose; **no consumer parses the string, and no format is assumed anywhere.** There is nothing to migrate and no existing row shape to preserve.

### Narrowing found while verifying — the issue's framing is right but wider than today's reachability

Worth recording because it changes what a test can assert, not what to build:

- **Today `:194` and `:368` can only ever carry one code.** `IngestEventError` has exactly two variants (`IngestEventErrors.cs:15,26`), and `EventAlreadyIngested` is routed to `Outcome.Stored` by the ternary at `:366`. So every reason these two sites can currently carry is `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE`. That does **not** argue for hard-coding it: the defect is that the mechanism cannot carry a reason at all, and the error catalogue is an open set (`RegisterEventTypeErrors`, `RetireEventTypeErrors` and others already exist in the same folder). Build the channel; today one value flows down it.
- **`:368` is reachable only via the singles fallback.** `StoreArrivalsAsync` reaches `RetryAsync` only when `TryStoreBatchAsync` returned `None` — i.e. the batch threw. So a skewed envelope in a batch that also contained a row Postgres refused goes down `:368`, and a skewed envelope in an otherwise-healthy batch goes down `:194`. Both are ordinary, and the two are **not** interchangeable in a test: `:194` needs a healthy batch containing a skewed envelope, `:368` needs a failing save plus a skewed envelope.

### Does this pattern appear elsewhere? Swept, and it does not

A repo-wide sweep for the same shape — *a typed error computed, then only logged or dropped, while the durable artefact an operator later reads gets a fabricated generic string* — returned a **clean negative outside this file**. The structural reason is that the whole solution has exactly **two** persisted failure-reason columns:

- `DeadLetter.Error` (`EventIngestion/Infrastructure/Persistence/Configurations/DeadLetterConfiguration.cs:46`) — this spec's subject.
- `Stream.LastError` (`StreamDistribution/Infrastructure/Persistence/Configurations/StreamConfiguration.cs:77`).

There is no outbox failure table of our own (Wolverine owns that), no saga-compensation record, no webhook-delivery record, and `AuditObservability` persists a serialised envelope rather than a free-text reason.

**The in-repo precedent for the fix is the sibling writer.** `MqttSubscriberHostedService.CaptureDeadLetterAsync` (`:296-318`) is handed the concrete failure text produced at `:204`/`:222`/`:232`/`:254` (`$"envelope parse failed: {ex.Message}"` and kin) and passes exactly that into `RejectionReason.From`. This spec makes the persistence loop do what the subscriber already does.

**Four adjacent weaknesses were surfaced and are deliberately out of scope**, recorded here so they are not rediscovered as this spec's omissions. None is the same defect; each deserves its own issue if anyone wants it:

1. `StreamDistribution/Infrastructure/HealthWatcher/StreamHealthWatcher.cs:140` — a `Result<StreamState, ReportStreamHealthError>` discarded entirely: not logged, not stored. A genuinely separate silent-failure bug, and the strongest of the four.
2. `EventIngestion/Api/EventsEndpoints.Writes.cs:366-375` — `catch (Exception ex) when (...)` binds `ex` and never uses it; the caller gets a hard-coded 503 sentence. Same bounded context, fully swallowed exception, but the substitution lands in an HTTP body rather than a durable row.
3. `StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs:111,126` — `"path not registered"` / `"not ready"` reach the `last_error` column as literals. Synthesised from nothing rather than substituted for something better: MediaMTX's `/v3/paths/get` returns no reason string.
4. `StreamDistribution/Application/Commands/Handlers/ReportStreamHealthCommandHandler.cs:35,43` — generic sentences on the `?? ` branch only, i.e. where no better text exists.

### Two existing assertions on the sentence — both survive, and that is checked, not assumed

| Assertion | Which site it actually exercises | Verdict |
|---|---|---|
| `tests/EventIngestion.Infrastructure.Tests/PersistenceLoopHostedServiceTests.cs:80` — `.Error.Value.ShouldContain("not storable after")` | The harness's `ScriptedEventRepository.SaveAsync` **throws** (`:405`, `InvalidOperationException("scripted persistence failure")`) with `FailuresBeforeSuccess = int.MaxValue`. A throw is caught at `:370` → `Outcome.Failed` → retried → `Exhausted` → **`:215`**. | Untouched by US1. Must stay green **unmodified**. |
| `tests/Integration.Tests/EventIngestion/PoisonDeliveryEscapeIntegrationTests.cs:73` — `error.ShouldContain("not storable")` | Drops the `events_hamburg` partition, so the insert raises `PostgresException` → caught at `:370` → `Outcome.Failed` → exhausted → **`:215`**. | Untouched by US1. Must stay green **unmodified**. Under US2 it stays green because US2 *appends* to the sentence rather than replacing it. |

**One existing assertion must be mechanically retargeted, and only mechanically.** `tests/EventIngestion.Application.Tests/Commands/IngestEventBatchCommandHandlerTests.cs:115`:

```csharp
result.Refused.ShouldHaveSingleItem().Identifier.ShouldBe(skewed.Identifier);
```

Widening `Refused`'s element type moves `Identifier` one hop to `.Envelope.Identifier`. That is a compile fix, not a behaviour adjustment — **the assertion may gain `.Envelope` and may gain a new claim about `.Reason`; it may not lose the claim it makes today.** Called out explicitly because "the test needed changing" is the shape under which a weakened gate normally arrives (ADR-0144: a deleted test or a lowered threshold is a blocked outcome).

### Latency budget — N/A

Constitution §IV's path is `event arrival → overlay rendered`. Nothing here is on it:

- `RecordRejectionAsync` runs **only** for a delivery that is never going to be stored, so it is off the happy path by construction — a stored event never enters it.
- On the `Event → overlay state` leg, the change to the ingestion path is `IngestEventBatchResult` carrying one extra reference per *refused* envelope. A batch with no refusals allocates the same empty list it does today.
- The reason string is now **read from an error that was already constructed** instead of being interpolated from `window`. Strictly less formatting work on the rejection path, unchanged work on the storage path.

No leg's figure is affected and no §VII dashboard obligation attaches.

---

## User stories

### US1 (P1) — A dead letter says why the delivery was actually refused

**As** a fab operator reading `GET /dead-letters` after a batch of events stopped arriving,
**I want** each row to state the rule that refused the delivery,
**so that** I can tell a device with a wrong clock from a database outage without reading a week of service logs.

**Independently shippable.** It changes two of three writers, needs no migration (the `error` column is unchanged — `varchar(512)`, `DeadLetterConfiguration.cs:46-50`), no contract change, no frontend, and leaves the third writer exactly as it is. Shipped alone it fixes the reported failure completely.

### US2 (P2) — The exhausted row names the failure it exhausted on

**As** the same operator, looking at a row that genuinely did retry for the whole window,
**I want** the last real failure appended to the window sentence,
**so that** "not storable" distinguishes a missing partition from a connection refusal.

**Separable on purpose.** US1 removes two fabrications; US2 makes the one *true* sentence informative. US2 touches a different field (`failingSince`'s value shape) and a different call site (`:215`), and can ship in a later PR without US1 being revisited — or be dropped entirely without leaving US1 half-done.

---

## Acceptance scenarios (Gherkin)

### US1 — happy path: the batch refusal (`:194`)

```gherkin
Scenario: an envelope the batch refuses records the rule that refused it
  Given a device whose clock is one hour ahead of the ingest service
  And the batch write for the arriving deliveries succeeds for every other envelope
  When the skewed envelope is drained by the persistence loop
  Then a dead letter is recorded for that delivery
  And its error contains "EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE"
  And its error contains "more than 5 minutes in the future"
  And its error does not contain "of retrying"
  And the delivery is acknowledged exactly once
```

### US1 — happy path: the first-attempt rejection on the singles path (`:368`)

```gherkin
Scenario: an envelope rejected on its first single attempt records the rule, not the window
  Given the batch write threw, so the loop is storing deliveries one at a time
  And one of those deliveries carries an occurredAt an hour in the future
  When the loop stores that delivery individually
  Then a dead letter is recorded for it
  And its error contains "EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE"
  And its error does not contain "of retrying"
  And nothing was retried for that delivery
```

### US1 — the site that must not change (`:215`)

```gherkin
Scenario: a delivery that genuinely exhausted the window keeps its window sentence
  Given every write for a delivery fails transiently
  When the retry window elapses for that delivery
  Then a dead letter is recorded for it
  And its error contains "not storable after"
  And its error names the retry window
```

### US1 — conflict: the redelivery that is not a rejection at all

```gherkin
Scenario: an already-ingested redelivery is stored, not dead-lettered
  Given an event that is already in the events table for its fab
  When the same delivery arrives again and is stored individually
  Then no dead letter is recorded
  And the delivery is acknowledged as stored
```

This is the ternary at `:366-368`. Carrying a reason must not tempt an implementation into dead-lettering `EventAlreadyIngested` — it is the idempotency rule working, and a row for it would be a new defect.

### US1 — bad request: the reason cannot break the writer

```gherkin
Scenario: an over-long reason does not strand the delivery
  Given a typed failure whose Code and Message together exceed 512 characters
  When the loop records the rejection
  Then the dead letter is written
  And its error is at most 512 characters
  And the delivery is acknowledged rather than carried forever
```

Non-hypothetical: `RejectionReason.From` runs `Ensure.That(value).HasMaxLength(512)` (`RejectionReason.cs:25-27`) and throws above it. That throw lands in `RecordRejectionAsync`'s catch (`:310`), which returns `false`, which puts the delivery back on `carried` (`:257`) — forever, because the reason will be over-long on every retry too. The bound is the EF column width (`DeadLetterConfiguration.cs:48`), so it is not negotiable.

### US1 — auth: the fab scoping the reason must not widen

```gherkin
Scenario: a richer reason does not reach an operator who does not hold the fab
  Given a dead letter recorded for fab "hamburg" with a detailed rejection reason
  When an operator holding only fab "munich" calls GET /dead-letters with scope sse.events.read
  Then the response does not include that row
```

`ListDeadLettersQueryHandler.cs:27-31` filters on `fabs.Contains(deadLetter.Fab)` and a null fab matches nobody. The reason text is now richer — it embeds a device's `occurredAt` — so the existing scoping is now carrying slightly more sensitive payload and must be re-asserted, not assumed.

### US2 — the exhausted row names its last failure

```gherkin
Scenario: an exhausted delivery records what it kept failing on
  Given every write for a delivery fails with "no partition covers fab hamburg"
  When the retry window elapses
  Then the dead letter's error contains "not storable after"
  And it also names the last failure the delivery met
```

---

## Independent end-to-end test procedure

Runnable by a person with no knowledge of this spec, against the real Aspire stack. It exercises `:194`, the site the reported failure travels.

1. Boot the stack: `dotnet run --project src/AppHost` (see CLAUDE.md memory — anonymous dashboard flag; one Aspire stack per machine).
2. Mint a token for `scenario-simulator` **from Aspire's proxied Keycloak endpoint**, not the container's mapped port.
3. Publish an MQTT message to `fab/munich/plc/clock-drift-1` whose `occurredAt` is `DateTimeOffset.UtcNow + 1 hour` and whose `kind` is a fresh `Guid.CreateVersion7()`-derived token, so the row is findable.
4. Wait for the dead letter: `GET /dead-letters?limit=50` with `sse.events.read`, filtering client-side on that kind appearing in `rawPayload`.
5. **Observe `error`.**
   - **Before the change:** `not storable after 00:05:00 of retrying`.
   - **After the change:** contains `EVENT_OCCURRED_AT_TOO_FAR_IN_FUTURE` and `more than 5 minutes in the future`, and does **not** contain `of retrying`.
6. Confirm the delivery was acknowledged, not carried: publish once, wait 30 s, re-read `GET /dead-letters` and confirm **exactly one** row for that kind. A carried delivery would produce a second.
7. Counterfactual for the untouched site, in the same run: drop `events_hamburg`, publish one event to `fab/hamburg/plc/dev-1`, and confirm its row still reads `not storable after …`. **Both rows present and different in the same listing** is the observation — a change that made every row say the same new thing would pass step 5 alone.

---

## Locked technology choices

Nothing new. Everything below already exists in this repo and is used as-is:

| Concern | Choice | Where it already is |
|---|---|---|
| Error carrier | `Result<T, IngestEventError>` (ADR-0047) | `IngestEventCommandHandler.cs:23` |
| Error shape | `ApiError(Code, Message, HttpStatusCode)` (ADR-0089) | `IngestEventErrors.cs:6` |
| Reason value object | `RejectionReason` (ADR-0038/0046) | `Domain/DeadLetter/RejectionReason.cs` |
| Absence | `Option<T>` (ADR-0141) | already used in this very file, `:179`, `:322` |
| Guards | `Ensure.That(...)` (ADR-0105) | `IngestEventBatchCommandHandler.cs:49` |
| Logging | `[LoggerMessage]` source-gen (ADR-0050) | `Infrastructure/Log.cs:115` |
| Tests | xUnit + Shouldly, sentence-style names, hand-written fakes (ADR-0052/0053/0054) | `PersistenceLoopHostedServiceTests.cs` |
| Integration | Aspire fixture, no Testcontainers (ADR-0103) | `PoisonDeliveryEscapeIntegrationTests.cs` |

**No migration.** The `error` column is `varchar(512)` and stays `varchar(512)`. No `Shared.Contracts` message changes. No frontend work — `apps/` is untouched.

---

## Out of scope

- **Changing `DeadLetterDto` or the `GET /dead-letters` contract.** The row's *content* improves; its shape does not. A structured `{code, message}` pair on the DTO would be a contract change, would need the management-web consumer, and is not required to fix the reported failure.
- **A dead-letter replay/redrive endpoint.** Adjacent and frequently wanted; not this issue.
- **The `MqttSubscriberHostedService` capture path.** Already carries a real reason (`:145`).
- **Making `IngestEventError` variants richer.** The two existing variants already carry enough; adding more is a separate decision with its own trigger.

---

## Assumptions, marked

1. **`$"{Code}: {Message}"` is the reason format.** Both halves earn their place: `Code` is the stable token already logged at `:360` and `IngestEventBatchCommandHandler.cs:153`, so a row and a log line can be correlated; `Message` is the sentence that tells the operator what to do. No consumer parses the field (verified — the only reader is the DTO projection), so the format is free to be prose. **If a reviewer prefers `Message` alone, that is a one-line change and does not disturb the design.**
2. **512 characters is enforced by truncation, not by refusal.** The alternative — let `RejectionReason.From` throw — strands the delivery permanently (see the bad-request scenario). Truncating is the only option that keeps the record-then-release contract of spec 018 FR-008 intact.
3. **US2's "last failure" is the exception's `Message`, not its full `ToString()`.** A stack trace in a 512-char column would crowd out the sentence. Recorded as a guess because nobody has asked for either.
