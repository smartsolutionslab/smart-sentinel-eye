# Plan 404: Camera URLs are served as their origin only

**Spec**: [spec.md](./spec.md). **Issue**: #3016. **Blocked on**: D-1 (spec §6); written for option A.

## 1. Constitution and ADR check

| Rule | How this plan meets it |
|---|---|
| §II value objects | No domain change. DTOs are the API boundary and carry primitives already. |
| §III no cross-context refs | `UrlRedaction` is `Shared.Kernel`; CameraCatalog.Application already references it (`GetCameraQueryHandler.cs:5`). |
| §IV latency | N/A: management reads only. |
| ADR-0036 smallest change | One expression in each of two handlers; one dialog; doc comments; test re-pointing. No new type, helper or abstraction. |
| ADR-0113 | `PATCH` and its `If-Match` path untouched. |
| ADR-0139 / ADR-0144 | Behaviour-changing, red first; the lane names every existing assertion it changes (§5) and changes no other. |
| ADR-0079 | `changeCameraAddressSchema` unchanged; its existing `min(1)` is what refuses an untouched Save. |
| ADR-0103 | Integration through the Aspire fixture; database reads via `CreateCameraCatalogDbContextAsync` (pattern: `CrossFabCameraIntegrationTests.cs:121`). |
| No ADR | Spec header: DTO shape unchanged; content decided by the owner. Option C of D-1 would need one. |

## 2. Bounded context and layers

**CameraCatalog only.** Layers touched: **Application** (read handlers, DTO doc comments). Not
touched: Domain (`RtspUrl`, `Camera`), Infrastructure (`CameraConfiguration` value converter,
`FromStored`), Api (`CameraEndpoints`, requests), `Shared.Kernel` (`UrlRedaction`), `Shared.Contracts`.

**Frontend:** `apps/management-cameras` (edit dialog), `apps/shared` (type doc comments only).

No entities, value objects, invariants, domain events or integration events change. No migration.

## 3. Backend design

### 3.1 `GetCameraQueryHandler` (`src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs:55`)

`found.Url.Value` -> `UrlRedaction.ToOrigin(found.Url.Value)`. The handler already materialises the
row (`ToListAsync`, `:35-38`), so this runs in memory.

### 3.2 `ListCamerasQueryHandler` (`.../ListCamerasQueryHandler.cs:72`)

`camera.Url.Value` -> `UrlRedaction.ToOrigin(camera.Url.Value)` inside the existing top-level
`Select`. EF Core client-evaluates a method call in the **top-level** projection (it fetches the
converted `rtsp_url` column, then calls the method per row), so the SQL is unchanged. If the engineer
finds EF refusing it, the fallback is to project `camera.Url` and map after `ToListAsync`; either is
acceptable, the observable result is the same. The legacy-row integration test (list case) proves it
through EF and Postgres, which the in-memory `ICameraQuerySource` fake cannot.

`ToOrigin` never throws for a non-null string (`UrlRedaction.cs:36-48` guards the BCL parser) and the
column is non-nullable, so no new failure path enters the read; a bad row cannot take the fab's
listing down (spec 389's concern).

### 3.3 Doc comments

`CameraDto.cs` and `CameraSummaryDto.cs`: a `<summary>` on the `RtspUrl` parameter saying it is the
stored address's origin (`scheme://host[:port]`, via `UrlRedaction.ToOrigin`) or `<unparseable url>`,
never the address itself, and why (issue #3016 belongs in the PR, not the comment; the *why* is
"a read scope must not disclose a credential or secret path a stored address carries").

## 4. Frontend design (D-1 option A)

### 4.1 `EditCameraAddressDialog.tsx`

- `defaultValues: { rtspUrl: '' }` and `reset({ rtspUrl: '' })` on open. The prop stays `currentUrl`
  (renaming touches callers and tests for no behaviour; the doc comment on the prop says it is the
  origin as served).
- Render the current origin as read-only context next to the field, for example a
  `<p className="text-sm text-fg-muted">Currently: <code>{currentUrl}</code></p>` above the input, and
  tie it to the input with `aria-describedby` so a screen reader announces it.
- Keep the `useEffect` deps (`open`, `currentUrl`) so a refetch while closed still resets.
- `Dialog` `description` may mention that the whole address must be entered. Wording is the
  engineer's; it must not claim the stored address is shown.
- Nothing else in the dialog changes (refusal banner, in-flight guard, `If-Match` version).

### 4.2 `CameraDetailPage.tsx`, `CamerasPage.tsx`

No code change (FR-006): they already render `record.rtspUrl` / `row.rtspUrl` verbatim. The column
header "RTSP URL" stays (A-1).

### 4.3 `apps/shared/src/api/cameras.api.ts`

Doc comments on `CameraSummary.rtspUrl` and `CameraDetail.rtspUrl` (origin only, may be
`<unparseable url>`, never pre-fill a write with it). No type change.

## 5. Tests

### 5.1 Red, backend unit (`tests/CameraCatalog.Application.Tests`)

New file `Queries/CameraUrlServedAsOriginTests.cs`, using the handlers with the existing in-memory
`ICameraQuerySource` fake that `GetCameraQueryHandlerTests`/`ListCamerasQueryHandlerTests` use:

- Get and List, stored `rtsp://10.0.5.77:8554/h264?token=PWMARK#FRAGMARK` -> `rtsp://10.0.5.77:8554`;
  served string contains no `h264`, `PWMARK`, `FRAGMARK`.
- Get and List, stored `rtsp://cam.example/PATHMARK` -> `rtsp://cam.example`.
- Get and List, the #2999 legacy shape `rtsp://USERMARK:PWMARK#@cam/s`, built with
  `RtspUrl.FromStored` (allowed in tests: `FromStoredCallSiteTests` scans `src/` only) ->
  `<unparseable url>` (assert against `UrlRedaction.Unparseable`), no `USERMARK`/`PWMARK`.
- List with two rows: each row redacted independently (no shared state).

Expected red on unchanged code: each `ShouldBe` sees the verbatim stored string.

Existing assertion changed (owner decision requires it):
`tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs:150`
`summary.RtspUrl.ShouldBe("rtsp://10.0.5.77/h264")` -> `ShouldBe("rtsp://10.0.5.77")`. Red on
unchanged code.

### 5.2 Integration (`tests/Integration.Tests`, all three classes already in **shard-1**; no new class,
so no shard-filter edit)

Red (owner decision requires the change):

- `CameraCatalog/StoredMisreadCameraAddressIntegrationTests.cs:39` and `:54`:
  `ShouldBe(MisreadAddress)` -> `ShouldBe("<unparseable url>")` plus the raw body
  `ShouldNotContain("12345")`. Add, in the same class, a read of the stored column after the GETs:
  still `MisreadAddress` (SC-004). The class doc paragraph "The value is returned as stored; masking it
  ... is a separate issue" is updated to the new behaviour.
- `CameraCatalog/ChangeCameraAddressIntegrationTests.cs`: one new fact, "a corrected address is read
  back as its origin": after the PATCH, `GET` returns `rtspUrl == "rtsp://10.0.5.44"`.

Re-pointed to the database, so they keep proving storage (FR-009). Each would otherwise either fail
(`:64`, `:107`) or pass **vacuously** (`:129`: `ShouldNotBe(CorrectedUrl)` is always true once the
response is an origin; `:158`/`:163`: equal origins do not prove an unchanged path). Add a private
`StoredUrlAsync(Guid camera)` per class:
`context.Cameras.AsNoTracking().Where(c => c.Id == CameraIdentifier.From(camera)).Select(c => c.Url.Value).SingleAsync()`.

| File:line | Today | After |
|---|---|---|
| `ChangeCameraAddressIntegrationTests.cs:64` | GET `rtspUrl` == `CorrectedUrl` | stored == `CorrectedUrl` |
| `ChangeCameraAddressIntegrationTests.cs:107` | GET `rtspUrl` == `CorrectedUrl` | stored == `CorrectedUrl` |
| `ChangeCameraAddressIntegrationTests.cs:129` | GET `rtspUrl` != `CorrectedUrl` | stored == `OriginalUrl` (stronger, and cannot pass vacuously) |
| `ChangeCameraAddressIntegrationTests.cs:158,163` | GET before == GET after | stored == `OriginalUrl` after the 400 |
| `StreamDistribution/RepointStreamIntegrationTests.cs:84` | GET `rtspUrl` == `CorrectedUrl` | stored == `CorrectedUrl`. **An origin assertion here would be vacuous**: `rtsp://camera-sim:8554/original` and `/corrected` share the origin `rtsp://camera-sim:8554`. |

The version assertions (`:68`, `VersionOfAsync`) keep reading the API: the version is not masked.

Integration tests cannot run locally (Docker down); red is argued here and observed in CI shard-1.

### 5.3 Red, frontend unit (`apps/management-cameras`)

`src/features/cameras/EditCameraAddressDialog.test.tsx`, new facts:

- the RTSP URL field is empty on open (`toHaveValue('')`) although `currentUrl` is given;
- the current origin is shown as text (`getByText('rtsp://10.0.5.12/h264')` within the dialog, since the
  fixture is what the API "returned") and is the input's accessible description;
- an untouched Save does not call `changeAddress` and shows the required-field error;
- reopening (`open` false -> true) after typing starts empty again.

Existing test changed (D-1 requires it): `EditCameraAddressDialog.test.tsx:185` "Submits a form-level
submit once when nothing is in flight" submits the **pre-filled** field. Its setup gains one
`fireEvent.change`/`user.type` of a valid address before the submit; its assertion
(`toHaveBeenCalledTimes(1)`) is unchanged. On unchanged code the new facts are red; this test stays
green before and after.

Not changed, deliberately: `CamerasPage.test.tsx:125`, `CameraDetailPage.test.tsx:143,468`,
`CameraDetailFabRemoval.test.tsx:161`, `CameraDetailRevocation.test.tsx:196` and every fixture with a
full `rtspUrl`. They mock the API and assert the page renders what it returns, which is FR-006; a
fixture with a path is still a valid "whatever the server sends" input.

### 5.4 Playwright (`e2e/`, CI only)

Changed (owner decision requires it; each would fail against the masked API):

| File:line | Today | After |
|---|---|---|
| `e2e/camera-detail.spec.ts:27` | `getByText('rtsp://10.0.5.98/stream')` visible | `getByText('rtsp://10.0.5.98', { exact: true })` visible, and `getByText('rtsp://10.0.5.98/stream')` count 0 |
| `e2e/camera-detail.spec.ts:57` | `getByText('rtsp://10.0.5.77/corrected')` visible | `getByText('rtsp://10.0.5.77', { exact: true })` visible |
| `e2e/in-flight-focus.spec.ts:133` | same as `:57` | same as `:57` |

Added: in `camera-detail.spec.ts` "operator corrects a camera address" (`:42`), after opening the
dialog, `await expect(field).toHaveValue('')` before `field.fill(...)`.

`exact: true` matters: with the "Currently:" context visible in the open dialog and the detail field
behind it, a substring match could resolve to two elements. The three assertions above run after the
dialog has closed, but exact matching keeps them strict-mode-safe.

Unaffected: `e2e/in-flight-focus.spec.ts:715` (a fulfilled listing fixture; renders whatever it
gives), every `registerCamera(..., { url })` and `.fill(...)` call.

### 5.5 Characterisation (must stay green, unmodified)

`CameraCatalogRouteTableCharacterisationTests`, `FromStoredCallSiteTests`, `FromStoredCallerIlTests`,
all `RtspUrl*`, `UrlRedaction*`, `CameraUrl*` tests, `GetCameraQueryHandlerTests`, the rest of
`ListCamerasQueryHandlerTests`, `CameraCatalogClientLogRedactionAdversarialTests`, every other
management-cameras Vitest file.

## 6. Risks

- **R-1** The dialog change is a UX regression for correcting a single character; accepted under D-1 A.
- **R-2** A future consumer that needs the full address (for example a second write client) cannot get
  it from the API. Today none does (spec §1); provisioning reads `CameraRegisteredV1`.
- **R-3** `ToOrigin` lower-cases the host; an operator comparing against a vendor sheet may notice.
  Cosmetic.

## 7. Engineers

- **backend-engineer**: §3 (two handler lines, two doc comments).
- **frontend-engineer**: §4 (dialog, two TS doc comments) and the e2e edits in §5.4.
- Disjoint files; they run in parallel after the test-writer's red pass.
