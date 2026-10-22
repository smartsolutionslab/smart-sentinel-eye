# Plan 438: A camera's address is read in full only by its writer

**Spec**: [spec.md](./spec.md). **Issue**: #3016. **ADR**: 0179 (Accepted). **This run**: slice A.

## 1. Constitution and ADR check

| Rule | How this plan meets it |
|---|---|
| §II value objects | No domain change. DTO and contract are boundary types (primitives allowed, ADR-0040). |
| §III no cross-context refs | CameraCatalog publishes, AuditObservability consumes, via `Shared.Contracts` only. |
| §IV latency | N/A (spec header). |
| ADR-0179 | Route, scope, fab resolution, contract shape/placement, audit arm, dialog behaviour: implemented as written. |
| ADR-0088 / spec 021 | Publish through `IEventBus` + `ITransactionalCommit.CommitAsync`, the pattern `RotateWebhookClientCommandHandler.cs:100-117` uses for a publish with no aggregate save. |
| ADR-0113 | ETag on the read; `PATCH` unchanged. |
| ADR-0142 | No idempotency key: a read. |
| ADR-0093 | `Queries/GetCameraAddressQuery.cs`, `Queries/GetCameraAddressErrors.cs`, `Queries/Handlers/GetCameraAddressQueryHandler.cs`, `DTOs/CameraAddressDto.cs`. |
| ADR-0036 | No new abstraction; reuses `ResolveWriteFabAsync`, `ResolveOperator`, `ConcurrencyHeaders.ETag`, `ICameraQuerySource`, `AuditAsync`. |

## 2. Bounded contexts and layers

- **CameraCatalog** — Application (query, handler, errors, DTO), Api (route + handler in
  `CameraEndpoints.cs` / `CameraEndpoints.Reads.cs`). Domain, Infrastructure, migrations: untouched.
- **Shared.Contracts** — `CameraCatalog/CameraAddressReadV1.cs` (foundational: both contexts compile
  against it).
- **AuditObservability** — Application: one arm in `IntegrationEventAuditHandler.cs`. Domain untouched.
  `V1ResourceMap` is convention-based (`CameraCatalog` → `Camera`, keyed on the `Camera` member), so
  no map code is expected; the engineer confirms against `V1ResourceMapTests`.
- **Frontend** — `apps/shared/src/api/cameras.api.ts` (one query endpoint + type),
  `apps/management-cameras` (`EditCameraAddressDialog.tsx`, `CameraDetailPage.tsx` props only).

No entity, value object, invariant or domain event changes. The integration event is raised by the
query handler, not by a domain event: a read changes no aggregate, so there is nothing to raise it from.

## 3. Slice A design

### 3.1 Contract (`src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs`)

```csharp
public sealed record CameraAddressReadV1(
    Guid Camera, string Fab, Guid ReadBy, DateTimeOffset ReadAt, EventMetadata Metadata) : IIntegrationEvent;
```

Doc comment states *why* it carries no address (the event exists because the address was disclosed;
carrying it would copy the credential into RabbitMQ, the outbox, dead letters and the audit table).

### 3.2 Application

- `GetCameraAddressQuery(FabIdentifier Fab, CameraIdentifier Camera, OperatorIdentifier ReadBy)`.
- `GetCameraAddressError` / `GetCameraAddressFailures.CameraNotFound(Guid)` → 404, same code and shape
  as `GetCameraFailures.CameraNotFound` (`CAMERA_NOT_FOUND`), so unknown and other-fab are identical.
- `GetCameraAddressQueryHandler(ICameraQuerySource cameras, IEventBus events, ITransactionalCommit
  commit, IClock clock)`:
  1. destructure; fab-in-predicate lookup exactly as `GetCameraQueryHandler.cs:36-39` (single fab);
  2. not found → failure, **nothing published, nothing committed**;
  3. found → publish `CameraAddressReadV1` (`ReadAt = clock.UtcNow`, `Metadata(Guid.CreateVersion7(),
     ReadAt, fab, readBy)`), then `await commit.CommitAsync(CancellationToken.None)` — after the publish
     the audit must not be skippable by a disconnect (Rotate precedent);
  4. only then return `CameraAddressDto(found.Url.Value, found.Version)`.
  A throw from publish/commit propagates (500); the DTO is never built (FR-A06).
- Logging: none of the address; if a log line is added, `UrlRedaction.ToOrigin` only (spec 373).
- **Engineer must verify** `IEventBus`/`ITransactionalCommit` resolve in CameraCatalog's container for
  an HTTP request (they are what `CameraRepository` uses), and that `CommitAsync` with no tracked
  entity changes still flushes the captured message (`SaveChangesAndFlushMessagesAsync`). The
  integration audit-row test is the proof; a unit fake cannot be.

### 3.3 Api

In `CameraEndpoints.cs`, `MapGetCameraAddress(writes)`:

```
writes.MapGet("/{camera:guid}/address", GetAddress).WithName("GetCameraAddress")
  .WithSummary("… full stored address, for correcting it. One camera per request. Every read is audited.
                Required scope: sse.cameras.write")
  .Produces<CameraAddressDto>(200).ProducesProblem(400/401/403/404)
```

`GetAddress` (in `CameraEndpoints.Reads.cs`): `ResolveWriteFabAsync` first; `ResolveOperator`; handler;
on success set `ETag` and `response.Headers.CacheControl = "no-store"`, `Results.Ok(dto)`. Route
ordering: `/{camera:guid}/address` cannot collide with `/{camera:guid}` (different segment count).

### 3.4 Audit

`IntegrationEventAuditHandler`: `public Task Handle(CameraAddressReadV1 message, CancellationToken
cancellationToken) => AuditAsync(message, cancellationToken);`. Fab-scoped (not in
`FabNeutralEvents`). `AuditPayloadRedaction` unchanged; `AuditPayloadRedactionCoverageTests.VerbatimInventory`
gains `"CameraAddressReadV1.Fab"` (a named classification decision, visible in the diff).

### 3.5 Frontend

- `cameras.api.ts`: `CameraAddress { rtspUrl: string; version: number }` and
  `getCameraAddress: build.query<CameraAddress, { cameraIdentifier: string; fabId?: string }>` →
  `GET /${id}/address`, `keepUnusedDataFor: 0`, **no `providesTags`** (must not live in the `Camera`
  cache the listing and detail share). Exported hook `useGetCameraAddressQuery`.
- `EditCameraAddressDialog.tsx`: drop props `currentUrl` and `version`; call
  `useGetCameraAddressQuery({ cameraIdentifier }, { skip: !open, refetchOnMountOrArgChange: true })`;
  while loading show a loading state and no Save; on error render the refusal (reuse the dialog's
  existing refusal rendering) and no field/Save; on success `reset({ rtspUrl: data.rtspUrl })` and quote
  `data.version` in `changeAddress`. Keep the `useEffectEvent` reset discipline (spec 267/#2624).
- `CameraDetailPage.tsx:189-195`: stop passing `currentUrl`/`version`. Nothing else on the page changes.

## 4. Slice B design (next run; recorded so slice A cannot foreclose it)

As spec 404 plan §3, re-verified: `GetCameraQueryHandler.cs:56` and `ListCamerasQueryHandler.cs:72`
wrap with `UrlRedaction.ToOrigin(...)` (top-level projection, client-evaluated; fallback: map after
`ToListAsync`). Doc comments on `CameraDto`, `CameraSummaryDto`, `CameraSummary`/`CameraDetail`.
No dialog change: slice A already removed its dependency on `rtspUrl`.

## 5. Tests

### 5.1 Slice A — red (test-writer)

| # | Where | Facts | Red on unchanged code because |
|---|---|---|---|
| R1 | `tests/Integration.Tests/CameraCatalog/CameraAddressReadIntegrationTests.cs` (**new class → add to `ci-shards/shard-1.filter`**) | writer 200: body `rtspUrl` == stored address with a path+query marker, `version` == `GET` version, `ETag` == version, `Cache-Control` contains `no-store`, version unchanged after the read | route absent → 404 |
| R2 | same | read-only probe (`realm.PlantPasswordGrantClientAsync` with a fab + `sse.cameras.read` only, pattern `VariableReadScopeIntegrationTests.cs:70-108`) → **403**, body has no `rtsp`; no token → 401 | 404 today, not 403 |
| R3 | same | another fab's camera and an unknown identifier → 404, equal `title`/`status`; refused bodies contain no `rtsp` | passes vacuously today for 404 — so R3 is paired with R1 in one fact: the same caller gets 200 for its own camera, else the fact fails |
| R4 | same | audit: refusals first (R2/R3 calls), then one 200, then `PollForAuditRowAsync` (`EndToEndIngestionIntegrationTests.cs:49`) for `eventKind=CameraAddressReadV1&resourceIdentifier={camera}`: **exactly one** row, actor = reader subject, fab = camera's fab, payload `ShouldNotContain("rtsp")`, nor the host, nor the marker | no row ever appears |
| R5 | `tests/Shared.Contracts.Tests/CameraAddressReadV1Tests.cs` | the contract's only `string` members, recursively including `EventMetadata`, are `Fab` and `Metadata.Fab`; no member name contains `Url`, `Uri` or `Address`; plus a **counterfactual** private probe record with `string StreamEndpoint` that the same predicate must report | type does not exist (compile-red, quoted) |
| R6 | `tests/CameraCatalog.Application.Tests/Queries/GetCameraAddressQueryHandlerTests.cs` | found → DTO verbatim + version, exactly one event published with `Camera/Fab/ReadBy/ReadAt` and `Metadata.Actor == ReadBy`, one commit, publish before commit; not found / other fab → `CameraNotFound`, zero publishes, zero commits; commit throws → handler throws, no DTO | compile-red |
| R7 | `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs` | one `MappingCase` for `CameraAddressReadV1` → `Camera` / camera guid | completeness fact fails once R5's type exists without the row; the case itself fails until mapped |
| R8 | `tests/Architecture.Tests/AuditPayloadRedactionCoverageTests.cs` | `VerbatimInventory` += `"CameraAddressReadV1.Fab"` | classification fact fails on the new contract without it |
| R9 | `apps/management-cameras/src/features/cameras/EditCameraAddressDialog.test.tsx` | (a) on open, requests the address route and the field holds **its** `rtspUrl` (fixture differs from anything the page has: `rtsp://10.0.5.12/h264?token=X`); (b) Save quotes the **address read's** version (fixture version ≠ the page's); (c) 403 / 404 / network error → refusal shown, no `#edit-camera-url`, no Save; (d) closed dialog makes no request; (e) reopen refetches | dialog reads props today |
| R10 | `e2e/camera-detail.spec.ts` `:42` test | before `field.fill`, `page.waitForResponse(/\/cameras\/[^/]+\/address$/)` answered 200 and `expect(field).toHaveValue('rtsp://10.0.5.98/stream')` | no such request today (CI only) |

Existing Vitest that renders the dialog through `CameraDetailPage` mocks must add a handler for the
address route; that is setup, not assertion, and every such file is named in the PR.

**Characterisation updates named in the PR** (not red, not weakened):
`CameraCatalogRouteTableCharacterisationTests` baseline gains the route; `EndpointScopeDeclarationTests`
sees it in the write group (engineer confirms whether it needs a baseline edit);
`BoundaryTests.Every_integration_event_has_an_audit_handler` goes red on the contract alone and green
with the arm — that is the guard proving FR-A08.

### 5.2 Slice B — red (next run)

Spec 404 plan §5.1-5.4 minus every option-A dialog fact, re-verified on `2e00b2ff`:
`ListCamerasQueryHandlerTests.cs:150` → origin; new `CameraUrlServedAsOriginTests` (port kept;
userinfo/path/query/fragment markers absent; legacy `FromStored` row → `UrlRedaction.Unparseable`);
`StoredMisreadCameraAddressIntegrationTests.cs:39,54` → `<unparseable url>` + body has no `12345` +
column byte-identical; re-point to a database read: `ChangeCameraAddressIntegrationTests.cs:64,107,129,
158,163`, `RepointStreamIntegrationTests.cs:84` (an origin assertion there is **vacuous**:
`/original` and `/corrected` share `rtsp://camera-sim:8554`); new: a kiosk-shaped token's `GET /cameras`
body contains no path/query marker; Playwright `camera-detail.spec.ts:27,57`,
`in-flight-focus.spec.ts:133` → origin with `exact: true`. R10 must stay green **unmodified** across
slice B: it is the proof the ordering holds.

### 5.3 Counterfactuals (prove each guard can fail)

- R4: temporarily drop the publish → poll times out; add `Url` to the contract → payload contains `rtsp`.
- R5: the `StreamEndpoint` probe proves a name-only check would have missed a renamed field.
- R2: move the route to the `reads` group → 200 for the kiosk probe.
- R9(c): remove the error branch → a field renders on 403.
Record each observed failure in the PR body.

## 6. Risks

- **R-1** Outbox commit on a read adds a database round trip to an operator click. Accepted; off-SLO.
- **R-2** RTK Query or Redux devtools could keep the credential in memory past the dialog;
  `keepUnusedDataFor: 0` and no shared tag bound it. Security review checks for persistence.
- **R-3** Slice B merged without slice A would corrupt data. Tasks gate B on A being on `develop`.

## 7. Engineers and order

1. **test-writer**: R1-R10 (slice A), verbatim output.
2. **backend-engineer** (T010-T013) and **frontend-engineer** (T020-T021) in parallel — disjoint
   files; the backend's first commit is the contract (T010), which nothing in the frontend needs.
3. **test-adversary**, **security-reviewer** + `/code-review` at phase 6.
