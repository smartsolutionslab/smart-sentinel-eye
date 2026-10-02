# Phase 5 verification — T130, spec 296 / issue #2618, PR-A (US1, backend only)

Date: 2026-10-01/02. Branch `feat/2618-automation-switches-a-wall`, worktree
`D:\Github\sse-2618`.

## 0. What was run

Confirmed `docker ps` empty before starting (no containers, no other AppHost on
the machine). Booted fresh:

```
ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true ASPNETCORE_ENVIRONMENT=Development \
DOTNET_ENVIRONMENT=Development dotnet run --project src/AppHost --launch-profile https
```

All resources needed for this walk reached `Running`/`Healthy` (via
`mcp__aspire__list_resources`): `keycloak`, `automation`, `layout-composition`,
`event-ingestion`, `audit-observability`, `identity`, `camera-catalog`; `migrations`
reached `Finished`.

Auth: password grant against Keycloak's **Aspire-proxied** endpoint
(`https://localhost:11274/realms/smart-sentinel-eye/protocol/openid-connect/token`,
not the container's raw mapped port — the issuer in the minted token must match
the host the API validates against), client `management-web`, user `admin` /
`Admin1234`, scope `openid` — same recipe `AspireFixture.CreateAdminClientAsync`
uses in the integration tests. Service endpoints resolved from
`mcp__aspire__list_resources`: `automation` → `http://localhost:5143`,
`layout-composition` → `http://localhost:5245`, `event-ingestion` →
`http://localhost:5120`, `audit-observability` → `http://localhost:5010`,
`camera-catalog` → `http://localhost:5183`. All calls below are direct HTTP
against these service endpoints (same path `RuleSwitchesAWallIntegrationTests`
exercises; no gateway in front of the backend in this environment), bearer
token on every request.

**One real finding during arrangement, not a defect in the code under test:**
`Kind.From` (EventIngestion) requires the value to start with an uppercase
letter and contain only letters/digits — spec.md §4's independent test writes
`triggerKind: "line-stop"` as prose shorthand, which this grammar rejects
(`EVENT_INVALID_INPUT`, confirmed at 21:57:20). Switched to `"LineStop"`
throughout, matching `RuleSwitchesAWallIntegrationTests.UniqueKind()`'s own
`"LineStop{guid}"` convention. Not a spec defect worth filing — the independent
test's prose was never meant as a literal wire value — but recorded here so
the grammar mismatch isn't mistaken for a product bug on a future re-walk.

## 1. The rule round-trips

Registered a camera, created and published two layouts (A, B), created wall W
with scenes `[A, B]` (`POST /walls` on `layout-composition`):

```
GET /walls/01a0f977-f2cd-733d-acee-816face9b0a7
→ {"wall":"01a0f977-f2cd-733d-acee-816face9b0a7","version":0,"fab":"munich",
   "name":"T130-Wall","scenes":["01a0f977-bb9b-7c5b-bbe5-d853ec6791b3",
   "01a0f977-d921-7d51-9dcb-5971f861d24a"],
   "showing":"01a0f977-bb9b-7c5b-bbe5-d853ec6791b3","sceneVersion":0, ...}
```

(A = `01a0f977-bb9b-...`, B = `01a0f977-d921-...`. W shows A.)

```
POST /rules (automation)
{"name":"t130-linestop-fault","triggerSource":"manual","triggerKind":"LineStop",
 "predicate":"$.payload.line == 3","actionType":"SwitchWallScene",
 "variableName":null,"valueExpression":null,"overlayIdentifier":null,"durationMs":null,
 "wallIdentifier":"01a0f977-f2cd-733d-acee-816face9b0a7","sceneTarget":"Layout",
 "targetLayoutIdentifier":"01a0f977-d921-7d51-9dcb-5971f861d24a"}
→ HTTP 201, body "01a0f978-aafd-74e0-bb23-5649b334f1e9"

GET /rules/t130-linestop-fault
→ {"ruleIdentifier":"01a0f978-aafd-74e0-bb23-5649b334f1e9","version":0,"fab":"munich",
   "name":"t130-linestop-fault","triggerSource":"manual","triggerKind":"LineStop",
   "predicate":"$.payload.line == 3",
   "action":{"kind":"SwitchWallScene","variableName":null,"valueExpression":null,
             "overlay":null,"durationMs":null,
             "wall":"01a0f977-f2cd-733d-acee-816face9b0a7","sceneTarget":"Layout",
             "targetLayout":"01a0f977-d921-7d51-9dcb-5971f861d24a"},
   "state":"Draft", ...}
```

**The action round-trips exactly as sent** (wall, sceneTarget "Layout", targetLayout
B). Published:

```
POST /rules/t130-linestop-fault/publish, If-Match: "0"
→ HTTP 200; GET afterward shows "version":1,"state":"Active","publishedAt":"2026-10-01T21:57:15.262594+00:00"
```

(An earlier rule created with `triggerKind: "line-stop"` was archived rather than
reused once the grammar mismatch above was found — see §0.)

## 2. A matching event switches W (the kiosk itself not observed — see §7)

```
POST /events/manual (event-ingestion)
{"deviceId":"spec296-device","kind":"LineStop","occurredAt":"2026-10-01T21:57:20.000Z",
 "payload":{"line":3}}
→ HTTP 201, body "01a0f978-d653-7043-9be0-0acfad30c60e"
```

Polled `GET /walls/{W}` on `layout-composition`; within ~1-2s:

```
→ {"wall":"01a0f977-f2cd-733d-acee-816face9b0a7","version":1,"fab":"munich",
   "name":"T130-Wall","scenes":[...],
   "showing":"01a0f977-d921-7d51-9dcb-5971f861d24a","sceneVersion":1,
   "showingSince":"2026-10-01T21:57:21.6366+00:00"}
```

**`Showing` moved from A to B, `sceneVersion` 0 → 1**, matching spec.md US1
step 3 exactly (the `+1` and the layout identifier both confirmed).

## 3. The audit pair reads correctly

`GET /audit/wall/{W}?fabId=munich` on `audit-observability`
(**resource kind is the lowercase `"wall"`** per
`AuditObservability.Domain.AuditEvent.ResourceKind.Wall`, not the capitalised
`"Wall"` spec.md's prose independent test writes — `GET /audit/Wall/{W}` 400s
with `AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND`; recorded for the same reason as
§0's `Kind` grammar note). Three rows as of this point in the walk:

```
1. WallConfiguredV1   — Wall creation (not part of this scenario, context only)
2. WallSceneSwitchRequestedV1 —
   {"Rule":"01a0f978-aafd-74e0-bb23-5649b334f1e9",
    "Wall":"01a0f977-f2cd-733d-acee-816face9b0a7","Target":"Layout",
    "Metadata":{"Fab":"munich","Actor":null,...},
    "TargetLayout":"01a0f977-d921-7d51-9dcb-5971f861d24a",
    "CausingEventIdentifier":"01a0f978-d653-7043-9be0-0acfad30c60e"}
3. WallSceneChangedV1 —
   {"Rule":"01a0f978-aafd-74e0-bb23-5649b334f1e9",
    "Wall":"01a0f977-f2cd-733d-acee-816face9b0a7","Cause":"Rule",
    "Metadata":{"Fab":"munich","Actor":null,...},
    "SceneVersion":1,"CurrentLayout":"01a0f977-d921-7d51-9dcb-5971f861d24a",
    "PreviousLayout":"01a0f977-bb9b-7c5b-bbe5-d853ec6791b3",
    "CausingEventIdentifier":"01a0f978-d653-7043-9be0-0acfad30c60e"}
```

**The pair is correctly correlated**: both rows 2 and 3 carry the identical
`Rule` identifier and the identical `CausingEventIdentifier`
(`01a0f978-d653-...`), which is exactly the event identifier `POST
/events/manual` returned in step 2. `Metadata.Actor` is `null` on both, as
spec.md requires for a rule-driven switch (no human in the loop).

## 4. PD-1 observed

Wall was on B (`version` 1). Manual switch back to A via the
`SwitchWallSceneCommandHandler` endpoint:

```
POST /walls/{W}/switch, If-Match: "1"
{"target":"layout","layout":"01a0f977-bb9b-7c5b-bbe5-d853ec6791b3"}
→ HTTP 200, {"...,"version":2,...,"showing":"01a0f977-bb9b-...","sceneVersion":2,...}
```

W is now showing A again, `Version` moved to 2 by the manual write. Ingested
the **same matching event** again:

```
POST /events/manual {"kind":"LineStop",...,"payload":{"line":3}}
→ HTTP 201, body "01a0f979-b885-743a-8dd2-f1e9c0ecbbee"
```

Polled `GET /walls/{W}`:

```
→ {"...,"version":3,...,"showing":"01a0f977-d921-...","sceneVersion":3,...}
```

**The rule-driven switch applied again, moving W back to B, even though the
manual switch had moved `Version` to 2 in between.** This is first-hand
confirmation of PD-1/FR-006: the event-driven path does not consult an
expected version. The audit trail for this stretch (fetched afterward) makes
the whole sequence legible in one place:

```
t=21:58:12  WallSceneChangedV1  Cause:"Operator"  Rule:null  SceneVersion:2
            PreviousLayout:B CurrentLayout:A  CausingEventIdentifier:null
            Metadata.Actor:<admin's user id>        (the manual switch)
t=21:58:19  WallSceneSwitchRequestedV1  Rule:<rule>  Target:"Layout" TargetLayout:B
            CausingEventIdentifier:01a0f979-b885-743a-8dd2-f1e9c0ecbbee
t=21:58:19  WallSceneChangedV1  Cause:"Rule"  Rule:<rule>  SceneVersion:3
            PreviousLayout:A CurrentLayout:B
            CausingEventIdentifier:01a0f979-b885-743a-8dd2-f1e9c0ecbbee
            Metadata.Actor:null                      (the rule winning back over it)
```

The manual row's `Cause` is `"Operator"` (not `"Rule"`) and carries the actor's
identifier with a null `Rule`/`CausingEventIdentifier` — the two causes are
cleanly distinguishable in the same audit stream, which is what step 3 above
also needed to hold.

## 5. A non-matching payload changes nothing

```
POST /events/manual {"kind":"LineStop","payload":{"line":4}, occurredAt: 21:58:32Z}
→ HTTP 201, body "01a0f979-ed54-7171-acc8-6a980713d9da"
```

Waited 5s, then `GET /walls/{W}`:

```
→ {"...,"version":3,...,"showing":"01a0f977-d921-...","sceneVersion":3,
   "showingSince":"2026-10-01T21:58:19.044982+00:00", ...}
```

**Unchanged** — same `version`/`sceneVersion`/`showing`/`showingSince` as
immediately after step 4's rule-driven switch. The predicate `$.payload.line
== 3` correctly rejected `line: 4`; no new row appears for this event in the
audit timeline either (confirmed by re-fetching `GET
/audit/wall/{W}` and finding no 7th row).

## 6. One Aspire trace spans ingest → Automation → LayoutComposition → (hub)

`mcp__aspire__list_traces` without a trace id returns the newest N traces
across the whole stack, which — with six services each polling
`identity`'s `/registered-clients/revoked` on a 5s timer — is flooded within
seconds by that polling noise and never surfaces a single real request by
free-text search (`search` on `list_traces`/`list_trace_structured_logs` is
effectively unusable for this, matching this repo's own recorded lesson about
Aspire's trace search). Worked around by supplying an explicit W3C
`traceparent` request header on the ingest call, which ASP.NET Core's
OpenTelemetry instrumentation honours as the trace's root — making the
resulting trace id exactly the one supplied, and therefore directly
look-up-able.

Arranged W back on A (manual switch, `version` 3→4), then:

```
POST /events/manual
  traceparent: 00-4296180726184296180726184296aa01-aa01aa01aa01aa01-01
  {"kind":"LineStop","payload":{"line":3},...}
→ HTTP 201, body "01a0f97b-61b2-7c77-bdaf-b73eb70e3f9f"
```

**Trace id: `4296180726184296180726184296aa01`** (dashboard:
`https://localhost:17069/traces/detail/4296180726184296180726184296aa01`,
total duration 77ms). Spans observed in this one trace, in order, by
`source` resource and span `name`:

| source | kind | name | notes |
|---|---|---|---|
| event-ingestion | Server | `POST /events/manual` | the HTTP write, 201 |
| event-ingestion | Client | `postgresql` ×3 | dedup check, strict-mode check, event insert |
| event-ingestion | Producer | `send` | `FabEventIngestedV1` → RabbitMQ |
| automation | Consumer | `receive` | handler.type = `FabEventIngestedV1Handler` |
| automation | Producer | `send` | `WallSceneSwitchRequestedV1` → RabbitMQ |
| audit-observability | Consumer/Internal | `receive` / `FabEventIngestedV1` | handler.type = `IntegrationEventAuditHandler` |
| audit-observability | Consumer/Internal | `receive` / `WallSceneSwitchRequestedV1` | same handler, audits the request itself |
| **layout-composition** | Consumer | `receive` (`WallSceneSwitchRequestedV1`) | handler.type = **`WallSceneSwitchRequestedV1Handler`** — this is T122's handler |
| layout-composition | Client | `postgresql` | `SELECT ... FROM walls ...`, then `INSERT INTO wall_switch_request_receipts ... ON CONFLICT (rule_id, causing_event_id) DO NOTHING` (the dedup store, FR-012), then the layout-published check, then `UPDATE walls SET scene_version = ..., showing_layout_id = ..., version = ...` |
| layout-composition | Producer | `send` | `WallSceneChangedV1`, both local-in-process and to RabbitMQ |
| layout-composition | Consumer/Internal | `receive` (`WallSceneChangedV1`) | handler.type = **`WallSceneChangedV1Handler`** — this is the hub-relay handler (`src/LayoutComposition/Application/EventHandlers/WallSceneChangedV1Handler.cs`), which calls `ILayoutLifecycleBroadcaster.WallSceneChangedAsync` |
| audit-observability | Consumer/Internal | `receive` (`WallSceneChangedV1`) | same `IntegrationEventAuditHandler`, writes the third audit row |

**The structured log for this exact trace id confirms the semantic content**,
not just the span shape:

```
mcp__aspire__list_trace_structured_logs(traceId=4296180726184296180726184296aa01)
→ source=layout-composition.Application.EventHandlers.WallSceneSwitchRequestedV1Handler
  "Switched wall 01a0f977-f2cd-733d-acee-816face9b0a7 scene by rule
   01a0f978-aafd-74e0-bb23-5649b334f1e9 (event 01a0f97b-61b2-7c77-bdaf-b73eb70e3f9f)."
```

Final state check confirmed the write this trace describes actually landed:
`GET /walls/{W}` → `"version":5,"sceneVersion":5,"showing":<B>`.

**One honest gap in this trace, not a code defect**: no separate child span
named for the SignalR hub send itself. Reading
`WallSceneChangedV1Handler.cs` explains why — the broadcaster call
(`ILayoutLifecycleBroadcaster.WallSceneChangedAsync`) is not wrapped in its own
`Activity`, so the hub relay is visible as the `WallSceneChangedV1Handler`
consumer span completing, not as a fourth named leg. The brief anticipated
exactly this with "(SignalR hub, if instrumented)" — it is not instrumented as
a separate span today. The trace still demonstrably reaches the handler whose
only job past the `Wall` write is that relay.

## 7. What was NOT observed — the honest gap

**"The kiosk follows" (spec.md US1 step 3's other half) was not observed
through the kiosk UI, though no kiosk-web code needs to change for it.**
`apps/kiosk-web/src/features/wall/WallPage.tsx` already subscribes to
`WallSceneChanged` (since spec 258) and would render this switch with no
change on its side — the earlier statement here that PR-B "carries this
spec's kiosk wall-display work" was wrong, caught during PR-B's own
phase-1-3 re-verification (plan.md/tasks.md, `docs(296): re-verify PR-B
plan/tasks against shipped PR-A`). PR-B is the management-web rule editor;
it *observes* the kiosk following the switch, it builds nothing in
kiosk-web. There is simply no kiosk client open in this backend-only
session to observe it through. Substituted with the direct, first-hand
`GET /layout-composition/walls/{W}` observations in steps 2 and 4 above,
which show the same state a kiosk's `WallSceneChanged` hub frame would
drive it to render, plus the trace in step 6 reaching into
`WallSceneChangedV1Handler`, the handler that performs that hub broadcast.
This is a known, expected gap for this PR, not a failure — precedent: spec
263 `verification.md` §"What was NOT observed" recorded the same kind of
gap for its own live click-path walkthrough.

No other gap. Every other part of T130 — the round-trip, the switch, the
audit pair, PD-1, the non-matching drop, and the cross-service trace — was
observed first-hand against a freshly booted, single-use Aspire stack, not
inferred from reading the code or from a prior test run.

## Latency budget (constitution §IV)

**N/A**, matching spec 263's own note for the sibling feature: a rule-driven
scene switch is not on the event-to-overlay path (no overlay state, no
plant-floor label). No leg in the §IV table is spent by this change.

## Stack teardown

AppHost process stopped; `docker ps` confirmed empty afterward (no containers
left running).
