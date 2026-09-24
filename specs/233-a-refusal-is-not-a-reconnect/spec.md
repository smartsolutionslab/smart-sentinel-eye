# Spec 233: A refusal is not a reconnect

**Issue**: #2355 · **Branch**: `fix/2355-whep-refusal-terminal-state` · **Phase**: 1 (Specify)
**Date**: 2026-09-24 · **Base**: `396c4fa7` (branch cut from `origin/develop`)
**Context**: frontend only, `apps/shared/src/ui/composites` (used by `kiosk-web` and `management-web`). No bounded context, no C#, no contract change.
**Engineer**: `frontend-engineer` · **Reviewer**: `frontend-reviewer` (+ `security-reviewer`, since this is the credential path)
**Lane**: autonomous (ADR-0144)
**Phase 4a colour**: **RED** (§6). The change adds a terminal state, a rendered refusal and a new log line.
**Scope**: issue points **1–3 only**. Point 4 (should a refusal ever be re-attempted, and on what trigger) is **out of scope** and goes back to the orchestrator as a follow-up (§8).
**ADRs**: ADR-0037 (phases), ADR-0144 (lane), ADR-0139 (new behaviour observed red), ADR-0036 (smallest change), ADR-0143 (a retry must not re-send a credential that cannot succeed; spec 142 applied it to the release path), ADR-0153 (one instance per service, which is why the hook has restart windows, §5 R1), ADR-0118 (one telemetry sink; the resilience line reaches it), ADR-0109 (disjoint files).
The issue cites ADR-0076. That ADR is superseded by ADR-0152 and covers SignalR transport, not WHEP, so this spec does not rely on it.
**Constitution**: §IV: **N/A for all six legs** (§7). §Availability (24/7): see risk R1. §Testing: new behaviour starts red.
**Amends**: spec 011 FR-003 ("retrying indefinitely"), adding a carve-out for authorization refusals (FR-001 below).
**New ADR needed**: **No**. The change applies ADR-0143's existing argument to one more path and adds no architecture. Point 4 may need one; that is a decision for its own issue.

---

## 1. The premise, re-checked against `396c4fa7`

| # | Issue claim | Status now |
|---|---|---|
| a | `useWhepSession.ts:290-292` sends every `connect()` rejection to `scheduleRetry` | **Holds, now at `:343-346`.** |
| b | `WhepClient.postOffer` throws `WhepError('unauthorized')` on 401 and `('forbidden')` on 403, at `WhepClient.ts:283-286` | **Holds, and the file has moved** to `apps/shared/src/streaming/WhepClient.ts:281-289` (`errorForResponse`). One detail: a 403 whose body contains `unavailable` becomes `'stream-unavailable'`, not `'forbidden'`. |
| c | `scheduleRetry` has no attempt ceiling and no terminal state | **Holds** (`:237-248`). The cap applies only to the delay (`RETRY_CAP_MS = 15_000`). |
| d | `transitionTo` is only called with `offline`, `reconnecting`, `live`, `connecting` | **Holds.** |
| e | "there is no `error` status at all" | **Wrong.** `CameraViewerStatus` at `:8` already includes `'error'`. The hook never produces it. `CameraViewer.tsx` already handles it (`labelFor` `:572` → `'Viewer error'`; `ViewerOverlay` tone `:501` → `text-accent-fault`), so that code exists but is never reached. |
| f | `FrameGrabber.tsx:84` branches on `status === 'error'` | **No longer true.** That arm was removed. `FrameGrabber.tsx:95-98` now says in a comment that `'error'` is unreachable and was dropped. Its fail-fast arm (`:99`) is `reconnecting \|\| offline`. **This change makes `'error'` reachable, so FrameGrabber has to be updated too** (FR-006). |
| g | "Nothing reports it" | **Partly true.** `transitionTo` already logs every transition generically (`connecting→reconnecting`), but no line names a refusal or its kind. |
| h | "re-POSTs the same dead credential" | **Not quite.** Each attempt reads `getToken()` again (`getTokenRef`, `:134-137`), and all three consumers read the latest `auth.user?.access_token` from a ref. A retry after a silent renewal therefore sends the *new* token. This matters for point 4 (§8). |

**A fact the issue does not mention, and it changes what a 401 means.**
The browser POSTs directly to MediaMTX (`StreamWhepUrlBuilder`, no gateway in between). Spec 119's verification (§2, measured against MediaMTX 1.21.0, the version pinned at `AppHost.cs:188`) shows that MediaMTX answers the browser with the same `401 {"status":"error","error":"authentication error"}` for **every** non-2xx from `/streams/authorize`: a refusal, a hook `500`, and a hook it cannot reach at all. The same applies to a `429` from spec 208's limiter, whose only partition is MediaMTX's own IP. Two consequences:

- In this deployment `'forbidden'` has not been observed. Refusals reach the browser as `'unauthorized'`. Phase 5 records which kind actually arrives (§4, step 5).
- On the client, a genuine refusal and an infrastructure outage look identical. This spec makes both terminal. That is the accepted risk R1 (§5), and it is why point 4 matters (§8).

**Exhaustiveness fallout: none.** `CameraViewerStatus` and `useWhepSession` are referenced only by `useWhepSession.ts`, `CameraViewer.tsx` and `FrameGrabber.tsx` (grep across `apps/`). No `switch` or `never` check exists over the union, and `kiosk-web`/`management-web` only pass `getToken`. The union does not change, because `'error'` is already a member.

---

## 2. User stories

### US1 (P1): A refused tile says so and stops asking

An operator or a wall viewer whose credential the stream server refuses sees a tile that reads **Access refused**, not **Reconnecting…**. The tile makes no further WHEP POSTs on its own. The refusal produces one resilience line that names the camera and the refusal kind.

**Why P1**: this is the whole issue. A wall with a revoked kiosk credential currently sends every tile's token to the SFU every 15 s indefinitely, while telling the operator it will recover.

**Independent test**: render `CameraViewer` with a WHEP endpoint answering `401`. The tile reads *Access refused*. Advance 60 s: there is still exactly one peer connection and one POST, and one `whep-refused` resilience line.

### US2 (P1, same slice): A frame capture fails fast on a refusal

The overlay editor's frame capture (`FrameGrabber`, spec 147) still gives up on a refused session immediately, instead of waiting for its 10 s outer timeout.

**Why the same slice**: US1 alone would introduce this regression. Today a refusal reaches FrameGrabber as `reconnecting`, which already fails fast. After US1 it arrives as `error`, which FrameGrabber ignores. The two ship together or not at all.

---

## 3. Acceptance scenarios

```gherkin
Feature: WHEP authorization refusal ends the session

  Background:
    Given a CameraViewer for camera "cam-42" whose stream health is Healthy

  # Happy path (the new behaviour)
  Scenario: A 401 from the WHEP endpoint ends the session as a refusal
    Given the WHEP endpoint answers the offer with 401
    When the tile attempts to connect
    Then the tile reads "Access refused"
    And its status region announces "Access refused. The stream server refused this viewer. This tile will not retry on its own."
    And one resilience line is logged with subsystem "stream", transition "whep-refused", cameraIdentifier "cam-42", kind "unauthorized"
    And after 60 seconds exactly one WHEP POST has been made

  Scenario: A 403 without "unavailable" in the body is also a refusal
    Given the WHEP endpoint answers 403 with body "forbidden"
    When the tile attempts to connect
    Then the tile reads "Access refused"
    And the resilience line carries kind "forbidden"
    And after 60 seconds exactly one WHEP POST has been made

  Scenario: A refusal on a retry ends the ladder too
    Given the first WHEP POST fails at the network level
    And the second WHEP POST is answered with 401
    When the retry fires
    Then the tile reads "Access refused"
    And no third POST is made within 60 seconds

  # Conflict: what must NOT become terminal
  Scenario Outline: Transient failures stay on the reconnect ladder
    Given the WHEP endpoint answers <response>
    When the tile attempts to connect
    Then the tile reads "Reconnecting…"
    And a second WHEP POST is made after the first backoff step
    And no "whep-refused" line is logged
    Examples:
      | response                                  |
      | 403 with body "stream unavailable"        |
      | 500                                       |
      | a network failure (fetch rejects)         |

  # Exits: the terminal state is left only by an outside change, one attempt each
  Scenario: Changing the camera leaves the refused state
    Given the tile for "cam-42" reads "Access refused"
    When the tile is re-propped to camera "cam-43" whose endpoint answers 201
    Then exactly one WHEP POST is made for "cam-43"
    And the tile no longer reads "Access refused"

  Scenario: A Degraded-to-Healthy recovery makes exactly one fresh attempt
    Given the tile reads "Access refused"
    When stream health goes Degraded and then Healthy
    Then exactly one further WHEP POST is made
    And if that POST is refused as well, the tile reads "Access refused" and makes no more

  # Bad request / auth: no credential at all
  Scenario: A tile with no token is refused, not retried
    Given getToken resolves null
    And the WHEP endpoint answers 401
    When the tile attempts to connect
    Then the tile reads "Access refused"
    And no second POST is made within 60 seconds

  # Frame capture (US2)
  Scenario: A refused frame capture fails without waiting for its timeout
    Given the overlay editor's frame capture is in flight for "cam-42"
    And the WHEP endpoint answers 401
    When the session is refused
    Then onFailed fires before the 10 second capture timeout
    And exactly one WHEP POST was made

  # Unmount
  Scenario: Unmounting a refused tile makes no request
    Given the tile reads "Access refused"
    When the tile unmounts
    Then no further WHEP POST is made
```

---

## 4. End-to-end test procedure (phase 5)

The procedure repeats spec 119 §2's technique: repoint MediaMTX's hook at a stub that returns a chosen status, keeping the offer and path the same.

1. Boot the AppHost (`MSYS_NO_PATHCONV=1`; one stack per machine). Open the kiosk wall for a published layout containing at least two cameras. Record every tile reading **Live**.
2. `PATCH /v3/config/global/patch` on MediaMTX's API to set `authHTTPAddress` to a local stub that answers `403` (the cross-fab refusal #2092 will produce).
3. Force the tiles to reconnect. Either restart the MediaMTX path, following the memory note "provoking a stream outage", or reload the wall.
4. **Observe**: every affected tile reads **Access refused** within ~5 s (MediaMTX pauses ~2 s on an auth failure). The browser console has exactly one `[resilience] {subsystem:'stream', transition:'whep-refused', …}` per tile. Over the next **120 s** the stub receives **one** call per tile, not ~8.
5. **Record the kind** the browser logged. Spec 119 predicts `'unauthorized'`, even though the stub answered 403. Write down what was actually observed.
6. Patch `authHTTPAddress` back to the real hook and confirm WHEP opens return `201`. Reload the wall and confirm the tiles return to Live. This is the recovery path until point 4 is decided.
7. Management console: repeat steps 2–4 on `CameraDetailPage` and on the overlay editor's frame capture. The capture shows the FR-016 could-not-capture message without the 10 s wait.

---

## 5. Requirements

- **FR-001**: When `WhepClient.connect()` rejects with a `WhepError` whose `kind` is `'unauthorized'` or `'forbidden'`, `useWhepSession` MUST move to `'error'` and MUST NOT schedule a retry. This carves an exception into spec 011 FR-003.
- **FR-002**: Every other rejection (`'stream-unavailable'`, `'network'`, `'sdp'`, a non-`WhepError`) MUST follow today's ladder unchanged.
- **FR-003**: `'error'` MUST be left only by a change that already re-runs the session effect: camera change, `whepUrl` change, an Offline→non-Offline change, a Degraded→Healthy recovery, or remount. Each of those makes **exactly one** fresh attempt. No timer may leave `'error'`.
- **FR-004**: On entering `'error'` from a refusal, the hook MUST emit `logResilienceEvent('stream', 'whep-refused', { cameraIdentifier, kind })` once. The existing generic `…→error` transition line still fires as well.
- **FR-005**: `CameraViewer` MUST label `'error'` **Access refused**, keep the existing fault tone, and show the hint *"The stream server refused this viewer. This tile will not retry on its own."* The status region MUST announce the same text (spec 228's "announced equals painted" rule). The hint MUST NOT show the raw server body, which from MediaMTX is JSON noise.
- **FR-006**: `FrameGrabber` MUST treat `'error'` as a failed capture (`onFailed`) exactly as it treats `'reconnecting'` and `'offline'`, and its stale "`'error'` is not reachable" comment MUST be corrected.
- **FR-007**: The `failedRead` path's **Viewer error** label (a failed stream read with no session) MUST NOT change.
- **NFR-001**: No change to steady-state timing on any §IV leg (§7).

### Risks

- **R1: accepted, needs written acceptance before phase 4 in the autonomous lane.** Under MediaMTX 1.21.0 a hook that is down, restarting (StreamDistribution runs one instance, ADR-0153), throttled, or unable to reach Keycloak reaches the browser as the same `401` as a real refusal (§1). Any tile that happens to reconnect during such a window **stays on "Access refused" until one of FR-003's triggers fires or the page is reloaded**. Today the same tile recovers by itself within ≤15 s. The trade is a retry storm plus a misleading "Reconnecting…" against a possibly-dark but honestly labelled tile. The label says the tile will not retry, and the resilience line makes the state visible. Point 4 (§8) is the proper fix. Until it lands, reloading the wall is the recovery.
- **R2**: an access token that expires between silent renewals, with a reconnect landing in that gap, is now terminal where it used to self-heal. The window is small (`automaticSilentRenew` renews before expiry), but it is a 24/7 wall. Point 4 covers this case too.

---

## 6. Phase 4a colour: RED

This is behaviour-changing, so every new-behaviour test must be observed failing first (ADR-0139). What red looks like on `396c4fa7`:

- *reads Access refused* → `Unable to find an element with the text: Access refused` (the tile shows `Reconnecting…`).
- *exactly one POST after 60 s* → `expected [ … ] to have a length of 1 but got 7` (1 s, 2, 4, 8, 15, 15, … ladder).
- *whep-refused line* → `expected "spy" to be called with arguments: [ '[resilience]', ObjectContaining{ transition: 'whep-refused', … } ]`.

The ladder scenarios for transient failures, the camera-swap exit and FrameGrabber's fast fail on a refusal are **characterisation**: they pass today and must pass unmodified afterwards. They are recorded green in the same 4a run. The FrameGrabber one is the net that catches the regression FR-006 prevents.

---

## 7. Latency budget (§IV)

**N/A for all six legs.** The change affects only a failed session setup: a WHEP POST that is refused never reaches Camera→SFU, SFU→decode, the presentation buffer, overlay state or composite/render. On the live path nothing new happens per frame or per event. The one added check is a type test inside a rejection handler.

---

## 8. Out of scope: point 4 (returned to the orchestrator)

Should a refused tile ever re-attempt on its own, and on what trigger? That is a design decision, and ADR-0144 does not allow the lane to make one. §5 R1/R2 record what this spec costs until it is decided. The orchestrator's report carries the precise statement for filing.

Also out of scope: #2092 (fab check in the authorize hook), any change to `WhepClient`, and any MediaMTX version or config change.
