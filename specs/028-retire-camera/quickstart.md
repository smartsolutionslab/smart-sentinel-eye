# Quickstart: Retire a camera

**Feature**: `028-retire-camera` · 2026-08-23

How to see this working end to end once it is implemented, and how to prove the
two things most likely to be wrong.

---

## Boot

```sh
dotnet run --project src/AppHost
```

Wait for services to be Running. Dashboard: `https://localhost:17069`.

Ports are reassigned every run — read the current ones from the AppHost's
resource list rather than reusing yesterday's.

## Get a token

Mint against the endpoint the **services** resolve, not the container's own
mapped port, or every call returns 401 with `The issuer '…' is invalid`:

```sh
curl -sk -X POST "https://localhost:<keycloak-proxied>/realms/smart-sentinel-eye/protocol/openid-connect/token" \
  -d grant_type=client_credentials \
  -d client_id=scenario-simulator \
  -d client_secret=dev-only-scenario-simulator-secret
```

---

## 1. Retire a camera (US1)

```sh
# Register one
curl -s -X POST "http://localhost:<camera-catalog>/cameras" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"name":"line-3-inlet","rtspUrl":"rtsp://camera-sim:8554/probe"}'
# → 201, returns the camera's Guid

# Retire it
curl -s -X POST "http://localhost:<camera-catalog>/cameras/<guid>/retire" \
  -H "Authorization: Bearer $TOK" -w "\nHTTP %{http_code}\n"
# → 204
```

**Then retire it again.** It must return **204**, not 409 — and the audit trail
must show **one** retirement, not two (FR-005). A second `CameraRetiredV1` is
the most likely defect here, and returning 204 hides it.

## 2. Reuse the name (US2)

```sh
curl -s -X POST "http://localhost:<camera-catalog>/cameras" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"name":"line-3-inlet","rtspUrl":"rtsp://camera-sim:8554/replacement"}'
# → 201
```

Registering that name **before** retiring must still return 409. If both
succeed, the partial index lost its filter.

## 3. Watch the journey

Retirement crosses two contexts, so it is one trace across both. In the
dashboard, find the `POST /cameras/{camera}/retire` Server span; beneath it:

```
camera-catalog       Server    POST /cameras/{camera}/retire
  ├─ Producer  send     CameraRetiredV1
  ├─ Consumer  receive  CameraRetiredV1   (audit-observability)
  └─ Consumer  receive  CameraRetiredV1   (stream-distribution)
       └─ Client  DELETE → mediamtx   /v3/config/paths/delete/cam-<guid>
```

**Stop the scenario simulator first.** It publishes plant-floor events about
twice a second and the trace listing returns only the newest handful — the
retirement trace ages out of reach within seconds otherwise.

## 4. Prove the noise is gone (the point of research §4)

After retiring, watch `stream-distribution` for a minute or two.

**There must be no further health-change announcements for that camera, ever.**
Since #1801 was fixed the watcher announces every health change rather than one
per sweep, so a retired camera left in the sweep becomes a permanent generator
of announcements and audit rows for hardware that does not exist.

Check directly:

```sql
SELECT count(*) FROM audit_events
WHERE event_kind = 'StreamHealthChangedV1'
  AND payload->>'Camera' = '<guid>'
  AND occurred_at > '<retirement time>';
-- must stay 0
```

A count that keeps climbing means the watcher is still sweeping retired streams.
That is the failure this feature is most likely to ship, because everything else
will look correct.

---

## Verification checklist

| | |
|---|---|
| Retire returns 204; second retire also 204, one audit row | FR-005 |
| Another fab's camera returns **404**, not 403 | FR-004 |
| Name reusable in same fab after retiring; still 409 before | FR-006 |
| Name reuse does not leak across fabs | FR-006 |
| Retired camera absent from `GET /cameras`; present with `includeRetired=true` | FR-007 |
| SFU path removed; stream in terminal state; row still present | FR-008 |
| Retirement succeeds even with the SFU unreachable | FR-008a |
| No health announcements for the retired camera afterwards | research §4 |
