# Spec 465 — An overlay seed that times out is retried

**Feature Branch**: `fix/3182-retry-reverse-index-seed`
**Created**: 2026-10-27
**Status**: Draft
**Issue:** #3182 (related, closed: #2158 / spec 126 — the same symptom from a refused credential).
**ADRs:** ADR-0116 (a degraded start beats no start; the seeder's own class doc cites it),
ADR-0143 (the standard handler retries `GET`; that retry is *inside* one attempt and is what the
30 s timeout in the report cut short), ADR-0049 (`CancellationToken` last, no `ConfigureAwait`),
ADR-0139 / ADR-0144 (red first for behaviour-changing work). **No new ADR:** the owner chose
"retry only" on the issue; the alternatives it lists (AppHost `WaitFor`, self-heal on read) are
out of scope below.
**Latency budget:** touches leg **Event → overlay state (≤ 200 ms)** by *availability*, not
latency. While the index is empty no overlay text resolves at all; a retry restores it. No
per-event latency changes. §VII's dashboard obligation for an implemented leg is for `plan.md`
to check; this spec adds no measurement of its own.
**Phase 4a colour:** behaviour-changing → **red first**.

**Input**: User description: "Issue #3182 — `system-variables` starts with an empty reverse index when
the cold-start overlay seed times out, and never recovers."

## Problem

`ReverseIndexSeederHostedService.StartAsync` (src/SystemVariables/Infrastructure/Resolution/ReverseIndexSeederHostedService.cs)
asks `overlay-designer` once for `GET /overlays?state=Published` and fills the in-memory
`InMemoryReverseIndex` from the answer. Any failure other than the two refusal statuses is logged
once and abandoned: a timeout or transport failure goes to `SeedFailed`, a non-success status to
`SeedNonSuccessStatus`. Both messages promise *"The index will populate as new
OverlayRevisionPublishedV3 events arrive."*

That promise is false for every overlay published **before** the process started: such an overlay
raises no event, so nothing ever refills the index. Observed 2026-10-27 with the whole stack
booting at once: the seed hit the 30 s resilience timeout
(`Polly.Timeout.TimeoutRejectedException`), `GET /system-variables/-/snapshot?overlayIdentifier=…`
answered `404 OVERLAY_NOT_IN_REVERSE_INDEX` for a published overlay, and the kiosk rendered the raw
template `INSPECTION: {{inspection_last_defect}}`. Restarting only `system-variables` with
`overlay-designer` already serving made the same call answer `200` and the kiosk show
`INSPECTION: MMI TEST FAIL` — the data was never the problem.

In production (24/7, k3s) a `system-variables` pod that restarts while `overlay-designer` is briefly
unavailable returns with an empty index, and overlay text stops resolving on every wall until
someone restarts the pod.

## User Scenarios & Testing

### User story 1 (P1) — the index fills itself once overlay-designer is reachable

As a wall operator, I want overlay text to resolve after a cold start even when `overlay-designer`
was slow or briefly down, so that I never have to restart a service to get my labels back.

**Independent test:** a seeder whose first call fails and whose second succeeds ends with a
populated index. Fails today; passes with the fix.

```gherkin
Scenario: The first seed attempt times out, a later one succeeds
  Given overlay-designer does not answer the first seed request within the allowed time
  And it answers the next request with 2 published overlays
  When system-variables has been running long enough for the second attempt
  Then the index holds both overlays
  And GET /system-variables/-/snapshot for either overlay answers 200 with resolved text
  And no restart of system-variables was needed

Scenario: A non-success status is retried the same way
  Given overlay-designer answers 503 to the first request and 200 to the second
  Then the index is populated after the second attempt

Scenario: A transport failure is retried the same way
  Given the first request fails with a connection error and the second succeeds
  Then the index is populated after the second attempt

Scenario: A successful first attempt is unchanged (regression)
  Given overlay-designer answers the first request with 2 published overlays
  Then the index holds both after that one request
  And exactly one request was made
  And "ReverseIndex seeded with 2 published overlays." is logged, word for word as today

Scenario: Retries stop at the first success
  Given a seed that succeeded on its third attempt
  Then no fourth request is made, however long the host keeps running
```

### User story 2 (P1) — starting never waits on, or fails because of, the seed

As an SRE, I want `system-variables` to come up whether or not `overlay-designer` is reachable, so
that a degraded index is never traded for no service (ADR-0116).

```gherkin
Scenario: The host does not wait for a seed that keeps failing
  Given overlay-designer is unreachable
  When system-variables starts
  Then the host reports started without waiting for a successful seed
  And the process keeps serving its other endpoints and consuming its events
  And the seed keeps retrying in the background

Scenario: Shutdown ends the retry loop
  Given the seed is between two attempts
  When the host is stopped
  Then the loop ends promptly
  And no failure is logged for the cancellation
```

### User story 3 (P2) — the logs say what is actually happening

As the on-call engineer, I want the seed's log lines to tell me the index is empty, that a retry is
scheduled, and when it succeeded, so that I stop reading a reassurance that is not true.

```gherkin
Scenario: A failed attempt says the index is empty and a retry is coming
  Given a seed attempt fails with a timeout
  Then a Warning is logged naming the failure, the attempt number and the delay before the next attempt
  And the message does not say events will repopulate the index

Scenario: Success after failures is visible
  Given a seed that failed twice and then succeeded
  Then the existing "seeded with N published overlays" line is logged once, at Information
```

### User story 4 (P2) — a late seed never undoes what events already established

As a wall operator, I want an overlay archived or republished while the seed was still retrying to
stay that way, so that a late listing does not resurrect an archived overlay or restore stale text.

```gherkin
Scenario: An overlay archived before the late seed lands stays gone
  Given the seed request was answered with a listing containing overlay O
  And OverlayRevisionArchived for O was handled after the listing was produced and before it was applied
  When the listing is applied
  Then O is not in the index

Scenario: A republish handled before the late seed lands keeps its newer text
  Given the listing carries O at revision 3 with text T3
  And OverlayRevisionPublishedV3 for O at revision 4 with text T4 was handled before the listing was applied
  When the listing is applied
  Then the index holds T4 for O
```

### Edge cases

- **A 200 whose body has no `published` key** is a failed attempt like any other (logged as today's
  `SeedMissingPublishedKey`, then retried): a contract mismatch is not fixed by giving up either, and
  a rolling deploy can serve an older shape for a while.
- **Overlay-designer answers, with an empty `published` list**: success with a count of zero —
  unchanged; it ends the loop.
- **Overlays in the listing without a usable identifier or `elements`** keep being skipped, not
  counted (specs 349/358); a listing that is otherwise valid is a success.
- **Events arrive while the index is still empty** (the normal self-heal): they upsert as today and are
  never discarded by the loop.
- **The standard resilience pipeline already retries `GET`** (ADR-0143) inside one attempt, so one
  *attempt* here can take up to the 30 s timeout. The loop's delay is between attempts and is added
  to that, not a replacement for it.

## Requirements

### Functional requirements

- **FR-001** A seed attempt that fails — timeout, transport failure, any non-success status
  or a refusal (FR-008), or a 200 without `published` — is retried until one succeeds or the host
  stops. There is no attempt limit.
- **FR-002** The delay between attempts grows and is capped, so a long outage costs a request every
  few tens of seconds, not a flood. Initial delay, factor and cap are fixed in `plan.md` and are
  injectable for tests (a fake `TimeProvider` / delay seam, ADR-0054: no real waiting in unit tests).
- **FR-003** Host start does not wait for a successful seed, and does not wait for a failing one:
  `StartAsync` returns without blocking on the first attempt's outcome. (Today it awaits the first call,
  up to the 30 s timeout.)
- **FR-004** After a successful attempt the loop ends and the index holds every overlay the listing
  carried; no further requests are made.
- **FR-005** Cancellation (host shutdown) ends the loop promptly and is not logged as a failure
  (ADR-0049; unchanged from today's `A_cancelled_token_propagates_and_logs_nothing`).
- **FR-006** Applying a listing never overwrites or resurrects state an event has set since the
  process started: an overlay removed by an archive event stays removed, and one upserted by a publish
  event keeps that event's text.
- **FR-007** A failed attempt logs at `Warning` with the failure (status or exception), the attempt
  number and the delay to the next one, and **does not claim that events will repopulate the index**.
  The success line `ReverseIndex seeded with {Count} published overlays.` is unchanged in level and
  text (it is pinned by spec 349's characterisation).
- **FR-008** A refused seed (401/403, including a refused token mint) is retried like any other
  failure, so a corrected credential or a restored Keycloak takes effect without restarting
  `system-variables`. It keeps its own level, `Error`, and its own wording (it names the refusal, the
  attempt number and the delay) so it stays distinguishable from a timeout; it no longer says the
  index "stays empty until … the service restarted". This supersedes spec 126's one-shot behaviour
  for the refusal case (#2158); everything else spec 126 established (the refusal is logged at
  `Error`, the host still starts) stands.
- **FR-009** The first call is unchanged where it already works: same URL
  (`/overlays?state=Published`), same named client, same service account, same parse of
  `overlayIdentifier` / `elements` / `text`. No new configuration is required to get the retry.

### Key entities

- **Reverse index** (`IReverseIndex`, in-memory): overlay → the label texts that reference variables.
  Filled by the seed and by publish/archive events; lost on every process start.
- **Seed attempt**: one `GET /overlays?state=Published` against overlay-designer, including the
  resilience pipeline's own internal retries.

## Success Criteria

- **SC-001** With `overlay-designer` unreachable for the first N seconds and reachable afterwards, the
  index is complete within one capped delay plus one request after it becomes reachable, with no
  restart of `system-variables`.
- **SC-002** Zero `404 OVERLAY_NOT_IN_REVERSE_INDEX` answers for overlays that were published before
  the process started, once that seed has succeeded.
- **SC-003** `system-variables` reports started no later than it does when the seed succeeds at once
  — not 30 s later because overlay-designer was slow.
- **SC-004** No seed log line contains the claim that events will repopulate the index for overlays
  published before the process started.
- **SC-005** Verified end to end on the live stack (phase 5): boot with `overlay-designer` held back,
  observe `404` on the snapshot, release it, observe `200` and the resolved text on the wall without
  touching `system-variables`.

## Existing tests this change is expected to move

This is **behaviour-changing** (phase 4a red), so these are expected to change, and the test-writer
re-states them — it is not a characterisation block:

- `ReverseIndexSeederBranchCharacterisationTests.A_500_logs_the_self_healing_warning_naming_that_status`
  (spec 349) pins today's Warning text, which carries the self-heal sentence FR-007 removes.
- `ReverseIndexSeederHostedServiceTests.A_server_error_stays_a_warning`, `A_404_…` and
  `A_transport_failure_that_is_not_a_refusal_stays_a_warning`: the **level stays Warning**, the words
  and the "one request, then done" shape do not.
- `A_refused_*` and `A_refused_token_mint_*` (spec 126): FR-008 answers "retry", so these move too —
  the level stays `Error`, the "one request, then done" shape and the "restart the service" wording do not.
- Everything about parsing a listing (specs 300, 349, 358) must stay green **unmodified**: that part is
  behaviour-preserving and is the characterisation net for the rewrite.
- `SystemVariablesRegistrationCharacterisationTests` pins `IHostedService → ReverseIndexSeederHostedService`;
  the plan keeps that registration, or states why not.

## Out of scope

- `AppHost` `WaitFor(overlay-designer)` ordering: dev-only, and does not help a production restart.
- Answering a missing overlay by asking `overlay-designer` at read time (self-heal on read): a new
  cross-context call on the read path, a separate decision.
- Persisting the index, or any change to `IReverseIndex`'s role as in-memory state.
- Any change to what overlay-designer returns, or to the `system-variables-seeder` credential.

## Assumptions

- Retry runs for the life of the host, in the background; "give up after N attempts" would recreate
  the defect after a long outage.
- A capped delay of a few tens of seconds is acceptable both for cold-start recovery and as steady
  load on an unreachable overlay-designer; the exact numbers are a plan decision, not a spec one.
- The seed's race with events (FR-006) exists today at start-up but is narrow; unbounded retry widens
  it to minutes, which is why it is a requirement here and not a footnote.
- No new metric or dashboard is added by this spec; the log lines in FR-007 are the observable.
  `plan.md` checks constitution §VII for the leg named above and says what it finds.
- ADR-0116's trade-off stands: a degraded index is acceptable, a refusal to start is not.
