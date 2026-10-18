# Spec 404: Camera URLs are served as their origin only

**Feature Branch**: `fix/3016-mask-camera-url-on-read` (cut from `origin/develop` at `b7001237`)

**Created**: 2026-10-18

**Status**: Draft, **one owner confirmation open (D-1, §6)** before phase 4. Everything else is
decided: the owner decided the issue on 2026-10-17 and it is recorded below, not re-opened. D-1 is not
a re-opening of that decision; it is a consequence of it that the issue did not mention and that the
lane must not settle alone, because the wrong answer corrupts stored data (§4).

**Input**: Issue [#3016](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3016),
*Camera API and UI serve stored camera URLs verbatim, including credential-bearing legacy rows*.
Split out of #2999 (spec 389, owner decision item 5).

**Owner decision (2026-10-17, on #3016), recorded here and not re-opened:**

- **Mask on read.** `CameraDto` and `CameraSummaryDto` return the redacted origin form, with
  `UrlRedaction.ToOrigin` semantics: no credentials, no path, no query.
- The management UI shows what the API returns (no client-side masking, no client-side unmasking).
- **No stored-data rewrite** (that stays #2990, "not now"). `RtspUrl.FromStored` is unchanged.
- `CameraRegisteredV1` / `CameraAddressChangedV1` payloads (RabbitMQ, the Wolverine outbox, dead
  letters) are **out of scope**.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing** (two API responses and two UI surfaces
change): phase 4a is **red** (ADR-0139). Security-sensitive (information disclosure to
`sse.cameras.read` holders): phase 6 runs `security-reviewer` alongside `/code-review`.

**Spec number.** 404, assigned by the orchestrator (402 = PR #3041; 403 and 405 taken by parallel
architects). Checked 2026-10-18 against `origin/develop` (highest: 401) and every remote branch head
(`git ls-tree <ref> specs/`: only 402 on `fix/3013-source-repair-persistent-404`). Re-check before the
PR is opened.

**ADRs.** ADR-0144 / ADR-0139 (lane; phase 4a red; the lane implements, it does not decide), ADR-0036
(smallest change), ADR-0070 (Minimal APIs; the route table is unchanged), ADR-0113 (`If-Match`; the
address correction keeps its version check), ADR-0079 (RHF + Zod; the edit form's schema is unchanged),
ADR-0075 (RTK Query; no cache change), ADR-0103 (integration through the Aspire fixture), ADR-0105
(`Ensure.That`), ADR-0065 (Application >= 80%). Constitution §III (no cross-context reference: the
helper is `Shared.Kernel`). Prior art: spec 373 (logs as origin), spec 377 (audit as origin), spec 389
(`FromStored`, the legacy rows this spec is about).

**No new ADR is needed.** The DTO keeps its shape, its field names and its JSON types; what changes is
the content of one string field, and the owner decided that on the issue, as spec 377's audit-payload
redaction was decided without one. There is no ADR on HTTP DTO versioning to amend (ADR-0073 covers
integration events only, and those are out of scope). **If the owner chooses D-1 option C** (a separate
full-address read for writers), that is new API surface and an authorization decision, and *does*
need an ADR; the lane must stop there.

**Latency budget.** N/A. `GET /cameras` and `GET /cameras/{camera}` are management reads, off every
§IV leg of the event-to-overlay path. The kiosk never reads the camera catalogue's URL (§1).

---

## 1. What was found (premise verified, not assumed)

On `b7001237` (= `origin/develop`, 2026-10-18). **Premise holds.**

**Where the DTO values are built: two places, both verbatim.**

| DTO | Built at | Value today |
|---|---|---|
| `CameraDto.RtspUrl` | `src/CameraCatalog/Application/Queries/Handlers/GetCameraQueryHandler.cs:51-58` | `found.Url.Value` |
| `CameraSummaryDto.RtspUrl` | `src/CameraCatalog/Application/Queries/Handlers/ListCamerasQueryHandler.cs:67-74` (EF projection) | `camera.Url.Value` |

No other production code constructs either record (`grep "new CameraDto\|new CameraSummaryDto" src`).

**Every response that carries the stored URL: two, both reads.**

| Route | Carries URL? | Evidence |
|---|---|---|
| `GET /cameras` (list, name filter, paging, `includeRetired`) | **Yes**, `items[].rtspUrl` | `CameraEndpoints.cs:151`, `.Produces<CameraListPageDto>` |
| `GET /cameras/{camera}` | **Yes**, `rtspUrl` | `CameraEndpoints.cs:134`, `.Produces<CameraDto>` |
| `POST /cameras` (incl. `Idempotency-Key` replay) | No: `201` + the identifier `Guid` | `CameraEndpoints.cs:58`, `ExecuteCreateAsync` stores the identifier only |
| `PATCH /cameras/{camera}` | No: `204` | `CameraEndpoints.cs:107` |
| `POST /cameras/{camera}/retire` | No: `204` | `CameraEndpoints.cs:80` |
| Search / export | None exist for cameras | — |
| SignalR | `LayoutLifecycleHub` is the only hub; it carries no camera URL | `src/LayoutComposition/Infrastructure/Broadcasting/LayoutLifecycleHub.cs` |
| `GET /streams` (`StreamHealthDto`) | No source URL field (`WhepUrl` is our SFU's) | `src/StreamDistribution/Application/DTOs/StreamHealthDto.cs` |
| Error bodies | `RtspUrl.From` refusal messages describe the rule, never echo the input | `RtspUrl.cs:22-44` |

Out of scope and unchanged: `CameraRegisteredV1.Url`, `CameraAddressChangedV1.PreviousUrl`/`.Url`
(owner decision); audit payloads already redact them (spec 377).

**Every consumer of the URL field.**

| Consumer | Reads `rtspUrl`? | Effect of masking |
|---|---|---|
| `apps/management-cameras/.../CamerasPage.tsx:132-134` (list column) | Yes, renders | Shows the origin. Intended. |
| `apps/management-cameras/.../CameraDetailPage.tsx:222` (detail field) | Yes, renders | Shows the origin. Intended. |
| `apps/management-cameras/.../CameraDetailPage.tsx:190` -> `EditCameraAddressDialog.tsx:54,67` | **Yes, pre-fills the edit field** | **Data-corrupting round trip, §4.** |
| `apps/shared/src/api/cameras.api.ts:43,63` (`CameraSummary`, `CameraDetail` types) | Type only | Doc comment only. |
| `src/ScenarioSimulator/CameraCatalog/CameraCatalogClient.cs:162` | No: projects `CameraIdentifier`, `Name` | None. |
| `src/StreamDistribution/Infrastructure/Attribution/CameraCatalogFabLookup.cs:74-82` | No: `fab`, `cameraIdentifier` | None. |
| `src/LayoutComposition/Infrastructure/Cameras/CameraCatalogFabGuard.cs:70-73` | No: `cameraIdentifier` | None. |
| `apps/kiosk-web` | No reference | None. |
| `apps/shared/src/ui/composites/*` | Test fixtures only | None. |
| `Shared.Contracts` | `CameraDto`/`CameraSummaryDto` are not there (CameraCatalog.Application) | None. |
| Route-table baseline `tests/Architecture.Tests/CameraCatalogRouteTableCharacterisationTests.cs:77` | DTO **type name** only | None (shape unchanged). |

Stream provisioning reads the URL from `CameraRegisteredV1`, not from the API, so the SFU keeps
receiving the full address.

**What `UrlRedaction.ToOrigin` returns** (`src/Shared.Kernel/UrlRedaction.cs:12-31`, pinned by
`tests/Shared.Kernel.Tests/UrlRedactionTests.cs:7-14`): `scheme://host[:port]`, host lower-cased, port
only when not the scheme default (for `rtsp`, which .NET does not register, any explicit port is kept):

| Stored | Served |
|---|---|
| `rtsp://10.0.5.5/h264` | `rtsp://10.0.5.5` |
| `rtsp://cam/stream?token=SECRET` | `rtsp://cam` |
| `rtsp://cam:8554/x?y=z#frag` | `rtsp://cam:8554` |
| `rtsp://[fd00::5]:554/x` | `rtsp://[fd00::5]:554` |
| `rtsp://CAM.Example/x` | `rtsp://cam.example` |
| `rtsp://admin:12345#@cam/s` (the #2999 legacy shape) | `<unparseable url>` |

Every shape the URL-authority guards refuse (raw `@` after the authority, text after `]`, a delimiter
inside brackets, a non-ASCII authority, an unreadable host) is served as the literal
`UrlRedaction.Unparseable` = `<unparseable url>`. It never echoes its input.

## 2. User story

### US1 (P1): A camera's address is read as its origin only

As a fab security officer, I want `GET /cameras` and `GET /cameras/{camera}`, and the management
pages that render them, to show a camera's address as `scheme://host[:port]` only, so that any token
holding `sse.cameras.read` in the fab no longer receives a password, token or secret path that a
camera row stored before #2999 (or a vendor URL with a secret in its path or query) carries, while
the operator still sees which device a camera points at.

**Why P1 / independently shippable:** two one-line changes in the read handlers, one change in the
edit dialog so the masked value cannot be written back (§4), observable end to end through the
running API and the management console.

## 3. Acceptance scenarios

```gherkin
Feature: camera addresses are read as their origin only

  Background:
    Given operator "op-3@munich.test" holds sse.cameras.read and sse.cameras.write in fab "munich"

  # happy path: single read
  Scenario: one camera's address is served as its origin
    Given camera "Line-7" in "munich" is stored with address "rtsp://10.0.5.77:8554/h264?token=SECRET"
    When the operator sends GET /cameras/{Line-7}
    Then the response is 200 and "rtspUrl" is "rtsp://10.0.5.77:8554"
    And the response body contains neither "h264" nor "SECRET"
    And every other field (cameraIdentifier, version, fab, name, registeredAt, status) is unchanged
    And the ETag still carries the version

  # happy path: listing
  Scenario: every listed row's address is served as its origin
    Given cameras in "munich" are stored with "rtsp://10.0.5.12/h264" and "rtsp://cam.example/SECRET"
    When the operator sends GET /cameras
    Then the rows' "rtspUrl" values are "rtsp://10.0.5.12" and "rtsp://cam.example"
    And the body contains no "SECRET"

  # the legacy row the issue is about
  Scenario: a credential-bearing row stored before #2999 is served without its credential
    Given a camera row inserted by SQL with rtsp_url "rtsp://admin:12345#@cam/s"
    When the operator reads it by identifier and by listing
    Then both responses are 200 and "rtspUrl" is "<unparseable url>"
    And neither body contains "admin" or "12345"
    And the stored rtsp_url is still "rtsp://admin:12345#@cam/s"

  # conflict: the masked value must not overwrite the stored address (§4)
  Scenario: correcting an address never starts from the masked value
    Given camera "Dock-2" is stored with "rtsp://10.0.5.98/stream"
    And the detail page shows "rtsp://10.0.5.98"
    When the operator opens "Correct the address"
    Then the RTSP URL field is empty
    And the current origin "rtsp://10.0.5.98" is shown as context, not as an editable value
    When the operator presses Save without typing
    Then no PATCH is sent and the field reports that an address is required
    And the stored address is still "rtsp://10.0.5.98/stream"

  # conflict: a real correction still works, under its version
  Scenario: a typed correction is stored in full and read back as its origin
    When the operator types "rtsp://10.0.5.77/corrected" and saves with the version shown
    Then PATCH /cameras/{Dock-2} answers 204
    And the stored rtsp_url is "rtsp://10.0.5.77/corrected"
    And the detail page then shows "rtsp://10.0.5.77"

  # conflict: a stale version is still refused and stores nothing
  Scenario: a stale correction leaves the stored address untouched
    When a PATCH quotes a version that is no longer current
    Then the response is 412 CAMERA_VERSION_STALE
    And the stored rtsp_url is unchanged (read from the database, not from GET)

  # bad request: unchanged
  Scenario: an unusable correction is refused as before
    When the operator sends PATCH with rtspUrl "http://not-rtsp.example/stream"
    Then the response is 400 and the stored rtsp_url is unchanged

  # auth: unchanged, and the masking is not a scope decision
  Scenario Outline: reads remain scope- and fab-gated
    When <caller> sends GET /cameras/{Line-7}
    Then the response is <status>
    Examples:
      | caller                                   | status |
      | no token                                 | 401    |
      | a token without sse.cameras.read         | 403    |
      | an operator of "dresden" only            | 404    |
```

## 4. The edit round trip (the decision point)

`CameraDetailPage.tsx:190` passes `record.rtspUrl` to `EditCameraAddressDialog` as `currentUrl`, and the
dialog uses it as the form's default value (`EditCameraAddressDialog.tsx:54`) and on every reopen
(`:67`). `changeCameraAddressSchema` (`apps/shared/src/api/cameras.schema.ts:36`) accepts any
`rtsp://` string without userinfo, and `RtspUrl.From` (`src/CameraCatalog/Domain/Camera/RtspUrl.cs:46`)
accepts an address with **no path** (`HasHost` is the only host rule). So, once the DTO is masked:

1. The operator opens "Correct the address"; the field holds `rtsp://10.0.5.98` (the origin).
2. They press Save, or change only the host, or the port.
3. The PATCH carries `rtsp://10.0.5.98` (or the edited origin) with a **current** `If-Match`, so the
   version check passes, `Camera.ChangeAddress` sees a different value (`Camera.cs:132`), and the
   stored `rtsp://10.0.5.98/stream` is **replaced by its origin**. The path is gone; the stream is
   repointed to it (`CameraAddressChangedV1`) and breaks.

Today the same unchanged Save is a harmless idempotent no-op (`Camera.cs:132`, `Url == url`). Masking
turns it into silent data loss. For a `<unparseable url>` row the client schema refuses the value
(`must start with rtsp://`), so that case cannot write, but it greets the operator with an invalid
pre-filled field.

This is a regression the owner decision does not address, so it is **D-1** (§6).

## 5. Requirements

- **FR-001** `GET /cameras/{camera}` returns `rtspUrl = UrlRedaction.ToOrigin(stored)`.
- **FR-002** `GET /cameras` returns `items[].rtspUrl = UrlRedaction.ToOrigin(stored)` for every row,
  under every filter, sort, page and `includeRetired` combination.
- **FR-003** The masking is applied in the Application read handlers, the only producers of the DTOs.
  The stored value, `RtspUrl`, `RtspUrl.FromStored`, `UrlRedaction` and the persistence configuration
  are unchanged.
- **FR-004** No response, header or error body introduced or touched by this work contains any part
  of the stored URL beyond its origin.
- **FR-005** The DTO shape is unchanged: same records, same field names, same JSON types, same
  OpenAPI `Produces` declarations; the route-table baseline is unchanged.
- **FR-006** The management UI renders `rtspUrl` exactly as returned; it neither masks nor reconstructs.
- **FR-007** (subject to D-1) The address-correction dialog never submits a value it did not receive
  from the operator's typing: its field starts empty on every open, the current origin is shown as
  read-only context, and an untouched Save sends nothing (the existing `min(1)` rule refuses it).
- **FR-008** Writes are unchanged: `POST` and `PATCH` accept and store the full address under the
  existing rules, `If-Match`, idempotency key and refusals.
- **FR-009** Integration tests that proved a **stored** address by reading it back through `GET` read
  it from the database instead, so they keep proving storage rather than weakening to the origin.

## 6. Decisions, assumptions, usability cost

**D-1 (owner confirmation required before phase 4): what the edit dialog starts from.**

| Option | What | Cost | Within the owner decision? |
|---|---|---|---|
| **A (recommended)** | Field starts empty; "Currently: `<origin>`" shown as text; untouched Save refused client-side by the existing schema | Operator must type the whole address to change any part of it, without seeing the stored path | Yes: UI shows what the API returns; no new API |
| B | Server refuses a PATCH equal to `ToOrigin(stored)` when the stored value differs | Heuristic; refuses a legitimate "drop the path" correction; new refusal code (contract change) | Partly; adds API behaviour nobody decided |
| C | A write-scoped endpoint returns the full address for pre-fill | New API surface and an authorization decision; re-exposes the credential to writers | No; needs an ADR. Lane must stop |
| D | Serve origin plus path, minus credentials | Contradicts "no path/query" | No |

The tasks are written for **A**. If the owner chooses anything else, phase 3 is redone.

**Usability cost of the owner decision (flagged, not re-opened).** With path and query gone, the
console can no longer tell an operator which stream on a device a camera points at. Concretely:
cameras behind one NVR or encoder (`rtsp://nvr:554/ch1` ... `/ch16`) all render the **same** address,
and two profiles of one camera (`/h264`, `/h265`) are indistinguishable; the camera *name* is the only
discriminator left. After a correction the operator sees only the origin, so a path typo is not
visible in the console (the stream health badge is the remaining signal). A follow-up to show a
non-secret path (an ADR-level decision about what in a path is secret) is suggested, not filed by the lane.

**[Assumption A-1]** The field keeps its name `rtspUrl` and may hold the non-URL literal
`<unparseable url>`. Renaming it (e.g. `rtspOrigin`) is a breaking contract change nobody asked for;
the doc comments on `CameraDto`, `CameraSummaryDto` and the TypeScript `CameraSummary`/`CameraDetail`
say what it now carries.

**[Assumption A-2]** The `<unparseable url>` placeholder is rendered as-is on the list and detail page.
It tells an operator the stored address needs correcting, which is the useful signal for a #2999
legacy row; no special styling is added.

**[Assumption A-3]** Sorting is unaffected: `AllowedSortFields` is `["name", "registeredAt"]`
(`ListCamerasQueryHandler.cs:12`), so no order can leak the hidden part of a URL.

## 7. Independent end-to-end test procedure

1. Boot the Aspire stack. Sign in to the management console as `op-3@munich.test`.
2. Register camera `E2E Detail <ts>` with `rtsp://10.0.5.98/stream`.
3. Open it: the detail page shows `rtsp://10.0.5.98` and no `/stream`. The list row shows the same.
4. `curl` `GET /camera-catalog/cameras/{id}` with the operator's token: `rtspUrl` is `rtsp://10.0.5.98`.
5. Open "Correct the address": the field is empty, "Currently: rtsp://10.0.5.98" is shown. Press Save:
   nothing is sent (browser network tab), the field says an address is required.
6. Type `rtsp://10.0.5.77/corrected`, Save: the page shows `rtsp://10.0.5.77`; in Postgres,
   `SELECT rtsp_url FROM cameras WHERE camera_id = '<id>'` returns `rtsp://10.0.5.77/corrected`.
7. Insert a row with `rtsp_url = 'rtsp://admin:12345#@cam/s'` by SQL; both reads return
   `<unparseable url>` and no `12345`; the row is unchanged.

## 8. Success criteria

- **SC-001** No `GET /cameras` or `GET /cameras/{camera}` response contains a stored URL's userinfo,
  path, query or fragment (asserted with marker strings in unit and integration tests).
- **SC-002** An untouched Save in the correction dialog sends no request (Vitest, and Playwright in CI).
- **SC-003** Every integration test that proves a stored address still fails if the address is not
  stored (database read, not `GET`).
- **SC-004** Stored rows are byte-identical before and after (the legacy-row integration test reads
  the column back).
