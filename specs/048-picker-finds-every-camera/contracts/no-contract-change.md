# No contract changes

**Recorded deliberately, because an empty `contracts/` folder reads as an
oversight and its absence reads as one too.**

Spec 048 changes no HTTP contract, no message contract, and no persisted shape.

## What stays exactly as it is

- **`GET /camera-catalog/cameras`** — unchanged. The picker's fix uses
  parameters the endpoint already accepts: `sort`, `order`, `offset`, `limit`.
  Nothing is added, nothing is deprecated, and no existing caller is affected.
- **`CameraListPage`** — unchanged. The response already carries `items`,
  `count`, `offset` and `limit`. The defect was a consumer discarding `count`,
  not a response that failed to provide it.
- **`listCameras`** in the shared client — left alone. `CamerasPage` uses it
  correctly for its own paging and must keep working; the new paging endpoint is
  added **alongside** it rather than replacing it.

## What would have been a contract change, and was deferred

**A name filter on the camera list.** That is the one thing this feature could
have needed from the server, and it is exactly why search was deferred to its
own spec: a new query parameter is a contract change, and it arrives with an
index consideration and a versioning question. Tracked as its own issue.

## The client-side shape

The new shared-client endpoint returns `items`, `count` and `complete`. That is
an internal shape between `apps/shared` and `apps/management-web`, not a
published contract — it is documented in [data-model.md](../data-model.md)
rather than here, because nothing outside this repository consumes it and
nothing needs versioning.
