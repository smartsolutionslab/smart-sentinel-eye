# Spec 173 — A delivery whose payload is absent

**Issue**: #2203 · **Branch**: `fix/2203-a-delivery-that-cannot-be-read` · **Phase**: 1 (Specify)

**ADRs**: ADR-0037 (phased workflow), ADR-0144 (autonomous lane; phase-4a colour),
ADR-0139 (new behaviour starts red), ADR-0036 (smallest possible change; validate
at trust boundaries only), ADR-0038/0046/0066 (hand-written value objects own
their validation), ADR-0105 (`Ensure.That` guards, and the `ArgumentException`
family they throw), ADR-0047/0089 (`Result` / `ApiError` — the 400 shape the HTTP
sites already use), ADR-0052/0053/0054 (xUnit + Shouldly, sentence-style names,
hand-written builders), ADR-0103 (integration tests against the Aspire fixture),
ADR-0095/0096/0100 (Mosquitto, MQTTnet, the QoS-1 persistent session this defect
loops against).

**No new ADR is needed.** The dead-letter behaviour itself is not changed — it is
*reached*. Nothing is decided here that ADR-0036 and the value-object ADRs do not
already settle. **If phase 4 finds it cannot fix the sites without changing what
dead-lettering means, or without rejecting a payload shape accepted today, it
must stop and say so rather than decide.**

---

## Why

`MqttSubscriberHostedService` guards its parse in three blocks. Two of them catch
the exceptions their contents can throw. The middle one does not:

```
:220  catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
:228      payloadVo = Payload.From(payload.Payload.GetRawText());
:230  catch (ArgumentException ex)
```

An MQTT message that omits the `payload` property leaves that `JsonElement` at
`JsonValueKind.Undefined`, and `GetRawText()` on `Undefined` throws
`InvalidOperationException` — which `:230` does not catch.

The escape happens *before* the delivery is acknowledged, so the message is never
ACKed, never dead-lettered, and QoS 1 redelivers it forever. The poison-message
escape hatch is bypassed entirely, because dead-lettering lives on the path the
exception jumped over.

---

## Premise check against `develop` (b33e9c93)

The issue cites `src/EventIngestion/Infrastructure/Messaging/…:226-233`. **The
directory has been renamed** — it is `Infrastructure/Ingress/` now. Everything
else holds, by content:

| Claim | State | Evidence |
|---|---|---|
| The narrow catch exists | **Holds** | `MqttSubscriberHostedService.cs:230` — `catch (ArgumentException ex)` around `:228` |
| `:220` catches `InvalidOperationException` too | **Holds** | `:220` — `ArgumentException or JsonException or InvalidOperationException` |
| Missing `payload` ⇒ `Undefined` | **Holds — measured, not assumed** | see below |
| `GetRawText()` on `Undefined` throws `InvalidOperationException` | **Holds — measured** | see below |
| `PoisonDeliveryEscapeIntegrationTests` exists | **Holds** | `tests/Integration.Tests/EventIngestion/PoisonDeliveryEscapeIntegrationTests.cs` |
| …and does not cover this branch | **Holds, and more strongly than #2203 says** | It does not exercise the *parse* path at all. Its poison is a **dropped partition** (`:48`), so the delivery parses fine and is dead-lettered by the *persistence* path (`error.ShouldContain("not storable")`, `:73`). Every one of its payloads carries `payload` (`:210`) |
| No test at any level covers the uncaught branch | **Holds** | There is no test of `OnMessageReceived` or `TryParseEnvelope` at any level. `EventIngestion.Infrastructure.Tests` covers the *connection* loop only |

### The two measured facts

Run in a throwaway project against the exact `MqttIngressPayload` record shape
(`net10.0`, same `System.Text.Json`):

```
[payload absent] deserialized. ValueKind=Undefined …
[payload absent]   GetRawText() THREW System.InvalidOperationException: Operation is not valid due to the current state of the object.
[payload null]     ValueKind=Null   → GetRawText() => null
[payload string]   ValueKind=String → GetRawText() => "oops"
```

Two consequences the issue does not contain, and both change the scope:

**A. Only *absence* throws. Wrong *kind* does not.** `payload: null` and
`payload: "oops"` both return raw text today and are **accepted** — `Payload.From`
parses `null` and `"oops"` as valid JSON. #2203 asks whether "a missing or
wrong-kind property anywhere else" has the identical defect; measured, the answer
is that `GetRawText()` is safe on every `ValueKind` except `Undefined`. This
narrows the fix to one condition and removes a whole speculative branch.

**It also creates an obligation.** Those two shapes are accepted on `develop`
today. A fix that guards "the payload does not look right" instead of "the
payload is not there" would start rejecting live producer traffic. They are
pinned green, before and after — see SC-4.

**B. The sibling fields are already safe, and that is now checked rather than
assumed.** Every other field on this path was read and its throw type traced:

| Site | Call | Absent/wrong value | Throws | Caught? |
|---|---|---|---|---|
| `:217` | `JsonSerializer.Deserialize` | wrong type on any typed field | `JsonException` | **yes** (`:220`) |
| `:228` | `payload.Payload.GetRawText()` | `payload` absent | **`InvalidOperationException`** | **NO** ← the defect |
| `:244` | `EventIdentifier.From(payload.EventId)` | `eventId` absent ⇒ `Guid.Empty` | `ArgumentException` (`Ensure…IsNotEmpty`) | **yes** (`:252`) |
| `:248` | `Kind.From(payload.Kind)` | `kind` absent ⇒ `null` | `ArgumentException` (`Ensure…IsNotNullOrWhiteSpace`) | **yes** |
| `:249` | `OccurredAt.From(payload.OccurredAt)` | `occurredAt` absent ⇒ `default` | *nothing* — accepted | n/a |
| `:213-215` | `FabIdentifier`/`Source`/`DeviceIdentifier.From` | bad topic segment | `ArgumentException` | **yes** (`:220`) |

`Ensure` throws only `ArgumentNullException` and `ArgumentException`
(`src/Shared.Kernel/Ensure.cs` — every throw site), both of which `:230`/`:252`
catch. So the value objects are not a second hole.

### The category search found two more sites of the same shape

`grep` for `GetRawText` / `JsonElement` across `src/` — not just this file:

| Site | Consequence today |
|---|---|
| `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs:228` | Un-ACKed, never dead-lettered, redelivered forever |
| `src/EventIngestion/Api/EventsEndpoints.Writes.cs:85` (`POST /events/manual`) | Unhandled → **500** where the same block answers **400** for every other bad field |
| `src/EventIngestion/Api/EventsEndpoints.Writes.cs:162` (`POST /events/webhook/{name}`) | Same |

All three are `JsonElement Payload` on a request record, read with `GetRawText()`
inside a `catch (ArgumentException)`. **One shape, three call sites** — #2203's
own framing ("the same shape may repeat") anticipates exactly this, so they are
fixed together.

Checked and **clean**, recorded so the next reader does not re-walk them:
`AelInterpreter` (guards `ValueKind != Object`, `TryGetProperty`, and switches on
`ValueKind` with a `_` arm). Adjacent but on a different path and **out of
scope**: `CameraCatalogFabGuard.cs:68`, `CameraCatalogFabLookup.cs:71`
(`page.GetProperty("items")` on an HTTP response we control), `MediaMtxRtspGateway`
and `ReverseIndexSeederHostedService` (both already use `TryGetProperty`).

---

## Latency-budget impact (constitution §IV)

**Leg 4, *event → overlay state* (≤ 200 ms), is the leg this path feeds.**

No leg's figure moves. The change replaces one `GetRawText()` call with a guarded
equivalent on the parse path — a `ValueKind` comparison, on a path that runs once
per delivery and is already doing JSON deserialization.

Stated the other way round, which is the part that matters: **today's defect is
capable of starving leg 4's input entirely.** An un-ACKed QoS-1 delivery is
redelivered against a persistent session forever; the handler throws on every
redelivery. Removing it protects the leg rather than costing it.

**§IV's per-leg table is not amended, and phase 4 must not edit
`.specify/memory/constitution.md`.**

---

## User stories

### US-1 (P1) — A delivery with no payload is dead-lettered once and released

**As** an operator of a 24/7 ingestion path, **I want** an MQTT message that omits
its `payload` property to be recorded in `dead_letters` and acknowledged, **so
that** one malformed publish cannot redeliver forever behind every healthy event.

Today the handler throws, the delivery is never ACKed, and the broker brings it
back for good.

### US-2 (P2) — A manual ingest with no payload is refused, not a server fault

**As** an operator posting an event by hand, **I want** a body missing `payload`
to be answered `400 EVENT_INVALID_INPUT`, **so that** I learn what I got wrong
instead of reading a 500.

Today the same request that answers 400 for a bad `kind` answers 500 for an
absent `payload`.

### US-3 (P2) — A webhook delivery with no payload is refused the same way

**As** the owner of a webhook integration, **I want** the machine path to give the
same refusal as the human one, **so that** a producer's missing field is a
client error it can act on.

---

## Acceptance scenarios (Gherkin)

### US-1 — MQTT

```gherkin
Scenario: happy — a well-formed delivery is unaffected
  Given an MQTT message on fab/{fab}/plc/{device} carrying eventId, kind, occurredAt and payload
  When the subscriber receives it
  Then an event row is stored
  And no dead letter is written
  # Characterisation: must pass unchanged before and after.

Scenario: conflict — the payload property is absent
  Given an MQTT message on a well-formed topic whose JSON omits "payload"
  When the subscriber receives it
  Then exactly one dead_letters row is written for that delivery
  And its error names the missing payload in words an operator can read
  And the delivery is acknowledged
  And no second dead_letters row appears for it after a settle
  And events published after it are stored

Scenario: bad-request — the payload is present but not valid JSON text
  Given a delivery whose payload exceeds 64 KB
  When the subscriber receives it
  Then one dead_letters row is written and the delivery is acknowledged
  # Already true via ArgumentException; pinned so the fix does not move it.

Scenario: auth — the publisher must be authorised by the broker
  Given a client without a Keycloak-minted JWT
  When it attempts to publish to fab/#
  Then the broker refuses it and nothing reaches the subscriber
  # Existing behaviour (ADR-0100); no new assertion.
```

### US-2 / US-3 — HTTP

```gherkin
Scenario: happy — a body with a payload is accepted
  Given an authenticated operator on a provisioned fab
  When POST /events/manual carries deviceId, kind, occurredAt and payload
  Then the response is 201 Created
  # Characterisation.

Scenario: conflict — the payload property is absent
  Given an authenticated operator on a provisioned fab
  When POST /events/manual carries a body with no "payload" property
  Then the response is 400
  And the problem title is EVENT_INVALID_INPUT
  And the detail names the missing payload
  # Today: 500.

Scenario: bad-request — payload present, another field invalid
  When POST /events/manual carries a lowercase "kind"
  Then the response is 400 EVENT_INVALID_INPUT
  # Already true; pinned so the fix does not move it.

Scenario: auth — an anonymous caller is refused before the body is read
  Given no bearer token
  When POST /events/manual is sent with a body missing "payload"
  Then the response is 401
  And no dead letter and no event row is written
  # The refusal must not become a 400 or a 500 that leaks how far the body got.
```

---

## Independent end-to-end test procedure

Run against a booted Aspire stack (one stack per machine).

1. **Observe the defect.** With `develop`'s code, publish one MQTT message to
   `fab/hamburg/plc/dev-1` whose JSON omits `payload`, using a run-unique `kind`
   inside no payload at all — identify it by topic and time instead. Query
   `dead_letters`: **no row appears**, at any deadline. Read the
   `event-ingestion` logs: the `InvalidOperationException` is visible and no
   `RejectingMqttDelivery` line accompanies it.
2. **Observe the loop.** Restart the `event-ingestion` resource. The broker
   redelivers the same message against its persistent session
   (`cleanSession=false`, MQTT 3.1.1 — `MosquittoConnectionFactory.cs:56-75`) and
   the same exception is logged again. That is the "forever" made visible without
   waiting for forever.
3. **Apply the fix, restart.** The pending redelivery is now dead-lettered and
   ACKed: one `dead_letters` row naming the missing payload, and the broker stops
   bringing it back. **The fix drains what step 1 created** — no manual cleanup of
   the broker session is required.
4. **HTTP.** `POST /events/manual` with a body omitting `payload` answers **400
   EVENT_INVALID_INPUT** where it previously answered 500; repeat for
   `POST /events/webhook/{name}`.
5. **Regression.** Publish 20 well-formed events and confirm all 20 are stored
   and no dead letter is written.

---

## Locked tech choices

- xUnit + Shouldly, hand-written fakes, no AutoFixture (ADR-0052, ADR-0054).
- Integration coverage runs against the `AspireFixture`; no Testcontainers
  (ADR-0103).
- Sentence-style test names with underscores (ADR-0053).
- The guard is expressed with `Ensure.That(...)` or an equivalent that throws from
  the `ArgumentException` family, so the existing catches remain correct
  (ADR-0105).
- No new package, no new abstraction, no configuration knob (ADR-0036).

## Success criteria

- **SC-1** A test observes an MQTT delivery with no `payload` producing **exactly
  one** `dead_letters` row and an acknowledgement — and that test is observed
  **red** against `develop` first, with the verbatim failure quoted in the PR.
- **SC-2** A test observes `POST /events/manual` and `POST /events/webhook/{name}`
  answering **400 `EVENT_INVALID_INPUT`** for a body with no `payload`, observed
  **red** first (they answer 500 today).
- **SC-3** The dead-letter `error` and the HTTP `detail` name the *missing
  payload* specifically. A message reading "Operation is not valid due to the
  current state of the object" does not satisfy this — that string is what
  widening the catch alone would put in front of an operator.
- **SC-4** `payload: null` and `payload: "oops"` remain **accepted**, pinned by a
  test that passes unmodified before and after the change. A fix that rejects
  either is over-reach and must be reverted.
- **SC-5** All three call sites are fixed in one change. A grep for
  `.GetRawText()` in `src/EventIngestion/` returns no unguarded site.
- **SC-6** `PoisonDeliveryEscapeIntegrationTests` is byte-identical to `develop`.
- **SC-7** No production behaviour changes for any delivery that parses today —
  the existing EventIngestion suites pass unmodified.

## Assumptions, marked

- **A1** The HTTP sites are assumed to answer **500** today, from reading the code
  (an uncaught `InvalidOperationException` escaping a minimal-API handler). The
  exact status and body are **observed in phase 4a**, not assumed; if the pipeline
  already converts it to something else, phase 4a reports the real figure and the
  spec's claim is corrected rather than the test bent to fit.
- **A2** Phase 4a's MQTT integration red leaves an un-ACKed delivery in the
  broker's persistent session. **Mitigation is the fix itself** (step 3 above).
  Stated as a known, bounded residue rather than discovered later.
- **A3** MQTTnet continues dispatching subsequent deliveries after a handler
  throws, so the red is not expected to wedge the rest of the Aspire suite. If
  phase 4a observes otherwise, it reports that — it is a fact about the defect's
  blast radius worth having.

## Out of scope

- Changing what dead-lettering *means* — what is captured, who can read it, how it
  is released. Only reaching it.
- `payload: null` and non-object payloads, which are accepted today and stay
  accepted (SC-4).
- `OccurredAt.From` accepting `DateTimeOffset.MinValue` for an absent
  `occurredAt` — a real silent-acceptance question, deliberately not decided here.
  **File it separately if it deserves an issue; do not fix it in this branch.**
- `CameraCatalogFabGuard`, `CameraCatalogFabLookup`, `MediaMtxRtspGateway`,
  `ReverseIndexSeederHostedService` — other contexts, different paths.
- Any edit to `.specify/memory/constitution.md` or `docs/adr/`.
