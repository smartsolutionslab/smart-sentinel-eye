# ADR-0175: The MQTT ingest channel blocks rather than sheds

**Status:** Accepted
**Date:** 2026-10-11
**Supersedes:** —
**Superseded by:** —

## Context

`BoundedIngestChannel` (`src/EventIngestion/Application/Ingress/BoundedIngestChannel.cs:32`)
is configured `FullMode = BoundedChannelFullMode.Wait` at capacity
5,000: when full, it blocks the MQTT subscriber rather than rejecting,
so this path structurally cannot produce a 429. This was a deliberate
choice (spec 006 FR-021/FR-022) recorded only in a class comment and a
test name (`BoundedIngestChannelTests.cs:101`,
`WriteAsync_blocks_when_full_until_a_slot_frees`) — nowhere a future
reader would look before assuming otherwise. #625 assumed this path
would return "429 then recovers" under burst load and was wrong;
nothing distinguished that assumption from a correct read of
documented intent (#2211).

The case for keeping `Wait`:
- MQTT already has durable backpressure: an unacked delivery is
  retained and redelivered by the broker; a dropped one is lost.
  Blocking converts overload into observable broker queue depth
  instead of silent application-level loss.
- The single-reader FIFO drain that preserves per-source order depends
  on the writer never skipping — `DropOldest`/`DropWrite` would
  silently break the ordering guarantee `IngestThroughputMeasurementTests`
  already asserts (`total.ShouldBe(0, "per-source order was lost under load")`).
- There is no HTTP caller to hand a 429 to — this is our own subscriber
  loop, not a client request.

The case against: blocking delays acks, which under sustained overload
pushes the problem further away (broker-side retention sheds instead,
with less telemetry); a blocked subscriber risks tripping MQTT
keepalive and forcing a reconnect, which is not free
(`MqttResubscribeAfterBrokerOutageIntegrationTests` exists because of
this); and the wait itself is currently invisible — `CurrentDepth` is
exposed as a gauge, blocking duration is not.

## Decision

**Keep `FullMode = BoundedChannelFullMode.Wait`.** The ordering
guarantee and the existing durable-redelivery property of MQTT itself
both depend on not shedding, and there is no caller-facing status code
this path could meaningfully return. This formalizes spec 006's
original choice as a recorded decision rather than leaving it
inferable only from a class comment and a test name.

**Add observability for the blocking itself**, as the accompaniment
this decision needs to be operable: a blocked-duration or saturation
signal alongside the existing `CurrentDepth` gauge, using the
`sse.ingest.events` instrumentation pattern #622 already established in
this context. Without this, an operator watching only `CurrentDepth`
has no way to see that the subscriber is currently stalled versus
merely busy.

## Consequences

**Positive:**
- The next reader (the next #625-shaped misunderstanding) has an ADR
  to find instead of re-deriving or guessing the reason from a comment.
- No behaviour change — this records existing, tested behaviour.
- The ordering guarantee and MQTT's own backpressure property are
  preserved exactly as today.

**Negative:**
- Sustained overload still eventually sheds, just at the broker rather
  than here, with less direct telemetry at the point of loss than an
  application-level shed would give — accepted as the cost of
  preserving ordering.
- The keepalive/reconnect risk under sustained blocking remains
  unaddressed by this ADR; it is a known cost of the chosen approach,
  not something the observability addition fixes.

## Scope, explicitly

This ADR does not change `IngestWriteLimiter` (the HTTP write path's
own, independent 429 mechanism, spec 104's test slice) and is not to be
folded into that work — they are different mechanisms for different
reasons, and spec 104 stands independently of how this decision goes.

## Follow-up

- Add the blocked-duration/saturation instrument beside `CurrentDepth`.
- Update `BoundedIngestChannel`'s class comment and
  `BoundedIngestChannelTests.cs` to cite this ADR rather than standing
  alone as the only record of the decision.
- Close #2211, citing this ADR.
