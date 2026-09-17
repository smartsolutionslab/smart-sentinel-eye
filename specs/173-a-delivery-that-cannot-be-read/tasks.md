# Spec 173 — Tasks

**Issue**: #2203 · **Branch**: `fix/2203-a-delivery-that-cannot-be-read` · **Phase**: 3 (Tasks)

**Spec**: `spec.md` · **Plan**: `plan.md`

---

## Declarations (ADR-0144)

| Declaration | Value |
|---|---|
| **Engineer (phase 4b)** | `backend-engineer` |
| **Phase 4a colour** | **RED — explicit, for all three sites.** Plus three named characterisation tests, green, listed separately below so the two colours are never confused |
| **New ADR** | **None expected.** Dead-letter behaviour is reached, not changed. If phase 4 finds it must change what dead-lettering means, or must reject a payload shape accepted today, it **stops and reports** rather than deciding |
| **Board** | #2203 is already on Project #13, status **Todo** — verified with `--limit 2000`. No `item-add` needed |
| **Skipped phases** | None |

### Files phase 4 may touch — exhaustive

**Production (phase 4b only, except the seam in T001):**

- `src/EventIngestion/Domain/Event/Payload.cs`
- `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs`
- `src/EventIngestion/Api/EventsEndpoints.Writes.cs`

**Tests:**

- `tests/EventIngestion.Infrastructure.Tests/MqttEnvelopeParsingTests.cs` *(new)*
- `tests/EventIngestion.Domain.Tests/Event/PayloadTests.cs` *(existing, extended)*
- `tests/Integration.Tests/EventIngestion/MissingPayloadIsDeadLetteredIntegrationTests.cs` *(new)*
- `tests/Integration.Tests/EventIngestion/MissingPayloadIsRefusedIntegrationTests.cs` *(new)*

**Anything else is out of scope**, in particular:
`tests/Integration.Tests/EventIngestion/PoisonDeliveryEscapeIntegrationTests.cs`
(must stay byte-identical — SC-6), `.specify/memory/constitution.md`, `docs/adr/`,
and the catch clauses at `MqttSubscriberHostedService.cs:220`/`:230`/`:252` and
`EventsEndpoints.Writes.cs:87`/`:164`.

---

## Phase 4a — tests first (`test-writer`)

The verbatim output of every run below goes to the engineer as its brief and into
the PR body. **The engineer may not edit these tests to make them pass.**

### Foundational — blocks T002 only

- **[T001] [US-1]** Widen `TryParseEnvelope` and the nested `ParseResult` record
  from `private static` / `private sealed record` to `internal` in
  `src/EventIngestion/Infrastructure/Ingress/MqttSubscriberHostedService.cs`.
  **Visibility only — no other edit to this file in the 4a commit.**
  `InternalsVisibleTo` already exists
  (`SmartSentinelEye.EventIngestion.Infrastructure.csproj:8`).
  Verify: solution builds, existing tests still pass.

### RED — the three sites

- **[T002] [US-1]** *(after T001)* New
  `tests/EventIngestion.Infrastructure.Tests/MqttEnvelopeParsingTests.cs`:
  `A_delivery_whose_payload_property_is_absent_is_rejected_rather_than_thrown`.
  Calls `MqttSubscriberHostedService.TryParseEnvelope("fab/hamburg/plc/dev-1",
  <UTF-8 bytes of JSON with eventId, kind, occurredAt and **no** payload>)` and
  asserts a `ParseResult` with a null envelope and an error naming the missing
  payload.
  **Expected red:** throws `System.InvalidOperationException: Operation is not
  valid due to the current state of the object.` Quote it verbatim.

- **[T003] [P] [US-1]** New
  `tests/Integration.Tests/EventIngestion/MissingPayloadIsDeadLetteredIntegrationTests.cs`
  (`AspireCollection`, ADR-0103): publish **one** QoS-1 MQTT message on a
  run-unique topic under a provisioned fab, JSON omitting `payload`; then publish
  N well-formed events.
  Asserts: exactly **one** `dead_letters` row for that topic; its `error` names
  the missing payload (SC-3); **no second row** after a settle; all N healthy
  events stored.
  Model the publish/poll helpers on `PoisonDeliveryEscapeIntegrationTests`
  (`:161-200`) — **copy the shape, do not edit that file**.
  **Expected red:** no `dead_letters` row ever appears; the wait times out.
  Record whether the rest of the collection still passes (spec A3).
  *Leaves a known residue in the broker session — the fix drains it (spec A2).*

- **[T004] [P] [US-2, US-3]** New
  `tests/Integration.Tests/EventIngestion/MissingPayloadIsRefusedIntegrationTests.cs`:
  two facts — `POST /events/manual` and `POST /events/webhook/{name}`, each with a
  body omitting `payload`, on a provisioned fab with a valid credential. Asserts
  **400** with problem title `EVENT_INVALID_INPUT` and a detail naming the missing
  payload. Reuse the auth/fab setup in `ManualIngestFabScopingIntegrationTests`
  and `WebhookBearerValidationIntegrationTests`.
  **Expected red:** 500 (spec A1 — **report the status actually observed**).

- **[T005] [P] [US-2, US-3]** In the same file as T004:
  `An_anonymous_caller_is_refused_before_the_body_is_read` — no bearer token, body
  omitting `payload`, expect **401**, and no `dead_letters` row and no event row.
  **Expected green today**; it pins that the fix does not turn an auth refusal
  into a 400 or a 500.

### CHARACTERISATION — green before and after, unmodified

Kept separate from the red so the two obligations are never conflated
(constitution §Testing). Each is observed **passing** against `develop` before any
production edit, and must pass **unmodified** afterwards. An assertion that has to
be edited is evidence the behaviour moved — **block, do not adjust**.

- **[T006] [US-1]** *(after T001, same file as T002)*
  `A_delivery_whose_payload_is_json_null_is_accepted` and
  `A_delivery_whose_payload_is_a_string_is_accepted` — both parse to an envelope
  today (measured: `GetRawText()` returns `null` and `"oops"` respectively). SC-4.
- **[T007] [US-1]** *(same file)* `A_well_formed_delivery_parses`, and
  `A_payload_over_64_KB_is_rejected_with_a_readable_reason` — the
  `ArgumentException` path that already reaches the dead letter.
- **[T008] [P] [US-2]** Extend
  `tests/EventIngestion.Domain.Tests/Event/PayloadTests.cs` only with cases that
  pass today (valid JSON, > 64 KB rejection). The `From(JsonElement)` cases are
  written in phase 4b alongside the factory — a test for an API that does not
  compile cannot be observed red, and this spec's red lives on the three existing
  surfaces above.

**Gate for 4a:** T002, T003, T004 observed red with output captured verbatim;
T005–T008 observed green.

---

## Phase 4b — implementation (`backend-engineer`)

Brief = the verbatim 4a output. Do not edit any test from 4a.

- **[T009] [US-1, US-2, US-3]** Add `Payload.From(JsonElement)` to
  `src/EventIngestion/Domain/Event/Payload.cs`: reject
  `ValueKind == JsonValueKind.Undefined` with an **`ArgumentException`-family**
  throw whose message names the missing payload in operator-readable words; then
  delegate to `From(element.GetRawText())`.
  **The condition is `Undefined` and nothing broader** — `Null` and scalar kinds
  stay accepted (SC-4). Add its own unit tests to `PayloadTests.cs` (both colours
  of the new factory).
  *Blocks T010 and T011.*

- **[T010] [P] [US-1]** *(after T009)*
  `MqttSubscriberHostedService.cs:228` → `Payload.From(payload.Payload)`.
  **No catch clause changes.**

- **[T011] [P] [US-2, US-3]** *(after T009)*
  `EventsEndpoints.Writes.cs:85` and `:162` → `Payload.From(body.Payload)`.
  **No catch clause changes.**

- **[T012] [US-1, US-2, US-3]** *(after T010, T011)* Run the full 4a set plus the
  existing EventIngestion suites (`EventIngestion.Domain.Tests`,
  `EventIngestion.Application.Tests`, `EventIngestion.Infrastructure.Tests`,
  `Architecture.Tests`) — all green, no 4a test modified (SC-7).
  Confirm `git diff develop -- tests/Integration.Tests/EventIngestion/PoisonDeliveryEscapeIntegrationTests.cs`
  is empty (SC-6), and that
  `grep -rn "GetRawText" src/EventIngestion/` shows no unguarded site (SC-5).
  Format + analyzers clean in Release.

---

## Phase 5 — verify

- **[T013]** Run the spec's independent end-to-end procedure against the booted
  Aspire stack: observe the dead letter appear for a payload-less publish, the
  acknowledgement, the absence of a second row, and the 400s on both HTTP routes.
  Confirm the broker no longer redelivers the residue from T003. Latency: cite
  leg 4 as **unchanged, no figure moves** (spec, §IV section) — no measurement
  owed.

## Phase 6 — QA

- **[T014]** `/code-review`. Security-sensitive? The change tightens validation at
  two authenticated HTTP trust boundaries and one broker boundary — run
  `/security-review` as well, scoped to whether the new refusal can leak payload
  content into a response body or a log line.

## Phase 7 — PR

- **[T015]** PR to `develop` (`--base develop`), body quoting the verbatim 4a red
  output for T002/T003/T004 (ADR-0139), referencing #2203 with a closing keyword,
  Conventional Commits, **no `Co-Authored-By`** (ADR-0086).

---

## Dependency graph

```
T001 ──► T002 ──┐
                ├──► [4a gate] ──► T009 ──┬──► T010 ──┐
T003 [P] ───────┤                         └──► T011 ──┴──► T012 ──► T013 ──► T014 ──► T015
T004 [P] ───────┤
T005 [P] ───────┤
T006 ───────────┤   (T006/T007 share T002's file — sequential with it)
T007 ───────────┤
T008 [P] ───────┘
```

**Parallel sets (ADR-0109, disjoint files):**

- 4a: `{T003}`, `{T004, T005}` (one file, one author — sequential within),
  `{T008}`, and `{T002, T006, T007}` (one file) may be written concurrently.
- 4b: `{T010}` and `{T011}` are disjoint files, both gated on T009.

**Nothing foundational blocks the fan-out** beyond T001 (a one-keyword visibility
seam) and T009 (the Domain factory). There is no Shared.Kernel, Shared.Contracts,
AppHost or Aspire-resource work in this spec.
