# Quickstart: Read a single camera, and correct one

**Feature**: `029-camera-read-edit` · 2026-08-24

How to see this working end to end once implemented, and how to prove the three
things most likely to be wrong.

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
  -d grant_type=password -d client_id=management-web \
  -d username=op-3@munich.test -d password=Operator1234
```

`op-3@munich.test` is the Munich operator and `op-dresden@dresden.test` the
Dresden one. The names follow `op-N@fab` for some and `op-<fab>@fab` for
others — spec 028 lost a CI round to guessing `op-munich@munich.test`, which
does not exist.

---

## 1. Read one camera (US1)

```sh
# Register one
curl -s -X POST "http://localhost:<camera-catalog>/cameras" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"name":"line-3-inlet","rtspUrl":"rtsp://camera-sim:8554/probe"}'
# → 201, returns the camera's Guid

# Read it back
curl -si "http://localhost:<camera-catalog>/cameras/<guid>" -H "Authorization: Bearer $TOK"
# → 200, and note the ETag header
```

**Check the `ETag` and the body's `version` agree.** The edit needs the version,
and this is the only way a caller can obtain it — nothing exposed a camera's
version before this feature.

## 2. Correct the address (US2)

```sh
curl -si -X PATCH "http://localhost:<camera-catalog>/cameras/<guid>" \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -H 'If-Match: "1"' \
  -d '{"rtspUrl":"rtsp://camera-sim:8554/replacement"}'
# → 204
```

**Then send the same request again with the same `If-Match: "1"`.** It must
return **412**, not 204 — the version moved. And send one with no `If-Match` at
all: **428**, never a silent success.

## 3. Prove the refusals are indistinguishable (FR-006 / SC-003)

The security property, and the one most likely to regress.

```sh
# As the DRESDEN operator, against a MUNICH camera:
curl -si "http://localhost:<camera-catalog>/cameras/<munich-guid>" -H "Authorization: Bearer $DRESDEN_TOK" > /tmp/cross.txt

# As the same operator, against an identifier that never existed:
curl -si "http://localhost:<camera-catalog>/cameras/$(uuidgen)" -H "Authorization: Bearer $DRESDEN_TOK" > /tmp/unknown.txt

diff /tmp/cross.txt /tmp/unknown.txt
# must differ ONLY in the ETag/Date/trace headers — never in status or body
```

**Repeat every one of these against `PATCH`**, including with **no `If-Match`**.
That last one is the sharp case: a `428 IF_MATCH_REQUIRED` for another fab's
camera confirms the camera exists, which is exactly the enumeration FR-006
forbids. It must be **404**.

## 4. Prove the stream follows the address (research finding 2)

**The check that decides whether this feature shipped a lie.** Not currently a
spec requirement — see [research.md](./research.md) §2.

```sh
# Before the change, MediaMTX pulls the original source:
curl -s "http://localhost:<mediamtx-api>/v3/config/paths/get/cam-<guid>" | grep source

# Change the address (step 2), then:
curl -s "http://localhost:<mediamtx-api>/v3/config/paths/get/cam-<guid>" | grep source
# must show the NEW source
```

If it still shows the old one, the catalogue and the SFU disagree: the API
reports the new address while the stream keeps pulling the old. That is
invisible until someone watches the wrong feed, which is what makes it worth a
deliberate check rather than trust.

**The path name must not change** — it derives from the camera identifier, which
is immutable, so any kiosk already watching keeps working.

## 5. Retired cameras: readable, not editable

```sh
curl -s -X POST "http://localhost:<camera-catalog>/cameras/<guid>/retire" -H "Authorization: Bearer $TOK"

curl -si "http://localhost:<camera-catalog>/cameras/<guid>" -H "Authorization: Bearer $TOK"
# → 200, status "Decommissioned"  (FR-002)

curl -si -X PATCH "http://localhost:<camera-catalog>/cameras/<guid>" \
  -H "Authorization: Bearer $TOK" -H 'If-Match: "<current>"' \
  -H "Content-Type: application/json" -d '{"rtspUrl":"rtsp://camera-sim:8554/nope"}'
# → 409 CAMERA_RETIRED  (FR-005)
```

The 409 must come from the **aggregate**, not the handler. Assert it in a domain
test too: a handler-only guard is bypassable by the next caller, and a rule
enforced in one layer but not another is exactly the defect spec 028 shipped
and had to fix.

## 6. Audit (FR-011)

```sql
SELECT actor_identifier, payload->>'PreviousUrl', payload->>'Url'
FROM audit_events
WHERE event_kind = 'CameraAddressChangedV1'
  AND payload->>'Camera' = '<guid>';
```

One row per real change, naming the operator — **not** the system actor. And
submitting the *same* address again must add **no** row: idempotency as no
event, not merely no error.

---

## Verification checklist

| | |
|---|---|
| Read returns the camera; `ETag` and body `version` agree | FR-001 |
| A retired camera is readable, with its status | FR-002 |
| Address corrected with a valid `If-Match` | FR-003 |
| Stale `If-Match` → 412; absent → 428 | FR-004 |
| A retired camera cannot be edited, refused by the aggregate | FR-005 |
| Another fab's camera refused **byte-identically** to a non-existent one, on **both** endpoints | FR-006 / SC-003 |
| No `If-Match` on another fab's camera → **404**, not 428 | FR-006 / FR-007 |
| Fab, identifier, registration record unchanged and unchangeable | FR-008 / FR-009 |
| A rejected change leaves the camera untouched | FR-010 |
| The change is audited, naming the operator; a no-op adds no row | FR-011 |
| **The SFU pulls the new address; the path name is unchanged** | research §2 |
| `GET /cameras` rows each carry a version | contract |
