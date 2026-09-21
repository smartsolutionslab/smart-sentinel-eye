# Verification — Spec 209 (#2506)

Phase 5 (ADR-0037), executed against a freshly booted Aspire AppHost from
worktree `D:/Github/sse-2506`, branch `2506-overlay-published-null-fab`,
HEAD `0139e0aa`, 2026-09-21/22. Not a test run for these steps: the
transcripts below are curl calls against the live, running system.

## Setup

- AppHost booted (`dotnet run --project src/AppHost`), gateway resource
  `api-gateway` at `http://localhost:55394`, Keycloak proxied at
  `https://localhost:16307`.
- **PID freshness check**: the AppHost process (PID `24924`) started at
  `2026-09-22 00:49:37`, after this branch's fix commit `0139e0aa`
  (`2026-09-22 00:46:32`) — confirmed serving current code, not a stale
  binary from an earlier boot (this repo's own standing lesson).
- Minted a token via the password grant against `management-web`,
  `admin`/`Admin1234` (member of fab `munich`, not `berlin`).

## Step 3 — publish an overlay revision

```
POST /overlay-designer/overlays
{"name":"spec209-verify-overlay","label":{"text":"cycle","normalizedX":0.1,
 "normalizedY":0.1,"normalizedWidth":0.2,"normalizedHeight":0.1,"fontSizePx":16}}
→ 201, O = 01a0c63e-6917-7565-81c2-4ae02d270101

POST /overlay-designer/overlays/{O}/revisions/1/publish
If-Match: "0"
→ 200, 1
```

## Step 4 — the control: the row exists, with `fab: null`

```
GET /audit-observability/audit?resourceKind=overlay&resourceIdentifier={O}
→ 200
{"rows":[{
  "eventKind":"OverlayRevisionPublishedV1","resourceKind":"overlay",
  "resourceIdentifier":"01a0c63e-6917-7565-81c2-4ae02d270101",
  "fab":null,
  "payload":"{...\"Metadata\": {\"Fab\": null, ...}...}"
}],"nextCursor":null}
```

**Confirmed**: the row exists, is correctly pivoted on `overlay`/`{O}`, and
genuinely carries `fab: null` — ADR-0115's fab-neutral design, not a missing
stamp. This is the precondition without which an empty result from step 5
would be indistinguishable from "the overlay never published."

## Step 5 — the contrast: the fixed fab-scoped timeline

```
GET /audit-observability/audit/overlay/{O}?fabId=munich
→ 200
{"rows":[{
  "eventKind":"OverlayRevisionPublishedV1","resourceKind":"overlay",
  "resourceIdentifier":"01a0c63e-6917-7565-81c2-4ae02d270101",
  "fab":null,
  ...
}],"nextCursor":null}
```

**One row returned — the fab-neutral row, through the endpoint that
previously excluded it unconditionally.** This is the "after" half of the
contrast. The "before" half is T001/T002's own captured red evidence
(commit `940996e5`): the unit test's `result.Value.Rows.Count` was `0`
where `1` was expected, and the integration test's live run against
unpatched code returned an empty page for the identical shape of request.
Both are quoted verbatim in the PR body; not re-derived here by reverting
the fix, since the captured red already is that evidence and this
worktree's HEAD is the fixed state.

## Step 6 — the boundary this fix does not touch

```
GET /audit-observability/audit/overlay/{O}?fabId=berlin
→ 403
{"title":"RESOURCE_FAB_NOT_AUTHORIZED","status":403,
 "detail":"Caller is not a member of fab 'berlin'."}
```

```
GET /audit-observability/audit/overlay/{O}
→ 400
{"title":"REQUEST_INVALID","status":400,
 "detail":"Required parameter \"string fabId\" was not provided from query string."}
```

**Both unchanged from before this fix.** The widened predicate changes only
*what an already-authorized caller's query returns*, never *who is
authorized* or *whether `fabId` is required*. `admin@munich.test` (a member
of `munich`, not `berlin`) is still refused a `berlin`-scoped read with
`403`, and the endpoint still requires `fabId` at all with `400`. This is
the evidence the security-review focus in `plan.md`/`tasks.md` asked for:
the widening did not touch authorization.

## Latency-budget impact

**N/A.** Per `spec.md` §*Latency-budget impact*: the audit read path is on
no leg of constitution §IV. No figure is cited — a leg recorded as measured
before anyone read its figure claims a discharge nobody earned.

## Stack

AppHost (PID `24924`) stopped; all ten containers from this boot
(`mosquitto`, `keycloak`, `mediamtx`, `pgadmin`, `fixture-video`,
`postgres`, `minio`, `camera-sim`, `rabbitmq`, and the DCP tunnel-proxy)
confirmed stopped. `docker ps` returns empty.
