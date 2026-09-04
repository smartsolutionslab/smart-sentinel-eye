# Feature Specification: A variable change reaches the tile

**Feature Branch**: `fix/2012-a-variable-change-reaches-the-tile`

**Created**: 2026-09-04

**Status**: Draft — phases 1–3 complete, phase 4a not started

**Issues**: #2012

**Lane**: ADR-0144 autonomous. `agent:ready` present, `agent:blocked` absent,
board status *In Progress*.

**ADRs referenced**: ADR-0102 (the `EventMetadata` envelope), ADR-0115 (a
variable resolves in the viewer's fab), ADR-0112 §3/§5 (the layout hub's frame
set), ADR-0129 (the label is aged, not frame-synced), ADR-0139/ADR-0140 (red
first), ADR-0144 (this lane), ADR-0138 (spec 056's finding), constitution §IV.

**Input (#2012)**: "A variable change does not reach an online kiosk tile, and
no test has ever covered that path. […] The tile keeps its old text for 60
seconds. […] Tile **opened afterwards** shows the new value immediately."

---

## The defect, located

**Phase 1 found the cause.** This is not a spec over an open investigation; the
chain is complete, static, and it explains every symptom the issue reports —
including the one from the accident in its second comment.

### The producer stamps no fab

`src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:65-74`

```csharp
ResolvedOverlayTextChangedV1 @event = new(
    Overlay: overlayId,
    ResolvedText: resolvedText,
    Version: version,
    Metadata: new(
        Guid.CreateVersion7(),
        changedAt,
        null,                    // <- line 72. This is EventMetadata.Fab.
        changedBy.Value));
```

The third positional parameter of `EventMetadata` is `Fab`
(`src/Shared.Contracts/EventMetadata.cs`). The fab is **in scope on line 33** of
the same method — `var (variable, fab, name, type, value, changedAt, changedBy, _)
= domainEvent;` — and is used correctly four lines away, on the
`SystemVariableValueChangedV1` published immediately above it.

`VariableArchivedDomainEventHandler.cs:97-105` is the same construction with the
same `null`, at line 104.

These are the **only two** publishers of `ResolvedOverlayTextChangedV1`
(`grep -rn "ResolvedOverlayTextChangedV1" src/SystemVariables`, corroborated by
the two call sites of `IReverseIndex.NextVersionFor`, which is the version
counter every push must take).

### The consumer drops what carries no fab

`src/LayoutComposition/Application/EventHandlers/ResolvedOverlayTextChangedV1Handler.cs:32-37`

```csharp
if (string.IsNullOrWhiteSpace(metadata?.Fab))
{
    logger.ResolvedOverlayTextChangedWithoutFab(overlay, version);

    return;
}
```

So **no `ResolvedOverlayTextChanged` SignalR frame is ever sent for a variable
change.** The broadcaster is never reached; the hub group is never addressed;
the kiosk's `onResolvedOverlayTextChanged` callback never fires.

The comment three lines above that guard, written when it was added, is exact:

> Said out loud, because a silent drop here looks exactly like a kiosk that
> simply never updated.

### Nothing in between repairs it

Checked, because the claim is only as good as its weakest link:

- `src/ServiceDefaults/OutboxEventBus.cs` is the sole `IEventBus`
  implementation (`grep -rn ": IEventBus" src`). It contains **no** occurrence
  of `Metadata` or `Fab` — it does not enrich.
- No `with { Fab = … }` or equivalent rewrite exists anywhere in `src`.
- `LayoutCompositionInfrastructureModule.cs:101` registers exactly one
  subscriber for this event. There is no second path to the broadcaster.

### When it broke, and why it looked fine

`git log -L` on the guard: it landed **2026-08-10** in
`22178f7 feat(layout-composition): deliver a resolved-text push only to its own fab`
(spec 017 FR-015). Before that, the frame went to `Clients.All` and the null fab
cost nothing.

The `null` itself dates from `958e190 feat(contracts): common EventMetadata
envelope on integration events (ADR-0102)` (2026-05-29), which set `Fab: null` on
*both* SystemVariables events. `SystemVariableValueChangedV1` was later given
`fab.Value` when the fab reached the domain event; `ResolvedOverlayTextChangedV1`
was not. So a consumer was tightened around a producer that had never been
completed, and the two were only ever tested apart.

---

## Verifying the premise, with commands

The issue asks to be checked rather than believed. Three of its claims survive,
one of its pointers is stale, and one thing it treats as unknown is now known.

### Confirmed — "the value not propagating" was correctly ruled out

The HTTP read path is genuinely fine. `GET /system-variables/snapshot` resolves
per fab from persisted state, which is why the operator page sees the value and
why a tile opened *afterwards* renders it. The push path is the broken one. The
defect above explains the split precisely, and it is the only reading that does.

### Confirmed — the label hold (ADR-0129) is correctly ruled out

Not by reasoning from the symptom but by reading the arithmetic.
`apps/shared/src/observability/labelDelay.ts` returns `null` for a `null` frame
age, and `useLabelDelay` returns `text` unchanged whenever
`isWorthDelaying(delay)` is false. On a wall with no video the frame age is
`null`, so the hold cannot engage and the label passes straight through. The
issue's "fails identically on a wall with no video" is therefore a valid
elimination, not a coincidence.

### Confirmed — the locator was correctly ruled out

`apps/kiosk-web/src/features/cell/CellPage.tsx:325` renders through
`CameraViewer`, and the check asserts on `camera-viewer-overlay-label`, which is
the element that carries the text. Switching to `layout-tile` widens the net
over the same subtree, so it could not have changed the outcome.

### Corrected — the artefact the issue points at no longer exists

> **Where it is:** `e2e/kiosk-label-follows-its-variable.spec.ts`, marked
> `test.fixme` […]

That file was **deleted** in `c8ed90c fix(056): the gate was wrong because I read
a truncated search as absence` — the same commit's message says "that file is
gone", because it drove the same variable as a check in an alphabetically-earlier
file. The surviving `test.fixme` is
**`e2e/kiosk-shows-a-label-over-video.spec.ts` → 'the span from a value being
submitted to it being visible'**, and its docblock carries the same evidence.
This spec works against that file.

### Corrected — "nothing has ever tested this path" is true, and one record said otherwise

The issue is right, and understated. `grep -rn "ResolvedOverlayTextChanged"
tests/` returns unit tests on **each side** of the seam and nothing that crosses
it:

| Level | Exists | Covers |
|---|---|---|
| `VariableValueChangedDomainEventHandlerTests` | yes, **6 passing** | the producer publishes *an* event |
| `ResolvedOverlayTextChangedV1HandlerTests` | yes, **3 passing** | the consumer broadcasts, and drops a fab-less frame |
| a `ResolvedOverlayTextChanged` **frame off a real hub** | **no test anywhere** | — |
| a kiosk tile re-rendering on that frame | **no test anywhere** | — |

Both sides were run during phase 1 and are green today:

```
$ dotnet test tests/SystemVariables.Application.Tests/… --filter "…VariableValueChangedDomainEventHandlerTests|…VariableArchivedDomainEventHandlerTests"
Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6

$ dotnet test tests/LayoutComposition.Application.Tests/… --filter "…ResolvedOverlayTextChangedV1HandlerTests"
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```

Nine green tests over a broken product. The consumer's tests construct their own
metadata with `MetadataFor("munich")`; the producer's assert nothing about
metadata at all. **The defect lives in the exact gap the two suites leave.**

**And one docblock claimed that gap was covered.**
`tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs`
says its measurement "excludes the SignalR hop to the kiosk, which
`OverlayPushIntegrationTests` covers separately". That file covers
`OverlayRevisionPublished` — a different frame, from a different context, on a
different trigger. The resolved-text hop is covered by nothing. A cross-reference
that made a gap look closed is part of why this survived, and this spec repairs
the sentence.

### New evidence, from the running system

A run-mode Postgres from an earlier stack is still up (`postgres-18bcf406`, 23 h,
persistent volume), so real recorded history was queryable without booting
anything:

```
$ docker exec -e PGPASSWORD=… postgres-18bcf406 psql -U postgres -d audit-db \
    -c "select event_kind, count(*) n, count(fab_id) with_fab from audit_events group by event_kind order by n desc;"

         event_kind          |  n   | with_fab
-----------------------------+------+----------
 FabEventIngestedV1          | 1103 |     1103
 OverlayHighlightRequestedV1 |  408 |      408
 SystemVariableDefinedV1     |    3 |        3
 SystemVariableArchivedV1    |    3 |        0     <- the sibling null, in production data
 …
```

`SystemVariableDefinedV1` carries its fab on every row; `SystemVariableArchivedV1`
carries it on none. That is the same `null`-in-position-three defect, observed in
real rows rather than read off a source file — direct empirical corroboration of
the static finding, from a different handler in the same context.

`ResolvedOverlayTextChangedV1` has **zero** rows in that database: no variable
value was ever changed against this long-lived stack, so the event itself was
never produced there. Its absence is not evidence either way, and is recorded so
nobody later reads the empty result as proof.

### What remains genuinely unknown

**Whether the frontend half also has a defect.** Once the frame is sent, it has
to travel `useLayoutLifecycle` → `CellPage.onResolvedOverlayTextChanged` →
`systemVariablesApi.util.upsertQueryData` → the tile's
`useGetOverlaySnapshotQuery` → `useLabelDelay` → the rendered label. Every hop of
that is plausible on reading, and **not one of them has ever been exercised**:
`CellPage.test.tsx` has 16 tests and none mentions resolved text;
`useLayoutLifecycle.test.tsx` proves only that the hook forwards its callback.

So the honest position is: the server-side defect is proven and fully explains
the reported symptoms; a second, independent frontend defect is not excluded.
This spec therefore covers the frontend hop with its own test (T004) and treats
its colour as information rather than assuming it. If it comes back red, phase 4b
has a second defect and the report says so rather than folding it in silently.

**Not claimed:** that this is the only reason a tile could fail to update. It is
the only one found, and it is sufficient.

---

## User Scenarios & Testing

### User Story 1 — the operator who changes a figure while the wall is watching (P1)

An operator changes a system variable's value in the management console. Every
kiosk in that fab already displaying a wall bound to an overlay that references
the variable shows the new value, without anyone reloading a page, reconnecting,
or touching the kiosk. This is the state a fab wall is in permanently, and it is
the one state the product does not currently serve.

**Independently shippable.** One field on one event, and the whole path lights
up. Nothing else is required for it to be observable end to end.

#### Acceptance scenarios (Gherkin)

**Happy path**

```gherkin
Given a kiosk in fab "munich" is displaying a published wall
  And a tile on that wall binds an overlay whose label text is "OEE {{oeeline1}}"
  And the kiosk's live-update channel is connected
 When an operator sets "oeeline1" to "82.5" in fab "munich"
 Then a ResolvedOverlayTextChanged frame carrying "OEE 82.5" is delivered to
      that kiosk's hub connection
  And the tile's overlay label reads "OEE 82.5" without any reload
```

**Cross-fab conflict — the isolation the guard exists for**

```gherkin
Given fab "munich" and fab "dresden" each hold a variable named "oeeline1"
  And both fabs display walls bound to the same overlay definition
  And a screen from each fab holds a hub connection
 When an operator sets "oeeline1" to "82.5" in fab "munich"
 Then only the munich connection receives the frame
  And the dresden connection receives nothing within the silence window
  And the dresden tile's label is unchanged
```

**Bad request — a rejected write pushes nothing**

```gherkin
Given a variable "oeeline1" of type "Number"
 When an operator submits the value "not-a-number"
 Then the write is refused with 400
  And no ResolvedOverlayTextChanged frame is delivered to any connection
  And every tile bound to the overlay keeps its previous text
```

**Auth — the hub still refuses a caller without the scope**

```gherkin
Given a bearer token that carries neither "sse.layouts.read" nor "sse.management"
 When a client attempts to connect to /hubs/layouts
 Then the connection is refused
  And no frame of any kind reaches it
```

**Degenerate — a frame that genuinely has no fab is still dropped**

```gherkin
Given a ResolvedOverlayTextChangedV1 whose Metadata.Fab is null or blank
 When LayoutComposition handles it
 Then it is logged and dropped
  And it is not broadcast to any group or to all clients
```

That last one is a regression guard, not new behaviour. It already passes, and it
must keep passing: "broadcast it to everyone when the fab is missing" is the one
fix shape this spec forbids outright, because it puts one plant's production
figure on another plant's wall (ADR-0115, spec 017 FR-015).

### User Story 2 — the operator who retires a variable (P2)

An operator archives a variable. Overlays that reference it revert to showing the
placeholder literally (spec 005 FR-011), and tiles already on screen follow.

Same defect, same fix, second producer. P2 rather than P1 because it shares the
single line of cause with US1 and is proved at unit level only — see *Out of
scope* for why it gets no e2e.

#### Acceptance scenario

```gherkin
Given a kiosk in fab "munich" displays a tile bound to "OEE {{oeeline1}}"
  And the tile currently reads "OEE 82.5"
 When an operator archives "oeeline1" in fab "munich"
 Then the ResolvedOverlayTextChangedV1 published carries Metadata.Fab = "munich"
  And the munich hub group is addressed
```

---

## Independent end-to-end test procedure

Runnable by a person with no knowledge of the fix, distinguishing a working
system from today's.

1. Boot the full stack: `dotnet run --project src/AppHost` (run mode, so the
   web apps and gateway are up).
2. In management-web, register a camera, define a variable `demo1` with value
   `BEFORE`, publish an overlay whose text is `Line {{demo1}}`, and publish a
   one-tile layout binding that camera and that overlay.
3. Open kiosk-web, sign in, open the wall. The tile's label reads `Line BEFORE`.
   Confirm the `live-updates-degraded` badge is **hidden**.
4. **Leave the kiosk open.** In a second browser session, set `demo1` to `AFTER`.
5. **Today:** the kiosk label stays `Line BEFORE` indefinitely. Reloading the
   kiosk page shows `Line AFTER` — which is the tell.
6. **After the fix:** the kiosk label becomes `Line AFTER` within a second or so,
   with no reload.
7. Corroboration either way, without a browser: watch the LayoutComposition logs.
   Today, every change emits the `ResolvedOverlayTextChangedWithoutFab` message
   from `ResolvedOverlayTextChangedV1Handler`. After the fix that message stops
   and `BroadcastResolvedOverlayTextChanged` appears instead. **Those two log
   lines are the single clearest before/after signal in the system**, and they
   need neither a wall nor video.

---

## Requirements

- **FR-001** — `ResolvedOverlayTextChangedV1` published in response to a
  variable **value change** MUST carry `Metadata.Fab` equal to the fab of the
  variable that changed.
- **FR-002** — the same event published in response to a variable being
  **archived** MUST carry `Metadata.Fab` equal to that variable's fab.
- **FR-003** — the resulting SignalR frame MUST reach hub connections holding
  that fab, and MUST NOT reach connections that do not. (Existing behaviour of
  `SignalRLayoutLifecycleBroadcaster`; asserted here because until now nothing
  could reach it to check.)
- **FR-004** — a kiosk tile already on screen, bound to an affected overlay, MUST
  show the new resolved text without a reload, a reconnect, or a re-fetch
  triggered by anything other than the push.
- **FR-005** — the resolved-text hub hop MUST be covered by an automated test
  that asserts the **frame**, taken off a real hub connection against the real
  stack. Nothing weaker satisfies this: nine passing unit tests are what the
  defect hid behind.
- **FR-006** — the change MUST record a figure for the *event → overlay state*
  leg measured from the write returning to the frame arriving, both ends stamped
  in one process on one clock (constitution §IV; see *Latency* below).
- **FR-007** — a frame whose `Metadata.Fab` is null or blank MUST still be
  dropped, never broadcast to all clients.
- **FR-008** — the misleading cross-reference in
  `NFR_VariableResolutionLatencyTests`' docblock MUST name the test that actually
  covers the resolved-text hop.

### Out of scope, each with its reason

- **`SystemVariableArchivedV1`'s own null fab.** Found during this
  investigation, and confirmed in real audit rows (3 rows, 0 with a fab, against
  `SystemVariableDefinedV1`'s 3 of 3). It is a genuine defect — those audit rows
  become visible to every fab, because `SearchAuditQueryHandler` treats a null
  fab as unscoped — but it is a **different** defect with a different symptom, in
  audit rather than on a wall. ADR-0036: a fix changes the bug, not two bugs.
  **Recommendation: file it as its own issue.** Evidence is in this document so
  nobody has to find it twice.
- **Restoring the deleted `kiosk-label-follows-its-variable.spec.ts`.** The
  surviving span check asserts the same thing (a value set by an operator
  appearing on an already-open tile) five times over, in the file that owns the
  wall. Restoring the deleted one re-creates the exact cross-file collision
  `c8ed90c` removed.
- **Amending constitution §IV.** §IV currently records this path as *"now also
  suspected broken for an already-open tile"*. After this fix that sentence is
  stale, and a figure may move the *Measured* cell. **ADR-0144 forbids the lane
  amending the constitution.** The PR will state what §IV now says and what the
  run observed; a human makes the edit. Not a block — see plan.md declaration 2.
- **The §VII dashboard obligation.** Every leg's Dashboard cell is `no`. That is
  pre-existing debt this change neither creates nor discharges.
- **The per-overlay version counter being shared across fabs.**
  `NextVersionFor(overlayId)` is global per overlay, so a munich change and a
  dresden change increment one counter. Examined: because it is strictly
  increasing, every frame a given connection receives still has a higher version
  than the last one it received, so the kiosk's `version <= last` guard never
  drops a frame it should keep. No defect; recorded so the next reader does not
  re-derive it.
- **Any change to `useLabelDelay`, the hold, or the alignment loop.** Ruled out
  as cause; untouched.

---

## Locked technology choices

Nothing new is introduced. The change uses only what is already decided:

| Concern | Choice | ADR |
|---|---|---|
| Integration event envelope | `EventMetadata(EventIdentifier, OccurredAt, Fab, Actor)` | 0102 |
| Fab-scoped resolution | a variable resolves in the viewer's fab | 0115 |
| Messaging | RabbitMQ via Wolverine, Postgres outbox | 0010, 0042, 0088 |
| Real-time push | SignalR on `/hubs/layouts`, one group per fab | 0076, 0112 |
| Test framework | xUnit + Shouldly; integration via `AspireFixture`, no Testcontainers | 0052, 0103 |
| Test naming | sentence-style with underscores | 0053 |
| e2e | Playwright, in the blocking `e2e` job | 0108 |
| Red first | new behaviour observed failing, output quoted in the PR | 0139, 0140, 0144 |

---

## Latency budget impact (constitution §IV) — **not N/A**

**Leg: *event → overlay state*, budget ≤ 200 ms.** The path
`SystemVariableValueRequestedV1`/`PUT …/value` → SystemVariables resolution →
`ResolvedOverlayTextChangedV1` → LayoutComposition → `/hubs/layouts` → the tile
is that leg, plus a few milliseconds of *overlay composite + render* (≤ 50 ms) at
the end.

**What the fix does to the leg.** It adds no computation, no I/O and no hop to
the producer: one string already in memory is passed where a `null` literal is
today. What it changes is that the leg **completes**. Today the consumer returns
early and the leg terminates with no effect — the value reaches an already-open
tile *never*, not slowly. After the fix the consumer performs the SignalR group
send that spec 005 FR-013 designed for it.

**So it does owe a figure**, because work that was being skipped now runs. The
figure comes from T002's integration test: `PUT /system-variables/{name}/value`
returning → the `ResolvedOverlayTextChanged` frame arriving at a subscribed hub
client, both stamps taken by the test process on one clock (the safe shape spec
053 established; never a host stamp minus a container stamp). The comparable
existing figure is `OverlayPushIntegrationTests`, which asserts its own frame
arrives within one second on CI hardware.

**What that figure is not.** It is a server-side figure: it excludes the browser,
the React re-render, and the label hold. The e2e span check produces the
browser-inclusive number, and its own docblock already states that it covers two
legs of six and carries **~±1000 ms of instrument error** — five times the leg it
would characterise. Neither figure discharges the 800 ms SLO, and this spec does
not claim either does.

**§IV's Measured column is not changed by this work**, and the PR must say so.
§IV records *event → overlay state* as **"recorded, not yet readable"** and adds
that it "is now also suspected broken for an already-open tile". This spec
converts the suspicion into a located defect and a fix. Moving a cell in that
table is a constitution edit, which ADR-0144 reserves to a human. Spec 056's
record is the precedent and the warning: *"A cell that gains a measured because a
plan expected one is the same defect as a leg recorded unbuilt after it was
built."*

---

## Relationship to other issues

- **#1971 (blocked)** — *Three of the six declared system-variable types do not
  exist.* Its architect blocked it partly because building a fourth variable type
  before #2012 is fixed means building a type whose value provably cannot be
  observed on a wall. **This issue is upstream of it and this fix removes that
  objection.** It does not unblock #1971 by itself: that issue is blocked because
  its honest answer is an amendment to ADR-0000 decision 017, which the lane may
  not write. **Overlaps, does not block.**
- **#2014 (ready)** — *system-variables e2e specs fail locally on a cold stack.*
  Interaction, stated precisely below rather than waved at. **Neither blocks the
  other.**
- **#2004** — unrelated; only shares the SystemVariables test folder.

### How the red e2e interacts with #2014

#2014's failure is a *write* against a cold service exceeding the local 15 s
`expect` timeout, absorbed in CI by `retries: 2` and a 30 s timeout.

For this spec's e2e (the span check in `kiosk-shows-a-label-over-video.spec.ts`)
the interaction is **not flakiness, it is a polluted first figure**:

- The check never asserts on the write completing. It fills, clicks, and then
  waits on the *kiosk label* with a 60 s budget per iteration. A slow first write
  is absorbed by that budget.
- Spec 056's seed already performs the run's first SystemVariables write, with an
  explicit 90 s budget, before any spec runs. The service is warm by the time the
  span starts.
- What remains is that **iteration 0 may include warm-up** and therefore be an
  outlier. The existing report already prints **every figure individually**, not
  just a median, precisely so an outlier is visible rather than averaged away. No
  change is needed; the reader is told to look at the list.

**Assumption, marked:** that the seed's write is enough to warm the service for
the span. If iteration 0 comes back conspicuously larger than 1–4 in the phase-5
output, that is #2014's shape showing through, and the verification note says so
rather than quoting a median that hides it.

---

## Assumptions

1. **The kiosk's own token carries a fab.** `signInToKiosk` uses the seeded
   `operator` account and asserts a **populated** picker; layouts are fab-scoped
   and delivered by fab group, so an empty-fab token could not produce that
   state. Corroborated by `OverlayFrameFabScopingIntegrationTests`, which drives
   real per-fab hub connections successfully today.
2. **The overlay bound by spec 056's wall contains a `{{…}}` placeholder.** If it
   did not, `CellPage`'s `hasPlaceholder` gate would skip the snapshot query and
   the initial value could not render — but the existing green check asserts the
   label carries `variableInitialValue`, so it does.
3. **The frontend hop is correct.** Assumed, *not* asserted, and T004 exists to
   find out. See *What remains genuinely unknown*.

## Guesses marked

- **The order-of-magnitude bound for T002's timing assertion.** Modelled on
  `OverlayPushIntegrationTests`' one second and `NFR_VariableResolutionLatencyTests`'
  deliberate 4× loosening, both of which state that a tight bound on shared CI
  flakes and then gets deleted. The **figure printed is the artefact**; the
  assertion is an order-of-magnitude regression guard, and the spec says so
  rather than letting a later reader mistake the bound for the budget.
