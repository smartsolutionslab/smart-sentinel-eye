# Verification — Spec 206, a row no timeline can reach (#2429)

Phase 5 (ADR-0037), executed against a freshly booted Aspire AppHost from
this worktree (`2429-wrong-guid-audit-timeline` @ `99cbebf0`), following
`spec.md` §*Independent end-to-end test procedure* / `tasks.md` T010.
Not a test run for steps 1–7: the transcripts below are curl calls and
Aspire dashboard structured-log reads against the running system,
2026-09-21.

**Phase-6 addendum (same date):** code review found the "joins the
timeline" claim in §*`GET /audit?resourceKind=overlay&resourceIdentifier={O}`*
below was inferred by combining two different queries rather than
observed in one. Re-verified against a second fresh Aspire boot of this
worktree (after the phase-6 test-file fixes, `fba3b287`); the corrected
section documents an actual single-query, two-row response.

## Setup

- Stopped a stale, unrelated Aspire stack (containers named `*-e0a2470a`,
  left running from a different worktree/repo checkout) that was holding
  the host ports this worktree's stack needed — its `FailedToStart` state
  and `"Bind for 127.0.0.1:16304 failed: port is already allocated"` made
  the conflict explicit. Stopped, not removed, until step 8 needed the
  ports fully free, at which point containers were removed (never their
  named volumes — `docker volume ls` confirms both `...-97b5c395-*` and
  `...-e0a2470a-*` data volumes survived untouched).
- Booted `dotnet run --project src/AppHost` with
  `DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` and
  `Logging__LogLevel__SmartSentinelEye.AuditObservability.Application=Debug`
  (the `Audited` log line is emitted at `Debug`, below the appsettings'
  `Information` default — CLAUDE.md's own standing note on this).
- Minted a token from Aspire's **proxied** Keycloak endpoint
  (`https://localhost:16307`, not the container's mapped port) via the
  password grant, `client_id=management-web`, `admin`/`Admin1234`. Decoded:
  `"scope":"... sse.audit.read sse.overlays.write sse.variables.write
  sse.events.write sse.rules.write ..."`, `"groups":["/fabs/munich"]` —
  exactly the scope and fab the procedure calls for.

## Steps 3–4 — overlay, variable, and the before-count

Created and published an overlay via `overlay-designer`
(`POST /overlays`, `POST /overlays/{O}/revisions/1/publish`):

```
O = 01a0c3a0-13c0-78a7-b754-994d02fe3c9f
```

Defined the system variable `spec206Var` via `system-variables`
(`POST /system-variables`).

`GET /audit/overlay/{O}?fabId=munich` **before** ingesting anything:

```json
{"rows":[],"nextCursor":null}
```

**Zero, not one.** This is a real, honestly-observed deviation from
`spec.md`'s step 4 wording ("note the rows present — the publish row"),
and it is **not** one of the two defects this spec fixes. Confirmed by
reading the code and the row itself: `OverlayRevisionPublishedDomainEventHandler.cs:44`
constructs `EventMetadata` with `Fab: null` — OverlayDesigner does not
stamp a fab on this event at all — and `GetResourceTimelineQueryHandler`
filters on `auditEvent.Fab == fabFilter` unconditionally, so a null-fab
row can never satisfy any `fabId`-scoped query. The row's presence and
correct `(kind, identifier)` pivot were confirmed instead via the
cross-cutting, fab-optional search:

```
GET /audit?resourceKind=overlay&resourceIdentifier=01a0c3a0-13c0-78a7-b754-994d02fe3c9f
→ 200, one row: eventKind=OverlayRevisionPublishedV1, resourceKind=overlay,
  resourceIdentifier=01a0c3a0-13c0-78a7-b754-994d02fe3c9f, fab=null
```

`OverlayRevisionPublishedV1` is row #14 in `spec.md`'s audit table,
listed **correct** — this spec does not touch its mapping, and the row
above proves that mapping is unaffected. The null-fab gap is a separate,
pre-existing defect in OverlayDesigner's publish handler, out of this
spec's file-contention list, and is recorded here rather than silently
absorbed into the before/after comparison.

## Step 5 — driving both effects

Two Automation rules on the same trigger (`triggerSource=manual`,
`triggerKind=Spec206Event`, `predicate=$.payload.cycleTime <= 30`,
fab `munich`), following the shape of
`tests/Integration.Tests/Automation/EventReachesItsEffectsTests.cs`'s
`ActivateRuleAsync` / `ActivateHighlightRuleAsync`:

- `spec206-set-var` — `SetVariableValue`, variable `spec206Var`,
  `100 - $.payload.cycleTime * 2`.
- `spec206-highlight` — `HighlightOverlay`, overlay `O`, `durationMs=2500`.

Both published and read back `"state":"Active"`. Then:

```
POST /events/manual?fabId=munich
{"deviceId":"spec206-device","kind":"Spec206Event",
 "occurredAt":"2026-09-21T11:03:26Z","payload":{"cycleTime":10}}
→ 201, event identifier 01a0c3a2-925a-7367-9f7a-3a4381639135
```

`spec206Var` reached `80` on the first poll (`GET /system-variables/spec206Var`).

## Step 6 — observed, in order

### The two `Audited` log lines

Read via the Aspire dashboard's structured logs
(`mcp__aspire__list_structured_logs`, filtered by the overlay/variable
identifier — per the standing lesson, the event was created first and the
logs read after, not hunted from history):

```
Audited OverlayHighlightRequestedV1 01a0c3a2-9274-7e2e-a0b1-6231f8939100 (resource: overlay/01a0c3a0-13c0-78a7-b754-994d02fe3c9f).
```
```
Audited SystemVariableValueRequestedV1 01a0c3a2-9272-7394-bdad-248753e41a40 (resource: variable/spec206Var).
```

Both `severity: Debug`, `source:
SmartSentinelEye.AuditObservability.Application.EventHandlers.AuditingMessageHandler`.
**On `develop` today** these would read
`... (resource: layout/01a0c3a0-13c0-78a7-b754-994d02fe3c9f).` and
`... (resource: variable/<a guid>).` — this worktree's lines show
`overlay` and `spec206Var` respectively, the fix in effect. This is no
longer an inspection claim: commit `b0055288` (`test(audit): assert
which resource each integration event pivots on`) ran the 20-row
mapping table (US3) against production code that is **byte-identical**
to `origin/develop` for `src/` — `git diff --stat origin/develop
b0055288 -- src/` is empty — and observed exactly these two failures
(`OverlayHighlightRequestedV1` expected `overlay`, got `layout`;
`SystemVariableValueRequestedV1` expected the variable's `Name`, got
`CausingEventIdentifier`). That is an **executed red test against
today's code**, not an inspection, and it is the phase-4a evidence
quoted verbatim in the PR body.

### `GET /audit/overlay/{O}` — before vs. after (fab-scoped endpoint)

- **Before** (step 4): `{"rows":[],"nextCursor":null}` — 0 rows (see the
  null-fab caveat above; the publish row exists but is unreachable by any
  `fabId`-scoped query for a reason unrelated to this fix).
- **After**:

```json
{"rows":[{"auditIdentifier":"01a0c3a2-928d-7a11-8f9b-eade8245e227",
  "occurredAt":"2026-09-21T11:03:26.578352+00:00","fab":"munich",
  "eventKind":"OverlayHighlightRequestedV1","resourceKind":"overlay",
  "resourceIdentifier":"01a0c3a0-13c0-78a7-b754-994d02fe3c9f",
  ...}],"nextCursor":null}
```

**One more row than before (0 → 1)** on this endpoint — the highlight,
correctly pivoted to `overlay`/`O`. The publish row still does not
appear here, because `GET /audit/{resourceKind}/{resourceIdentifier}`
(`GetResourceTimelineQueryHandler.cs:52-54`) filters on `Fab ==
fabFilter` unconditionally, and `OverlayRevisionPublishedV1`'s row
carries a null `Fab` (the pre-existing, unrelated gap above). This
sub-section is retained as a secondary observation; it is **not** the
evidence that the two rows join one timeline — see below.

### `GET /audit?resourceKind=overlay&resourceIdentifier={O}` — both rows, one query, freshly observed

The section above combined two different queries to argue the highlight
"joins the same timeline" as the publish: the fab-scoped endpoint (which
the null-fab gap keeps to one row) and a separate, earlier call to the
cross-cutting search below (taken *before* driving the highlight, so it
only ever showed the publish row). Neither call by itself showed both
rows together. Redone here, from a fresh boot of this worktree, with a
newly created overlay so the response is not mixed with rows left over
from the earlier pass:

```
O = 01a0c3d3-e580-735e-a26e-66b3e4035477
```

`POST /overlay-designer/overlays` → `201`, then `POST
/overlay-designer/overlays/{O}/revisions/1/publish` with `If-Match:
"0"` → `200`. A dedicated Automation rule (`spec206verifynit6`,
`triggerKind=Spec206VerifyNit6`, `HighlightOverlay` on `O`,
`durationMs=2500`) was created and published, then fired:

```
POST /event-ingestion/events/manual?fabId=munich
{"deviceId":"spec206-nit6-device","kind":"Spec206VerifyNit6",
 "occurredAt":"2026-09-21T12:02:00Z","payload":{"cycleTime":9}}
→ 201, event identifier 01a0c3d7-fd92-7de1-b89e-889028dab0b4
```

Then, **one call**, after the highlight, nothing else in between:

```
GET /audit?resourceKind=overlay&resourceIdentifier=01a0c3d3-e580-735e-a26e-66b3e4035477
```

```json
{"rows":[
  {"auditIdentifier":"01a0c3d7-fda3-7118-9d7f-340bb14f4011",
   "occurredAt":"2026-09-21T12:01:47.419946+00:00",
   "receivedAt":"2026-09-21T12:01:47.427994+00:00","fab":"munich",
   "eventKind":"OverlayHighlightRequestedV1","resourceKind":"overlay",
   "resourceIdentifier":"01a0c3d3-e580-735e-a26e-66b3e4035477",
   "actorIdentifier":"00000000-0000-0000-0000-000000000000",
   "actorIsSystem":true,"actorUsername":null,
   "eventIdentifier":"01a0c3d7-fd9b-7479-82f2-b76811178d1f",
   "payload":"{\"Metadata\": {\"Fab\": \"munich\", ...},
     \"DurationMs\": 2500,
     \"OverlayIdentifier\": \"01a0c3d3-e580-735e-a26e-66b3e4035477\",
     \"CausingEventIdentifier\": \"01a0c3d7-fd92-7de1-b89e-889028dab0b4\"}",
   "payloadSizeBytes":392,"schemaVersion":1},
  {"auditIdentifier":"01a0c3d4-25da-7689-8dfd-3fdaa43a6a8e",
   "occurredAt":"2026-09-21T11:57:34.792688+00:00",
   "receivedAt":"2026-09-21T11:57:35.578897+00:00","fab":null,
   "eventKind":"OverlayRevisionPublishedV1","resourceKind":"overlay",
   "resourceIdentifier":"01a0c3d3-e580-735e-a26e-66b3e4035477",
   "actorIdentifier":"3bdc90ad-f223-4cbf-b48b-d5e9ec50806d",
   "actorIsSystem":false,"actorUsername":null,
   "eventIdentifier":"01a0c3d4-22d2-765c-a6a6-05e51399a002",
   "payload":"{\"Name\": \"spec206-verify-overlay\", \"Text\": \"cycle\",
     \"Overlay\": \"01a0c3d3-e580-735e-a26e-66b3e4035477\", ...}",
   "payloadSizeBytes":515,"schemaVersion":1}
],"nextCursor":null}
```

**Both rows, in one response, from one call.** This is the acceptance
scenario `spec.md` describes ("both rows are returned") observed
directly rather than inferred by combining two different queries taken
at two different times.

**One honest correction to `spec.md`'s Gherkin wording, found here
rather than glossed over:** the acceptance scenario says "ascending by
occurred_at" — true of `GET /audit/{resourceKind}/{resourceIdentifier}`
(`GetResourceTimelineQueryHandler.cs:75`, `OrderBy(OccurredAt)`), which
is the endpoint the Gherkin names. But that endpoint cannot show both
rows at all while `OverlayRevisionPublishedV1`'s null-`Fab` defect
stands (above), so the single query that actually joins them is the
cross-cutting search, `SearchAuditQueryHandler.cs:140`, which sorts
**`OrderByDescending(OccurredAt)`** — the response above shows the
highlight (12:01:47) before the publish (11:57:34), newest first, not
ascending. Both rows on one timeline is the thing #2429 is about; the
sort direction is a property of which endpoint answers the query, and
differs between the two by design (search is "what happened recently",
the resource timeline is "the story in order"). Recorded rather than
silently reported as "ascending" to match the Gherkin's wording, since
that would not be what was observed.

### `GET /audit/variable/spec206Var`

```json
{"rows":[{"auditIdentifier":"01a0c3a2-9452-7023-be02-b650ece9788d",
  "occurredAt":"2026-09-21T11:03:26.578352+00:00","fab":"munich",
  "eventKind":"SystemVariableValueRequestedV1","resourceKind":"variable",
  "resourceIdentifier":"spec206Var","payload":"{\"Name\": \"spec206Var\",
  \"Value\": \"80\", ...}", ...}],"nextCursor":null}
```

One row, correctly pivoted to `variable`/`spec206Var` (the business name,
per defect B's fix — not `CausingEventIdentifier`). On `develop` today
this query returns an empty page (the row would be written under
`variable`/`<the ingested event's guid>` instead).

### `GET /audit/layout/{O}` — empty, explicitly

```json
{"rows":[],"nextCursor":null}
```

**Empty, both before and after driving the highlight.** This is the half
of the fix a "the new query works" note would omit: the highlight row is
not merely *findable* under `overlay`/`O`, it is also **absent** from the
`layout` timeline it used to land on by mistake (defect A: the event was
published into the `LayoutComposition` namespace, so the unfixed
convention picker assigned kind `layout`). The row has genuinely left the
wrong timeline, not just gained a second (wrong) presence on it.

## Step 7 — the other eighteen mappings did not move

Registered a camera via `camera-catalog` (`POST /cameras`,
`CAM = 01a0c3a3-69dc-7ca7-a747-aec12880e11e`):

```
GET /audit/camera/01a0c3a3-69dc-7ca7-a747-aec12880e11e?fabId=munich
→ 200, one row: eventKind=CameraRegisteredV1, resourceKind=camera,
  resourceIdentifier=01a0c3a3-69dc-7ca7-a747-aec12880e11e, fab=munich
```

Camera's audit row is reachable exactly as before (and, unlike
OverlayDesigner, CameraCatalog *does* stamp `fab` — confirming the
null-fab gap above is specific to `OverlayRevisionPublishedV1`, not a
property of the timeline endpoint or of this fix). Nothing else moved.

## Step 8 — NFR001 / NFR002, twice each, Release

Both are `[Trait("Category","Measurement")]` (NFR001) / unfiltered
(NFR002) and excluded from CI's default run; invoked explicitly with
`dotnet test -c Release --no-build`, each run booting its own dedicated
Aspire test stack (ADR-0103 — the manual stack from steps 1–7 was
stopped first so the two boots would not contend for the same ports;
confirmed clean via `docker ps` / process list before each run).

### NFR001 — `Requirement_span_at_100_events_per_second_is_an_interval_not_a_verdict`

`Logging__LogLevel__Default=Warning` set for the run (the test itself
refuses to report a verdict at the inherited `Information` level — this
is not optional, it is the test's own guard).

**Run 1:**
```
rate achieved: 99.6 ev/s (target 100)
paced drive since boot: #1 (cold — the first of this boot)
typical: requirement span between 5.3 and 1252.8 ms (width 1247.4 ms)
tail (p99 band): requirement span between 2.7 and 2921.8 ms (width 2919.1 ms)
NFR-001 budget: 50 ms — falls inside the interval: this run cannot say
  whether NFR-001 was met or missed
```

**Run 2:**
```
rate achieved: 99.7 ev/s (target 100)
paced drive since boot: #1 (cold — the first of this boot)
typical: requirement span between 4.1 and 1579.3 ms (width 1575.2 ms)
tail (p99 band): width 3759.8 ms
NFR-001 budget: 50 ms — falls inside the interval, same inconclusive verdict
```

**Both runs land as this fact's drive #1 (cold)**, because each
`dotnet test` invocation is an independent fresh Aspire boot and nothing
else runs before it to warm the paced-drive path. The test class's own
doc comments record this exact effect measured elsewhere in this repo —
"the first paced drive of a boot slower than the second in 7 of 7
within-boot pairs, by 3.2x to 143x" — and a third attempt (the full test
class in one boot, so this fact ran as drive #1 and
`Where_the_ingest_span_goes` ran second as drive #2/warm in the *same*
boot) reproduced the pattern directly: drive #1 typical width 2114.5 ms,
drive #2 (same boot) typical **7.6 ms**. A fourth attempt (full class,
second boot) additionally hit the test's own clock-disagreement guard
(`result.Verdict.IsEstablished` failed: stamping clocks differed by
12.07 ms, over the 10 ms the write leg can absorb) — a measurement
apparatus refusal, not a code failure, and consistent with a cold,
just-booted container's clock not yet having settled.

**Both officially-quoted runs are inconclusive against the 50 ms budget**,
for a documented, reproducible reason (cold first paced drive) rather
than a regression — this spec's change runs in `AuditObservability`'s own
Wolverine subscriber, off the write path NFR001 measures, and neither
adds nor removes work on it (two `FrozenDictionary` entries, decided at
process startup). This repo's own standing lesson — "the first run after
machine churn looks exactly like a regression" — is exactly what these
two runs show, quoted honestly rather than smoothed over.

### NFR002 — `Search_over_a_24h_window_p99_stays_under_200ms`

**Run 1:**
```
GET /audit over a 24h window, 100000 seeded rows, 1000 requests:
p50 = 11.4 ms, p99 = 30.6 ms, max = 49.6 ms (budget 200 ms p99)
```

**Run 2:**
```
GET /audit over a 24h window, 100000 seeded rows, 1000 requests:
p50 = 9.6 ms, p99 = 30.5 ms, max = 51.6 ms (budget 200 ms p99)
```

Both comfortably inside the 200 ms budget (about 6.5×–6.6× margin at
p99), consistent between runs, and unaffected by the cold-boot clock
issue above (this path is a single client-side HTTP round trip, not a
cross-process clock attribution).

## Latency-budget statement (constitution §IV)

**N/A — no leg of constitution §IV is touched.** The changed code
(`V1ResourceMap.Conventions.cs`'s hand-tweak table) runs inside
`AuditObservability`'s own Wolverine subscriber, under per-module queue
isolation (ADR-0088), consuming `OverlayHighlightRequestedV1` and
`SystemVariableValueRequestedV1` as a fan-out audit sink. The kiosk-facing
SignalR push that *is* on the `Event → overlay state ≤ 200 ms` leg is
LayoutComposition's separate consumer of the same integration events and
is not modified, referenced, or reordered by this change. Observed above:
the highlight reached the audit trail correctly pivoted with no change to
`FabEventIngestedV1Handler`, the rule evaluator, or any hot-path
publisher — only where the audit write path files the resulting row.

## What was not covered

- **The null-`Fab` gap on `OverlayRevisionPublishedV1`** (and, by the same
  mechanism, any other OverlayDesigner-published audit row) is a real,
  separate defect found during this verification — not fixed here, not
  in `spec.md`'s file-contention list, and not part of #2429. Recorded so
  it is a known finding rather than a silent surprise for the next
  reader; worth its own follow-up issue.
- **`GET /audit/overlay/{O}` with an empty `fabId=`** 500s
  (`Ensure.That(...).IsNotNullOrWhiteSpace()` inside
  `DefaultFabAuthorizationGuard.EnsureAccessAsync` throwing
  uncaught) — noticed incidentally while probing the endpoint, unrelated
  to #2429, not chased further here.
- **Inter-display / production-scale latency** was not measured; both
  NFR figures are dev-box, Release, single-machine, matching how this
  repo's other NFR verifications are taken.
- **The frontend audit UI** (`AuditPage.tsx`) was not exercised — this
  spec touches no frontend file, and the HTTP API responses above are
  what it renders verbatim.
