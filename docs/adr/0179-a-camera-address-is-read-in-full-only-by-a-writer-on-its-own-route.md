# ADR-0179: A camera's address is read in full only by a writer, on its own route

**Status:** **Proposed** (the owner ruled on 2026-10-17 and 2026-10-18 on #3016, and on 2026-10-22 answered the details those rulings left open; this records all three)
**Date:** 2026-10-21
**Amends:** none
**Relates to:** ADR-0070, ADR-0113, ADR-0114, ADR-0142, ADR-0144, spec 373, spec 377, spec 389, spec 404, issues 2990, 2999, 3016

## Context

`GET /cameras` and `GET /cameras/{camera}` return a camera's stored RTSP address verbatim
(`GetCameraQueryHandler.cs:56`, `ListCamerasQueryHandler.cs:72`, verified on `develop` at
`6206ecf9`). Rows stored before #2999 can carry a credential in a misread form
(`rtsp://admin:12345#@cam/s`); #2999 refuses that shape on write but keeps stored rows readable
through `RtspUrl.FromStored`. A vendor URL can also carry a secret in its path or query.

**Who receives it is wider than the console.** The route needs only `sse.cameras.read`, and that
scope is a default scope of `management-web`, `kiosk-web`, `kiosk-wall`, `integration-test-admin`
and `scenario-simulator` in the realm file, and part of the runtime `Kiosk` and `Device` bundles
(`KeycloakScopeBundles.cs:59-76`). So every kiosk, wall display and enrolled device in a fab can read
every camera's password. None of them needs the address: stream provisioning takes it from
`CameraRegisteredV1`, and the kiosk never reads the catalogue's URL field (spec 404 §1).

The owner decided **mask on read** on 2026-10-17: both DTOs return `UrlRedaction.ToOrigin` of the stored
address (`scheme://host[:port]`, or `<unparseable url>`); no stored-data rewrite (#2990); `FromStored`
unchanged; the integration-event payloads out of scope.

Spec 404 (branch `fix/3016-mask-camera-url-on-read`, `4f0a284b`) then found the consequence the decision
did not mention: the address-correction dialog pre-fills its field from `rtspUrl`
(`CameraDetailPage.tsx:190`, `EditCameraAddressDialog.tsx:54,67`). Once the read is masked, an untouched
Save sends the origin back with a current `If-Match`, `Camera.ChangeAddress` sees a different value, and
the stored `rtsp://10.0.5.98/stream` becomes `rtsp://10.0.5.98`. The path is lost and the stream is
repointed at it. Today that Save is a harmless no-op.

On 2026-10-18 the owner chose **option C** of spec 404's D-1: a write-scoped, separately gated read that
returns the full address for editing. That is new API surface and an authorization decision, which the
autonomous lane may not make (ADR-0144), so #3016 was blocked on this ADR.

## Decision

**1. The masked read is the default, and stays the only read anyone else gets.** `CameraDto.RtspUrl`
and `CameraSummaryDto.RtspUrl` carry `UrlRedaction.ToOrigin(stored)`, as decided on 2026-10-17. Field
names, JSON types and the route table entries for the two existing reads do not change. The management
UI renders what the API returns and never reconstructs an address.

**2. A new route returns the full address, for editing only:**

```
GET /cameras/{camera:guid}/address[?fabId=...]
200 { "rtspUrl": "<stored address, verbatim>", "version": <n> }
    ETag: <version>   Cache-Control: no-store
```

- **One camera per request.** No list form, no search, no export; a bulk read of every credential in a
  fab is exactly what this decision exists to prevent.
- It is a **read**: `GET`, no body, no side effect on the camera, no `If-Match`, no `Idempotency-Key`
  (ADR-0142 covers creates and rotations only).
- The body carries the version and the response the ETag, so the dialog can pre-fill and quote the
  version it pre-filled from. A `PATCH` with that version still meets ADR-0113's Layer 1 unchanged.
- `Cache-Control: no-store`, because the body may be a credential and the gateway or a browser must not
  keep it.
- **Decided 2026-10-22:** the route is `GET /cameras/{camera}/address`, answered with
  `Cache-Control: no-store`, and the masked read of point 1 stays the default every other read gets.

**3. Scope: `sse.cameras.write`, reused (decided 2026-10-22).** The route joins the existing `writes`
group (`CameraEndpoints.cs:28-30`), which already requires it. No new scope is added to the catalogue.

- This separates exactly the populations that should be separated: kiosks, wall displays and devices
  hold `sse.cameras.read` only and **stay refused** (403).
- **What it does not separate, stated plainly:** `sse.cameras.write` is a *default* client scope of
  `management-web`, so **every console user in a fab can read the full address**, not only those who
  edit cameras. The realm has no per-role scope mapping today. That is the same population that can
  already overwrite the address, so the route discloses nothing to anyone who could not already replace
  it; but it is wider than "people who edit cameras".
- A dedicated scope (e.g. `sse.cameras.address.read`) was considered and not chosen: it would land on
  `management-web` as a default scope too and separate nobody until the realm gains role-mapped scopes.

**4. Fab scoping: the write resolution, not the read one.** The route resolves its fab with
`ResolveWriteFabAsync` (`CameraEndpoints.cs:384`), the single-fab resolution the camera writes already
use: a multi-fab caller names the fab, and a camera outside it answers **404**, never 403, so the route
cannot be used to learn whether a camera exists in a fab the caller does not hold. Fab first, before the
camera is looked at, as `Get` already does (`CameraEndpoints.Reads.cs`).

**5. Every successful read is audited by an integration event, without the address (decided
2026-10-22).** A `200` publishes a new integration event, `CameraAddressReadV1`, through CameraCatalog's
existing outbox. Per ADR-0073 and the shape of its siblings it lives at
`src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs` and carries primitives at the wire boundary
(ADR-0040): **who** read (`Guid ReadBy`), **which camera** (`Guid Camera`, with its `string Fab`), **when**
(`DateTimeOffset ReadAt`), and the usual `EventMetadata`. It starts at `V1`; any later member follows
ADR-0073's rules like every other contract.

- **The event never carries the URL**, origin included. It therefore needs no entry in
  `AuditPayloadRedaction` and cannot leak one.
- **The audit handler** is AuditObservability's existing `IntegrationEventAuditHandler`
  (`src/AuditObservability/Application/EventHandlers/IntegrationEventAuditHandler.cs`), which gains one
  `Handle(CameraAddressReadV1 ...)` arm delegating to `AuditAsync`, exactly as `CameraAddressChangedV1`'s
  does. No new handler type.
- **Audit rows never contain the address**: the row's payload is the event, and the event has no field
  that could hold it.
- A refused read (401/403/404) is not an audit event; it is the gateway's and the log's business, as for
  every other route.

An audit event was chosen over a structured log line: a log is cheaper but is not the audit trail an
investigator queries, and this read is the one place the API discloses a credential.

**6. The edit dialog pre-fills from the new route, never from the masked DTO.** When "Correct the
address" opens, the dialog fetches the full address and its version, pre-fills from it, and quotes that
version in `If-Match`. If the fetch fails or is refused, the dialog does not open a pre-filled form; it
shows the refusal and offers nothing to save. A masked value can therefore never be written back.

**7. Unchanged:** `POST`/`PATCH` semantics and refusals; `RtspUrl`, `FromStored`, `UrlRedaction`;
stored rows (no rewrite; #2990); `CameraRegisteredV1`/`CameraAddressChangedV1` payloads; the route group's
existing scopes. Latency budget: **N/A**, management reads off every §IV leg.

## Consequences

**Positive: a read scope stops disclosing credentials.** Every kiosk, wall display and device loses
access to every camera's password, and the console shows the origin by default.

**Positive: the correction round trip stays safe and convenient.** The operator edits the stored
address, not its origin, so correcting one character does not require retyping a path they cannot see.
Spec 404's option A (empty field) would have made every correction a full retype.

**Negative: writers still see credentials, now on purpose.** Option C re-exposes the stored credential
to every console user in the fab, by design and audited. That is narrower than today, not closed.

**Negative: new API surface to keep.** One route, one DTO, one integration event and its audit arm; the
route-table characterisation baseline and `EndpointScopeDeclarationTests` both change.

**Negative: the console loses which stream a camera points at.** With path and query hidden in lists and
details, cameras behind one NVR (`/ch1` ... `/ch16`) render the same address (spec 404 §6); only the
name and the edit dialog distinguish them. A non-secret path display would need its own ruling on what in
a path is secret.

## Alternatives Considered

**A: empty field, current origin shown as text.** Within the 2026-10-17 decision and no new API, but
every correction becomes a full retype of an address the operator cannot see. Not chosen by the owner.

**B: refuse a `PATCH` equal to `ToOrigin(stored)`.** A heuristic, refuses a legitimate "drop the path"
correction, and adds a refusal code nobody decided. Rejected in spec 404.

**D: serve origin plus path, minus credentials.** Contradicts the owner's "no path/query", and a path can
itself be the secret.

**Serve the full address on the existing `GET /cameras/{camera}` to writers only.** One route whose
response shape depends on the caller's scopes is harder to reason about, harder to audit selectively,
and puts the credential in a response that list-oriented UI code already caches. Rejected.

**A new scope (`sse.cameras.address.read`).** See Decision 3: it separates nobody under today's realm.
Worth revisiting when scopes become role-mapped.

## Implementation Notes

What the autonomous lane builds after approval (#3016 re-enrolled; behaviour-changing, phase 4a **red**;
phase 6 runs `security-reviewer`). Spec 404 is reworked from option A to C at phase 1-3.

1. **Mask on read** (spec 404 FR-001..FR-005, unchanged): `UrlRedaction.ToOrigin` in
   `GetCameraQueryHandler.cs:56` and `ListCamerasQueryHandler.cs:72`; doc comments on both DTOs and on
   `CameraSummary`/`CameraDetail` in `apps/shared/src/api/cameras.api.ts`.
2. **New read**: `GetCameraAddressQuery` + handler in `CameraCatalog/Application/Queries` (paired
   `*Errors.cs`), a `CameraAddressDto(string RtspUrl, int Version)` (matching `CameraDto.Version`),
   route in the `writes` group with `ResolveWriteFabAsync`, ETag and `Cache-Control: no-store`,
   `.Produces`/`.ProducesProblem` for 200/400/401/403/404.
3. **Audit**: `src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs`
   (`Guid Camera, string Fab, Guid ReadBy, DateTimeOffset ReadAt, EventMetadata Metadata`; no URL member),
   published on 200; one `IntegrationEventAuditHandler` arm. A unit test asserts the contract has no member
   whose name contains `Url`, so a later edit cannot add one silently, and an integration test reads the
   persisted audit row and asserts it contains no `rtsp`.
4. **UI**: `EditCameraAddressDialog` pre-fills from the new route via an RTK Query endpoint in
   `apps/shared/src/api/cameras.api.ts` (no caching beyond the open dialog), quotes its version, and does
   not render a form on failure.
5. **Retired or re-pointed assertions** (spec 404 plan §5, line numbers verified on `6206ecf9`):
   `ListCamerasQueryHandlerTests.cs:150` (expects the origin); `StoredMisreadCameraAddressIntegrationTests.cs:39,54`
   (expect `<unparseable url>`, plus a database read proving the row is unchanged);
   `ChangeCameraAddressIntegrationTests.cs:64,107,129,158,163` and `RepointStreamIntegrationTests.cs:84`
   re-pointed from `GET` to a database read (or to the new route), so they keep proving storage;
   `e2e/camera-detail.spec.ts:27,57` and `e2e/in-flight-focus.spec.ts:133` expect the origin with
   `exact: true`. Spec 404's option-A dialog facts are **not** built; the dialog facts become "pre-filled
   from the full address", "refused fetch shows no form", "masked value never sent".
6. **New red tests**: full address returned to a `sse.cameras.write` holder; 403 for a token with only
   `sse.cameras.read` (a kiosk token); 404 for another fab's camera and for an unknown camera, identical
   bodies; `no-store` present; one audit row per 200 and none per refusal; the audit payload contains no
   `rtsp`.
7. **Baselines**: `CameraCatalogRouteTableCharacterisationTests` gains the route (a characterisation
   update named in the PR); `EndpointScopeDeclarationTests` sees the new route in the write group.
