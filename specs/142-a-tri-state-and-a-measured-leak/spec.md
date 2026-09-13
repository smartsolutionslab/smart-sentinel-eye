# Spec 142 — A tri-state, and a release that says it failed

**Issue:** #2198 (part 3 of 3 from #2109) · **Branch:** `fix/2198-a-tri-state-and-a-measured-leak`
**Phase:** 1 (Specify) · **Date:** 2026-09-13
**ADRs:** ADR-0037 (the phased workflow), ADR-0074 (the two React apps and the
shared package), ADR-0076 (the resilience channel's transport is replaceable —
this adds no coupling to it), ADR-0109 (disjoint files and `[P]`), ADR-0117 (an
implemented leg is subject to §VII; a discharge has to be earned), ADR-0128
(receiver playout alignment against the SFU's RTCP clock — the leg
`setPlayoutTarget` actuates), ADR-0129 (the label is aged, and fails open),
ADR-0139 + constitution §Testing (new behaviour starts red), ADR-0143 (retrying
is the thing that needs justifying), ADR-0144 (the lane may not write an ADR and
may not weaken a gate).
**Constitution:** §IV (the latency budget and its four-state leg table), §VII,
§Testing.

---

## The measurement the issue demanded, taken before the severity was ranked

#2198 says, in as many words: _"Read the configured timeout before deciding the
severity — do not let this issue's ranking stand in for that."_ This section is
that reading, and it changes the answer.

### What was read

- `src/AppHost/AppHost.cs:159` pins **`bluenviron/mediamtx:1.21.0-ffmpeg`**
  (`:202` and `:647` pin the same tag for `fixture-video` and `camera-sim`;
  `:195` is a comment naming it, which is the line #2198's brief cited).
- `src/AppHost/Resources/mediamtx.yml` is **18 effective lines and sets no
  timeout of any kind** — `grep -niE 'timeout|session'` over it returns nothing.
  Every MediaMTX default therefore applies unmodified.
- `mediamtx.yml` at tag `v1.21.0`
  (`https://raw.githubusercontent.com/bluenviron/mediamtx/v1.21.0/mediamtx.yml`)
  declares, for WebRTC and WHEP: `readTimeout: 10s`, `writeTimeout: 10s`,
  `webrtcHandshakeTimeout: 10s`, `webrtcSTUNGatherTimeout: 5s`,
  `webrtcTrackGatherTimeout: 2s`, `whepHandshakeTimeout: 10s`,
  `whepSTUNGatherTimeout: 5s`, `whepTrackGatherTimeout: 2s`.

### Finding 1 — there is no such setting

**MediaMTX 1.21.0 has no idle-session or orphaned-session reclaim timeout for
WebRTC/WHEP.** Every timeout above governs _establishment_ (handshake, candidate
gathering, track gathering) or a single read/write, not the lifetime of an
established session whose client has vanished. The premise behind the phrase
"MediaMTX's own timeout reclaims it" does not exist as a configurable value.

### Finding 2 — reclaim is driven by ICE failure, and it is bounded

`internal/servers/webrtc/session.go` at `v1.21.0` terminates a read session on
exactly three things, quoted from the source:

```go
select {
case <-pc.Failed():
    return 0, fmt.Errorf("peer connection closed")
case err = <-r.Error():
    return 0, err
case <-s.ctx.Done():
    return 0, fmt.Errorf("terminated")
}
```

`internal/protocols/webrtc/peer_connection.go` fires `Failed()` on
`webrtc.PeerConnectionStateFailed || webrtc.PeerConnectionStateClosed`, and sets
**no** ICE agent timeouts on its `SettingEngine` — the only thing it configures
is `SetSTUNGatherTimeout`. So pion/ice's defaults stand
(`pion/ice/agent_config.go`):

```go
defaultKeepaliveInterval   = 2 * time.Second
defaultDisconnectedTimeout = 5 * time.Second   // time till an Agent transitions disconnected
defaultFailedTimeout       = 25 * time.Second  // time till an Agent transitions to failed after disconnected
```

**Ceiling: about 30 s** from the moment the browser stops sending, to
`PeerConnectionStateFailed`, to session termination. `WhepClient.close()`
(`:133-136`) calls `releaseSession()` and then **unconditionally**
`teardownLocally()` → `pc.close()`, so that clock starts on every teardown
whether or not the DELETE lands. A page that unloads kills the peer connection
the same way.

### Finding 3 — the severity this produces

**A logging gap with a bounded, correlated capacity spike — not an unbounded
resource leak.** Both halves matter:

- _Bounded._ A failed DELETE costs at most about 30 s of orphaned session, never
  forever. The word **"leak"** in this issue's title and branch name is
  therefore too strong, and the spec says so rather than inheriting it.
- _Correlated, and that is the part that survives._ The issue's own scaling
  argument holds inside the 30 s. A 250-camera wall that re-lays-out releases and
  re-establishes every session at once, and the usual cause of a failed release
  (an expired token, a gateway blip) hits all of them together. So the media
  server carrying the whole wall sees up to **500 concurrent sessions where it
  expected 250**, for up to 30 s, with the old half consuming goroutines, UDP
  flows and ICE agents while the new half is establishing. That is a real
  transient, on the one process the entire wall depends on, and **nothing
  anywhere says it happened.**

**This is the reason the fix is a report and not a retry.** See FR-004.

### Confidence, stated honestly

This is a **code-path derivation at the pinned tag**, not an observation of a
running MediaMTX. No MediaMTX container is running on this machine (`docker ps
-a` matches nothing), and booting the Aspire stack to watch
`GET /v3/webrtcsessions/list` drain after a killed browser is phase-5 work, not
phase 1. The three quoted sources are at the pinned `v1.21.0` tag and at
`pion/ice` `master`; **pion's version is the one risk** — `go.mod` was not read,
so if MediaMTX 1.21.0 pins a pion/ice whose defaults differ, the 30 s moves.
It does not move the _shape_ of the finding: with no idle-session setting
anywhere, reclaim is ICE-driven either way, and ICE-driven means bounded.

**What would change the ranking.** An observed session that outlives a killed
browser by materially more than 30 s, or a MediaMTX configuration change that
introduces a session TTL, would make this a leak and would justify the retry
FR-004 declines. **#2103** (the floating MediaMTX tag) is the standing way that
can change under us.

---

## What survives contact with the repository

### Confirmed — the swallowed release

`apps/shared/src/streaming/WhepClient.ts:213-230`. `releaseSession()` resolves
`getToken()`, fires `fetch(sessionUrl, { method: 'DELETE', headers, keepalive:
true })`, and ends `.catch(() => undefined)`. A rejected fetch, a 401, a 404 and
a 500 are indistinguishable from success, to every caller and to every log.

### Confirmed — the one boolean over three causes

`apps/shared/src/streaming/WhepClient.ts:188-211`, exactly as the issue's table
says: `:190` returns `false` for no peer connection or no `getReceivers`; the
loop's `continue` at `:196` leaves `applied` false when no video receiver carries
`jitterBufferTarget`; the `catch` at `:204` leaves it false when the assignment
throws. `useWhepSession.ts:332` then adds a **fourth** producer of the same
`false` — `clientRef.current?.setPlayoutTarget(...) ?? false` — and
`CameraViewer.tsx:336` reports all four as `playout-target-unsupported`.

That fourth producer is verbatim spec 095's recorded residual: _"a one-commit
window can latch `playout-target-unsupported` on a healthy engine."_

### Confirmed — the passing test that asserts the silence

`WhepClient.test.ts:320-334`, _"close() without a captured session URL performs
local teardown only"_, asserts `expect(fetchMock).not.toHaveBeenCalled()`. It is
correct and **stays unmodified** (FR-009). Nothing in that file exercises a
release that fails, and nothing in that file exercises `setPlayoutTarget` at
all — `:411` mentions it only in a comment, and the harness's `receivers` are
typed `{ track: { stop: () => void } }[]`, so `receiver.track?.kind !== 'video'`
skips every one of them.

### Correction the spec carries — "the unload path" does not exist

The issue reasons: _"it is `keepalive: true` deliberately: the request must
survive page unload. So 'await it and report' is **not** available on the unload
path."_

**There is no unload call path in this repository.** `grep -rn
'pagehide\|beforeunload' apps/ e2e/` returns nothing, and `WhepClient.close()`
has exactly **one** production caller: the effect cleanup at
`useWhepSession.ts:299`. React effect cleanups do not run on page unload. So
every `releaseSession()` that happens today happens on a live page with a
console to log to.

`keepalive: true` is still right, and stays: it protects a release already **in
flight** when the page goes away — an SPA route change immediately followed by a
navigation, a kiosk browser being killed a beat after a re-layout. What it does
_not_ do is create a code path on which a continuation is unavailable by
construction.

**The consequence for the design is the opposite of the issue's premise:** a
`.then`/`.catch` that reports is available on **every** call path that exists,
and the unload case degrades to "the continuation never runs and nothing is
logged" — which is stated in FR-003 rather than left implicit, because it is a
genuine hole and the only one.

---

## User stories

### US1 (P1) — A failed session release is visible

**As** the engineer debugging why the fab's media server is carrying twice the
sessions it should for half a minute after every wall re-layout,
**I want** the kiosk to say that a session release failed and what the server
answered,
**so that** a correlated release failure is a line I can grep for instead of a
transient I have to catch live on `/v3/webrtcsessions/list`.

**Independent end-to-end test:** boot the Aspire stack, open a kiosk wall, then
break the release: stop the API gateway (or let the kiosk token expire) and
navigate away from the wall so the tiles unmount. The browser console shows one
`[resilience] { subsystem: 'stream', transition: 'session-release-failed', … }`
line per tile that had captured a session URL, carrying the HTTP status or the
rejection text. With the gateway healthy, navigating away produces **no** such
line and the DELETEs return 2xx.

### US2 (P1) — "Still connecting" stops being reported as "cannot align"

**As** the operator watching a wall come up,
**I want** a tile that has not attached video yet to say nothing about its
playout target,
**so that** the one line a kiosk emits about its engine means what it says —
this browser cannot hold a playout target — instead of being latched by the
first render of a perfectly healthy tile.

**Independent end-to-end test:** open a wall in Chromium ≥ 115 with the console
open. No `playout-target-unsupported` line appears at any point during startup or
across a reconnect. Open the same wall in Firefox: exactly one line per tile.
(Today, the first of those two is not reliably true.)

**US2 is the story #2157 is waiting on.** #2157 is `agent:blocked` behind #1714
and explicitly consumes the tri-state; this spec supplies it and stops at the
call site.

---

## Functional requirements

### Item 1 — the release

- **FR-001.** `releaseSession()` reports on the existing `[resilience]` channel
  when the DELETE does not succeed. Two causes, two payload shapes under one
  transition `session-release-failed`, subsystem `stream`:
  - the request resolved with a non-2xx → `{ status: <number> }`;
  - the request (or the `getToken()` that precedes it) rejected →
    `{ error: <string> }`.
    Each line carries exactly the one fact it has; neither pads the other with a
    null. A consumer tells them apart by which key is present.
- **FR-002.** A DELETE that succeeds reports **nothing**, and a `close()` with no
  captured session URL reports **nothing** and still issues no request.
- **FR-003.** **On page unload, nothing is logged, and that is accepted.** The
  continuation attached to a `keepalive` request does not run once the page is
  gone; there is no document to log to and no console to read it. This is stated
  as the cost of `keepalive`, not designed around. It is not a live hole today
  (see _§ Correction_: no unload call path exists), and it becomes one only if a
  `pagehide` handler is ever added — at which point this FR is the note that
  says the report will be silent there.
- **FR-004.** **No retry.** `releaseSession` fires the DELETE once, as today.
  Three reasons, in descending weight: the ~30 s ICE ceiling derived at the
  pinned tag (A1/A2) caps what a retry could recover; the dominant failure the issue names is a **401 from an
  expired token**, and `getToken()` is already re-resolved at release time
  (pinned by `WhepClient.test.ts:296`), so a retry re-presents the same dead
  credential; and ADR-0143's principle — _retrying is the thing that needs
  justifying_ — is not met by a request whose failure costs a bounded transient.
  Recorded as **considered and declined with the number that declines it**, so a
  later reader does not re-open it as an oversight.
- **FR-005.** Teardown is not delayed or made to depend on the release. The
  report is attached to the existing fire-and-forget chain; `teardownLocally()`
  still runs synchronously after `releaseSession()` returns.

### Item 2 — the tri-state

- **FR-006.** `WhepClient.setPlayoutTarget` returns
  `PlayoutTargetOutcome = 'applied' | 'not-connected' | 'unsupported' | 'refused'`
  — a string-literal union, matching `WhepErrorKind` (`WhepClient.ts:3`) and
  `ResilienceSubsystem` (`resilienceLog.ts:1`). Mapping:

  | Outcome           | Condition                                                              | Issue's row                         |
  | ----------------- | ---------------------------------------------------------------------- | ----------------------------------- |
  | `'applied'`       | at least one video receiver accepted the assignment                    | (success)                           |
  | `'not-connected'` | no peer connection, no `getReceivers`, **or no video receiver at all** | `:190` — transient                  |
  | `'unsupported'`   | video receivers exist, none carries `jitterBufferTarget`               | `:196` — permanent for this browser |
  | `'refused'`       | a video receiver carries it and every assignment threw                 | `:204` — possibly per-value         |

- **FR-007.** **`'not-connected'` absorbs "zero video receivers", and this is a
  deliberate refinement of the issue's table rather than a deviation from it.**
  The issue's row 2 reads _"no receiver **carries** `jitterBufferTarget`"_, which
  presupposes receivers to inspect. A peer connection that has not attached video
  yet is the same transient condition as no peer connection — it is `:190`'s
  meaning, reached one line later — and mapping it to `'unsupported'` would
  re-open exactly the latch US2 exists to close, one layer down.
- **FR-008.** **The `catch` at `:204` stays swallowed.** FR-013 of spec 045 is
  right and is not reopened: a tile that cannot be aligned carries on showing
  video. The throw becomes _distinguishable_ (`'refused'`) and is still not
  raised to any caller and still not surfaced to an operator on the tile.
- **FR-009.** **The actuation is byte-equivalent.** `outcome === 'applied'` holds
  exactly where today's `applied` boolean holds: same loop, same receivers, same
  `jitterBufferTarget = milliseconds`, same argument, assignment attempted on
  **every** qualifying video receiver rather than short-circuiting at the first
  success. Nothing about what reaches the receiver changes.
- **FR-010.** The call site stops reporting `'not-connected'`.
  `CameraViewer.tsx` emits `playout-target-unsupported` for `'unsupported'` and
  `'refused'` only. The latch, its scope (once per mounted tile per session, spec
  095 FR-003) and the emitted **payload** are unchanged — see FR-011.
- **FR-011.** **The `[resilience]` line's payload does not grow.** It stays
  `{ subsystem: 'stream', transition: 'playout-target-unsupported',
cameraIdentifier }`. Three reasons: `resilienceLog.ts:3-8` declares the line
  shape an observable contract; `CameraViewerAlignment.test.tsx:365-369` asserts
  it with an exact `toEqual`, so growing it would force an assertion edit, and an
  assertion that has to be edited is a block rather than an adjustment; and
  operationally `'unsupported'` and `'refused'` mean the same thing to the person
  reading it — this tile will not align. The point of the tri-state is to stop
  reporting `'not-connected'`, not to enrich what is reported.

### Both items — the quiet cases

- **FR-012.** Every new signal has a case asserting it stays **quiet** on the
  healthy path, and every such case asserts **first, with a positive
  count, that the double actually ran.** This is 095's review finding carried
  hardest: deleting `!applied &&` there left all 404 tests green while every tile
  permanently claimed a fault, because no double could produce the healthy
  answer. The quiet cases are enumerated in `tasks.md`; each names the
  counterfactual that must fail it.

---

## Acceptance scenarios (Gherkin)

### Item 1

```gherkin
Scenario: A session release rejected by the network says so
  Given a WhepClient that connected and captured a session URL
  When close() is called and the DELETE fetch rejects with "network down"
  Then exactly one [resilience] line is logged with transition "session-release-failed"
  And that line carries error "Error: network down"
  And the peer connection is still closed locally

Scenario: A session release refused by the server says which status
  Given a WhepClient that connected and captured a session URL
  And the caller's token has expired
  When close() is called and the DELETE resolves 401
  Then exactly one [resilience] line is logged with transition "session-release-failed"
  And that line carries status 401

Scenario (quiet): A release that succeeds says nothing
  Given a WhepClient that connected and captured a session URL
  When close() is called and the DELETE resolves 200
  Then exactly one DELETE request was issued          # the double actually ran
  And no [resilience] line with transition "session-release-failed" is logged

Scenario (quiet): A close with no session URL says nothing and asks nothing
  Given a WhepClient whose answer carried no Location header
  When close() is called
  Then no request is issued at all
  And no [resilience] line with transition "session-release-failed" is logged
```

### Item 2 — the three causes, constructed independently

```gherkin
Scenario: A client that never connected is not connected
  Given a WhepClient that has not connected
  When setPlayoutTarget(120) is called
  Then it answers "not-connected"

Scenario: A connected session with no video receiver is not connected
  Given a connected WhepClient whose peer connection reports no receivers
  When setPlayoutTarget(120) is called
  Then it answers "not-connected"

Scenario: An engine whose video receiver has no jitterBufferTarget is unsupported
  Given a connected WhepClient with one video receiver carrying no jitterBufferTarget property
  When setPlayoutTarget(120) is called
  Then it answers "unsupported"

Scenario: An engine whose assignment throws has refused the value
  Given a connected WhepClient with one video receiver whose jitterBufferTarget setter throws
  When setPlayoutTarget(120) is called
  Then it answers "refused"
  And no exception escapes setPlayoutTarget                      # FR-008

Scenario (quiet, and the actuation proof): A receiver that accepts is applied
  Given a connected WhepClient with one video receiver carrying jitterBufferTarget
  When setPlayoutTarget(120) is called
  Then that receiver's jitterBufferTarget is 120                 # FR-009, the positive
  And it answers "applied"

Scenario: An audio receiver carrying the property is not written to
  Given a connected WhepClient with one audio receiver carrying jitterBufferTarget
  When setPlayoutTarget(120) is called
  Then that receiver's jitterBufferTarget is unchanged
  And it answers "not-connected"                                 # FR-007
```

### Item 2 — at the call site

```gherkin
Scenario: A live tile whose client is not connected is not reported
  Given a CameraViewer whose session is live and asked for a 120 ms target
  And setPlayoutTarget answers "not-connected"
  When the alignment effect runs
  Then setPlayoutTarget was called with 120                      # the double ran
  And no [resilience] line with transition "playout-target-unsupported" is logged

Scenario: An engine that cannot hold a target is still reported once
  Given a CameraViewer whose session is live and asked for a 120 ms target
  And setPlayoutTarget answers "unsupported"
  When the alignment effect runs, and the session flaps, and it runs again
  Then the second live window actuated too                       # the flap was real
  And exactly one [resilience] line with transition "playout-target-unsupported" is logged
  And that line equals { subsystem, transition, cameraIdentifier: 'cam-42' }   # FR-011

Scenario: An engine that refuses the value is reported the same way
  Given a CameraViewer whose session is live and asked for a 120 ms target
  And setPlayoutTarget answers "refused"
  Then exactly one [resilience] line with transition "playout-target-unsupported" is logged

Scenario (bad request / hostile engine): setPlayoutTarget itself throws
  Given a CameraViewer whose session is live and asked for a 120 ms target
  And setPlayoutTarget throws
  Then the tile still renders a <video> element                  # spec 045 FR-013
  And exactly one [resilience] line with transition "playout-target-unsupported" is logged
```

**Auth:** no scenario here crosses an authorization boundary that this change
touches. The DELETE's bearer token is minted by the existing `getToken()` and its
401 is now _reported_ rather than granted differently; no scope, realm, client or
policy is read or changed. There is no new endpoint and no new claim.

**Bad request:** the two "hostile engine" shapes above — a receiver whose setter
throws, and a `setPlayoutTarget` that throws outright — are this change's
bad-request analogue at a browser API boundary, and both are required to leave
the picture running.

---

## Locked technical choices

| Concern           | Choice                                                                                     | Why                                                                                                                                                                                                                                                                                                                   |
| ----------------- | ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Outcome type      | **string-literal union**, `PlayoutTargetOutcome`                                           | No payload to carry. Matches `WhepErrorKind` and `ResilienceSubsystem`; a discriminated object union would add a shape for nothing. Spec 095 recorded that the narrowed return buys `TS2367` on a comparison against a name outside the union — the same benefit applies here and is the point of not using `string`. |
| Reporting channel | existing `logResilienceEvent` (ADR-0076 — transport-agnostic)                              | One new transition string, no new module, no widened union, no new dependency.                                                                                                                                                                                                                                        |
| Retry             | **none** (FR-004)                                                                          | The ~30 s ceiling (derived, A1/A2), the 401 that a retry cannot fix, ADR-0143's principle.                                                                                                                                                                                                                            |
| `keepalive: true` | **kept**                                                                                   | It protects an in-flight release across a navigation. Its cost is FR-003's silence, which is now written down.                                                                                                                                                                                                        |
| Test framework    | Vitest + jsdom, existing `WhepClient.test.ts` / `CameraViewerAlignment.test.tsx` harnesses | No new harness; the `receivers` fake widens (`tasks.md` T001).                                                                                                                                                                                                                                                        |

---

## Latency budget impact

**N/A — no §IV cell moves, and no measurement is owed.**

`setPlayoutTarget` is the **actuator** for the _Presentation buffer (playout
alignment) ≤ 200 ms_ leg (ADR-0128), so the claim has to be earned rather than
asserted. It is earned by FR-009: the loop, the receivers visited, the property
written and the value written are unchanged, and `outcome === 'applied'` is the
same predicate as today's `applied`. What changes is the _name_ of the answer and
which answers the call site reports. No effect is added or re-keyed; no render is
provoked (the call site writes a ref, as today); `setPlayoutTarget` is still
called once per effect run with the same argument.

The release report adds a `.then` to a fire-and-forget request that already
exists, after `pc.close()` has been scheduled — it is not on any of the six legs,
and it cannot delay teardown (FR-005).

**No _before_ figure is cited, deliberately.** §IV records the presentation
buffer as _recorded, not yet observed_, and **#1714** is open precisely because
nobody has read that number off a running wall. Inventing a baseline would be the
clerical failure this whole series exists to make visible. The smaller true
claim: after this change a `playout-target-unsupported` line in a kiosk log
means the engine cannot align, which it does not reliably mean today.

---

## Does this need an ADR? No

Argued rather than assumed, because "every spec references ≥1 ADR; if a decision
isn't covered, flag that an ADR is needed" is the rule, and because ADR-0144
forbids this lane from writing one — so "no ADR needed" has to be true, not
convenient.

- **No new dependency, no new package, no new runtime resource.** Nothing is
  added to `AppHost`, no npm package is introduced.
- **No contract changes.** `Shared.Contracts` is untouched; no HTTP DTO, no
  RabbitMQ message, no wire shape moves. `PlayoutTargetOutcome` lives inside
  `apps/shared` and crosses no process boundary.
- **No architectural decision is being made.** The `[resilience]` channel is
  ADR-0076/spec 011's; the string-literal-union idiom is the file's own
  (`WhepErrorKind`); the once-per-tile latch is spec 095's; the swallowed
  `catch` is spec 045 FR-013's, explicitly preserved rather than revisited.
  Every choice here _continues_ an existing pattern.
- **The one thing that would need an ADR is deliberately not done.** Making
  `setPlayoutTarget` raise a fault to an operator would overturn spec 045
  FR-013, and changing MediaMTX's session lifecycle would touch ADR-0128's
  neighbourhood. FR-008 and FR-004 decline both.

Same reasoning as #2197 (spec 141), and it holds for the same reasons.

---

## Assumptions, marked

- **A1 — pion/ice's defaults are the ones in effect.** MediaMTX 1.21.0's
  `go.mod` was not read; the defaults quoted are `pion/ice` `master`. If the
  pinned pion differs, the 30 s figure moves. The _shape_ of the finding — no
  idle-session setting exists, so reclaim is ICE-driven and therefore bounded —
  does not depend on the exact number.
- **A2 — no live MediaMTX was observed.** No container is running and the stack
  was not booted. The reclaim behaviour is derived from the source at the pinned
  tag. Phase 5 can observe it cheaply (`GET /v3/webrtcsessions/list` after
  killing a kiosk tab) and **should**, because this repository's records have
  gone wrong before by recording a derivation as a measurement.
- **A3 — `keepalive` does not prevent the continuation from running.** A
  `keepalive` fetch returns an ordinary promise; it is the _page's_ disappearance
  that strands the continuation, not the flag. Standard behaviour, not verified
  against a browser here.

## Not in scope

- **`useWhepSession.ts`'s state machine** — #2157, `agent:blocked` behind #1714.
  This spec changes **two lines** in that file (the interface's return type at
  `:48` and the `?? false` default at `:332`); it touches `transitionTo`,
  `statusRef`, the connection effect, `scheduleRetry` and the cleanup **not at
  all**. See `plan.md` §"The two lines in `useWhepSession.ts`, and why they are
  not #2157" — this is the one place the spec's reading is narrower than
  "`useWhepSession.ts` is untouched", and it is stated rather than slipped in.
- **`CellPage.tsx`** — spec 141 / #2197, in flight on a disjoint branch.
- **`CameraViewer.tsx`'s samplers and the lag/decode predicates** — spec 095,
  merged and unchanged here apart from the FR-010 condition.
- **A bounded retry on release** — FR-004, declined with its number.
- **Surfacing an alignment fault on the tile** — spec 045 FR-013, FR-008.
