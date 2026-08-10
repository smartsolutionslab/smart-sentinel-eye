# Quickstart: Fab-scope stream distribution

**Feature**: `016-stream-fab-scoping`

"Done" is the observations, not the walk. Record them on the PR.

## 1. Attribution, against streams that have no fab

This is the step that cannot be faked. Unlike specs 013–015 there is **no SQL
backfill to watch** — the migration adds a nullable column and nothing else, so
the only way to see FR-008 and FR-010 work is to give the attribution service
something to attribute.

A fresh database has no streams, so it proves nothing.

```sh
# Stack up. Register cameras in two fabs so streams get provisioned.
#   as op-multi:  POST /cameras?fabId=munich   -> stream provisioned
#                 POST /cameras?fabId=dresden  -> stream provisioned
#
# Then blank the fabs to recreate the pre-feature state:
UPDATE streams SET fab = NULL;
```

Restart `stream-distribution` and read its log:

```
info: ...StreamFabAttributionService
      Attributed N stream(s) to a fab; M could not be resolved.
```

**Record both numbers.** M > 0 with no explanation is the interesting case —
it means a stream exists whose camera CameraCatalog no longer knows, which
FR-010 says must stay unattributed rather than be defaulted.

Then confirm the derivation is real, not a guess:

```sql
SELECT fab, count(*) FROM streams GROUP BY fab;
-- expect munich and dresden, matching the cameras — NOT everything in munich
```

That last point is the whole difference from specs 013–015. If every stream
lands in munich, the derivation silently fell back to a default and the feature
has not worked.

## 2. The window is real, and fails closed

Between the migration and the first attribution pass, every stream has a null
fab. Confirm it is invisible rather than visible-to-all:

```sh
UPDATE streams SET fab = NULL;   # do not restart
```

`GET /streams` as any operator → **the stream is absent**. Not listed for
munich, not listed for dresden, not listed for a multi-fab operator.

If it appears for anyone, FR-009 is broken and the transitional state is
leaking another plant's video path.

## 3. The scoped reads

| As | Do | Expect |
|---|---|---|
| `op-dresden@dresden.test` | `GET /streams` | only dresden's streams |
| `op-dresden` | `GET /streams/{a munich camera}` | **404**, byte-identical to a camera with no stream |
| `op-multi@smart-sentinel-eye.test` | `GET /streams` | both fabs' streams |
| `op-multi` | `GET /streams?fabId=dresden` | narrowed to dresden |
| `op-dresden` | `GET /streams?fabId=munich` | **403** |

Compare the two 404s **field by field**, with `traceId` removed. A difference
in title or type lets an operator confirm another plant's camera is streaming.

## 4. The callback is untouched

`POST /streams/authorize` must behave exactly as before — MediaMTX is not an
operator and holds no fab. Play a stream through the SFU and confirm it still
authorises.

This is the one latency-sensitive route here, and this feature does not touch
it. If playback breaks, something was scoped that should not have been.

## 5. A new camera lands in its own fab

Register a camera as `op-dresden`, then read its stream back as the same
operator: the stream is present and its fab is **dresden**. Read it as a
munich-only operator: **404**.

That is FR-002 end to end — the fab derived from the camera with nobody having
been asked.
