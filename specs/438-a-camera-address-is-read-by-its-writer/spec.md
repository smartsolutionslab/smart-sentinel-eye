# Spec 438: A camera's address is read in full only by its writer

**Feature Branch**: `feat/3016-camera-address-write-read` (cut from `origin/develop` at `2e00b2ff`)

**Created**: 2026-10-22

**Status**: Ready for phase 4 (slice A). No `[NEEDS CLARIFICATION]`: every decision below was made by
the owner on #3016 (2026-10-17, 2026-10-18, 2026-10-22) and recorded in **ADR-0179 (Accepted)**. This
spec implements that ADR's Implementation Notes; it does not re-open or extend it.

**Input**: Issue [#3016](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3016),
*Camera API and UI serve stored camera URLs verbatim, including credential-bearing legacy rows*.

**Supersedes**: spec 404 (branch `fix/3016-mask-camera-url-on-read`, `4f0a284b`, never merged), written
for option A of its D-1 and blocked on the ADR. Its analysis (§1 below) is reused and re-verified on
`2e00b2ff`; its option-A dialog facts (empty field, "Currently: …") are **not** built.

**Lane.** Autonomous (ADR-0144). **Behaviour-changing**: phase 4a is **red** (ADR-0139).
**Security-sensitive** (a new route that discloses a credential, a new audit event, mask-on-read):
phase 6 runs `security-reviewer` alongside `/code-review` — mandatory, not optional.

**Spec number.** 438. Checked 2026-10-22: `origin/develop` highest 435; remote heads hold 436
(`fix/2783-terminal-write-retry`) and 437 (`feat/2225-authorization-decision-point`); sibling worktrees
`D:\Github\sse-*` hold nothing above 437. **Re-check before each PR opens** (two unmerged branches can
claim one number).

**ADRs.** **ADR-0179** (the decision implemented here), ADR-0070 (Minimal APIs), ADR-0073 (`V1`
contract), ADR-0040 (primitives at the wire boundary), ADR-0088 (outbox), ADR-0113 (`If-Match`/ETag),
ADR-0114 (fab resolution), ADR-0142 (no `Idempotency-Key` on a read), ADR-0075 (RTK Query), ADR-0079
(RHF + Zod), ADR-0103 (Aspire fixture), ADR-0105 (`Ensure.That`), ADR-0139/0144 (lane, red first).
Prior art: spec 373 (logs as origin), spec 377 (audit payload redaction), spec 389 (`FromStored`).

**No new ADR is needed.** ADR-0179 fixes the route, scope, fab resolution, contract name, placement,
members and audit arm. One ordering it implies but does not spell out is recorded as a requirement
(FR-A06, audit before disclosure), because "every 200 is audited" cannot hold otherwise.

**Latency budget.** **N/A.** Management reads and a management dialog, off every §IV leg (ADR-0179
§7). The kiosk never reads the catalogue's URL field (§1).

---

## 1. What is true today (premise verified on `2e00b2ff`)

**The URL leaves the API in exactly two responses, both reads, both verbatim.**

| Route | Built at | Value |
|---|---|---|
| `GET /cameras/{camera}` → `CameraDto.RtspUrl` | `GetCameraQueryHandler.cs:56` | `found.Url.Value` |
| `GET /cameras` → `items[].rtspUrl` (`CameraSummaryDto`) | `ListCamerasQueryHandler.cs:72` (EF projection) | `camera.Url.Value` |

`POST /cameras` (201 + `Guid`), `PATCH /cameras/{camera}` (204), `POST …/retire` (204) carry no URL.
No search/export route exists. `LayoutLifecycleHub` carries no camera URL. `RtspUrl.From` refusals do
not echo input. Both reads sit in the `reads` group, `RequireAuthorization(Scope.Sse.Cameras.Read)`
(`CameraEndpoints.cs:42-47`), resolved with `ResolveReadFabsAsync` (`CameraEndpoints.Reads.cs:115`).

**Who holds `sse.cameras.read`** (ADR-0179 Context): default scope of `management-web`, `kiosk-web`,
`kiosk-wall`, `integration-test-admin`, `scenario-simulator`, and the runtime `Kiosk`/`Device` bundles.
`sse.cameras.write` is a default scope of `management-web` (realm `smart-sentinel-eye-realm.json:147`
and siblings); kiosks and devices do not hold it.

**UI consumers of `rtspUrl`.**

| Site | Use |
|---|---|
| `apps/management-cameras/src/features/cameras/CamerasPage.tsx:133-135` | renders the list column |
| `CameraDetailPage.tsx:226` | renders the detail field |
| `CameraDetailPage.tsx:189-195` → `EditCameraAddressDialog.tsx:54,67` | **pre-fills the edit field** (`currentUrl`) and quotes `record.version` |
| `apps/shared/src/api/cameras.api.ts:43,63` | `CameraSummary` / `CameraDetail` types |

Non-UI readers of the catalogue (`ScenarioSimulator` `CameraCatalogClient`, StreamDistribution
`CameraCatalogFabLookup`, LayoutComposition `CameraCatalogFabGuard`) never read the URL field. Stream
provisioning takes the address from `CameraRegisteredV1`/`CameraAddressChangedV1`, untouched here.

**The round trip that masking would corrupt** (spec 404 §4, still true): once `GET` returns the origin,
an untouched Save in the correction dialog PATCHes `rtsp://10.0.5.98` over `rtsp://10.0.5.98/stream`
under a current `If-Match`; `Camera.ChangeAddress` sees a different value and repoints the stream.
**Therefore masking (slice B) must not merge before the dialog stops pre-filling from `GET` (slice A).**

**Masking means** `UrlRedaction.ToOrigin(stored)` (`src/Shared.Kernel/UrlRedaction.cs`):
`scheme://host[:port]`, host lower-cased; userinfo, path, query and fragment dropped; any shape the
URL-authority guards refuse (including the #2999 legacy `rtsp://admin:12345#@cam/s`) becomes the
literal `<unparseable url>` and never echoes input (ADR-0179 §1, owner decision 2026-10-17).

## 2. Slices (two PRs, one spec, one issue)

| Slice | Story | PR | Mergeable alone because |
|---|---|---|---|
| **A (this run)** | US1 — the writer's full-address read, its audit, and the dialog using it | `feat(3016)`, "Part of #3016" | Adds a route to a population that already reads the address on `GET`; the dialog now pre-fills from the authoritative full read. `GET` responses unchanged. |
| **B (next run)** | US2 — mask on read | `fix(3016)`, "Closes #3016" | Only after A is on `develop`: the dialog no longer reads `rtspUrl` from `GET`, so masking cannot be written back. |

Why split: A is a new route + contract + audit arm + UI fetch; B rewrites two handlers and re-points
~10 existing assertions across unit, integration and Playwright. Together they exceed one reviewable
diff, and the security review of the new disclosure route deserves its own PR. Each ordering other
than A → B corrupts data on merge.

## 3. User stories

### US1 (P1, slice A): an operator corrects a camera from its full address, and that read is audited

As an operator holding `sse.cameras.write`, when I open "Correct the address" I want the field
pre-filled with the camera's **stored** address (fetched on purpose, for this edit), so that I can fix
one character without retyping a path I cannot otherwise see; and as a fab security officer I want
every such full read recorded as who read which camera when, without the address itself, and refused
to kiosks, wall displays and devices.

### US2 (P1, slice B, depends on US1 merged): reads show only the origin

As a fab security officer, I want `GET /cameras` and `GET /cameras/{camera}`, and the console pages
that render them, to carry only `scheme://host[:port]`, so that no `sse.cameras.read` holder (every
kiosk, wall display and device) receives a password, token or secret path a stored address carries.

## 4. Acceptance scenarios

```gherkin
Feature: a camera's full address is read only by a writer, on its own route (ADR-0179)

  Background:
    Given operator "op-3@munich.test" holds sse.cameras.read and sse.cameras.write in fab "munich"
    And camera "Dock-2" in "munich" is stored with "rtsp://10.0.5.98/stream?token=PWMARK" at version 3

  # ---- US1 / slice A ----

  Scenario: a writer reads the full address for editing
    When the operator sends GET /cameras/{Dock-2}/address
    Then the response is 200 with body { "rtspUrl": "rtsp://10.0.5.98/stream?token=PWMARK", "version": 3 }
    And the ETag carries version 3
    And Cache-Control is "no-store"
    And the camera's version is still 3

  Scenario: the full read is audited without the address
    When the operator sends GET /cameras/{Dock-2}/address and receives 200
    Then exactly one audit row of kind "CameraAddressReadV1" exists for Dock-2
    And its actor is the operator's subject, its fab "munich"
    And its payload contains neither "rtsp" nor "PWMARK" nor "10.0.5.98"

  Scenario: the dialog pre-fills from the full read, not from the listing
    When the operator opens "Correct the address" on Dock-2's page
    Then the browser sends GET …/cameras/{Dock-2}/address
    And the field holds "rtsp://10.0.5.98/stream?token=PWMARK"
    When the operator saves without typing
    Then PATCH carries If-Match 3 and the stored address is unchanged

  # conflict: version quoted is the one the field was filled from
  Scenario: a correction quotes the version of the address read
    Given the address read returned version 3
    When the camera is renamed elsewhere (now version 4) and the operator saves a correction
    Then PATCH carries If-Match 3 and answers 412 CAMERA_VERSION_STALE, storing nothing

  # auth: read-only callers stay refused
  Scenario Outline: only a writer in the camera's fab gets the address
    When <caller> sends GET /cameras/{Dock-2}/address
    Then the response is <status> and the body contains no "rtsp"
    And no CameraAddressReadV1 audit row is written
    Examples:
      | caller                                                   | status |
      | no token                                                 | 401    |
      | a token with sse.cameras.read only (kiosk/device shape)  | 403    |
      | an operator of "dresden" only (camera exists in munich)  | 404    |
      | the operator, for an unknown camera identifier           | 404    |

  Scenario: a refused fetch never opens a form
    Given the address read answers 403 (or 404, or fails)
    When the operator opens "Correct the address"
    Then the dialog shows the refusal and renders no RTSP URL field and no Save

  # bad request
  Scenario: a multi-fab caller must name the fab, as for every camera write
    Given a caller holding "munich" and "dresden" sends GET /cameras/{Dock-2}/address without fabId
    Then the response is 400 CAMERA_FAB_REQUIRED and no audit row is written

  # ---- US2 / slice B ----

  Scenario: reads serve the origin only
    When any caller with sse.cameras.read sends GET /cameras/{Dock-2} or GET /cameras
    Then "rtspUrl" is "rtsp://10.0.5.98" and the body contains neither "stream" nor "PWMARK"

  Scenario: a credential-bearing legacy row is served without its credential, and is not rewritten
    Given a row inserted by SQL with rtsp_url "rtsp://admin:12345#@cam/s"
    When it is read by identifier and by listing
    Then both answer "<unparseable url>", neither body contains "admin" or "12345"
    And the stored rtsp_url is byte-identical afterwards

  Scenario: masking does not break the correction (the A -> B ordering holds)
    Given slice B is merged
    When the operator opens "Correct the address" on Dock-2 and saves without typing
    Then the field held the full address and the stored address is unchanged
```

## 5. Requirements

**Slice A**

- **FR-A01** `GET /cameras/{camera:guid}/address[?fabId=]` exists in the `writes` group
  (`RequireAuthorization(Scope.Sse.Cameras.Write)`); no new scope; realm unchanged (ADR-0179 §3).
- **FR-A02** 200 body `CameraAddressDto(string RtspUrl, int Version)`: the stored address verbatim and
  the camera's version; `ETag` = `ConcurrencyHeaders.ETag(version)`; `Cache-Control: no-store`.
- **FR-A03** One camera per request: no list, search or export form.
- **FR-A04** Fab first via `ResolveWriteFabAsync`, before the camera is looked at; another fab's
  camera and an unknown camera answer the same 404 problem shape (ADR-0179 §4).
- **FR-A04a** A retired (`Decommissioned`) camera answers that same 404 `CAMERA_NOT_FOUND` - same
  title and status, no distinguishing variant - with no event published and nothing committed. Reason:
  the disclosure exists to support correcting the address, and `PATCH` refuses a retired camera with
  `CAMERA_RETIRED`, so there is nothing to correct and nothing to disclose (owner decision, Heiko).
- **FR-A05** A read: no change to the camera (version unchanged), no `If-Match`, no `Idempotency-Key`.
- **FR-A06** Every 200 publishes exactly one `CameraAddressReadV1` and **commits it before the body is
  returned**; if the publish or commit fails, the request fails and the address is not returned (no
  disclosure without its audit). The announcement survives a crash or broker outage only because
  `CameraAddressReadV1` is declared in CameraCatalog's `durableOutboxMessageTypes` (ADR-0185 decisions 1
  and 3; an undeclared type is sent Inline and a failed send is swallowed), so durability is a property
  of that declaration, guarded by `DurableOutboxListTests`. No event on 400/401/403/404.
- **FR-A07** `CameraAddressReadV1(Guid Camera, string Fab, Guid ReadBy, DateTimeOffset ReadAt,
  EventMetadata Metadata) : IIntegrationEvent` in `src/Shared.Contracts/CameraCatalog/`; **no member
  that holds or could hold an address**; `ReadBy` is the token subject, never client input; `Metadata`
  carries fab and actor = `ReadBy`.
- **FR-A08** `IntegrationEventAuditHandler` gains one `Handle(CameraAddressReadV1 …)` arm delegating to
  `AuditAsync`; no new handler type; no `AuditPayloadRedaction` register entry (nothing to redact).
- **FR-A09** No log line, problem body or header written by this route contains the address beyond
  its origin.
- **FR-A10** `EditCameraAddressDialog` pre-fills from the new route on every open and quotes **its**
  version in `If-Match`; it no longer receives `currentUrl` or `version` from the page, so a `GET`
  value cannot reach the form structurally. On refusal or failure it renders the refusal and no
  field/Save. The RTK Query endpoint keeps the result no longer than the open dialog
  (`keepUnusedDataFor: 0`, refetch on every open, no `providesTags` shared with `Camera`).
- **FR-A11** `GET /cameras` and `GET /cameras/{camera}` are unchanged in slice A.

**Slice B**

- **FR-B01** `CameraDto.RtspUrl` and `CameraSummaryDto.RtspUrl` = `UrlRedaction.ToOrigin(stored)`
  in the two read handlers, for every filter/sort/page/`includeRetired` combination.
- **FR-B02** DTO shape, field names, JSON types, `Produces` and the route-table entries of the two
  reads unchanged; `RtspUrl`, `FromStored`, `UrlRedaction`, stored rows unchanged (no rewrite, #2990).
- **FR-B03** The UI renders `rtspUrl` as returned; it neither masks nor reconstructs.
- **FR-B04** Integration assertions that proved a **stored** address through `GET` read the database
  (or the new route) instead, so they keep proving storage and cannot pass vacuously.

## 6. Assumptions (explicit)

- **A-1** `rtspUrl` keeps its name and may hold `<unparseable url>` (spec 404 A-1); doc comments say so.
- **A-2** The address read mirrors `PATCH`'s fab handling exactly: the dialog passes the same `fabId`
  (today none) to both, so a multi-fab operator meets the same 400 on both.
- **A-3** Sorting cannot leak the hidden part: `AllowedSortFields` is `name`, `registeredAt`.
- **A-5** A retired camera is folded into not-found rather than answered `CAMERA_RETIRED`: a distinct
  answer would confirm the identifier once existed in the caller's fab (FR-A04a).
- **A-4** Usability cost accepted by ADR-0179 Consequences: after slice B, cameras behind one NVR show
  the same origin in lists; only the name and the dialog distinguish them.

## 7. Independent end-to-end test procedure

Slice A: boot the stack; sign in as `op-3@munich.test`; register `rtsp://10.0.5.98/stream`; open the
detail page and "Correct the address": the network tab shows `GET /camera-catalog/cameras/{id}/address`
answering 200 with `Cache-Control: no-store`; the field holds the full address. `curl` the route with a
kiosk token: 403. `GET /audit-observability/audit?eventKind=CameraAddressReadV1&resourceIdentifier={id}`
returns one row whose payload has no `rtsp`. Save untouched: 204, `SELECT rtsp_url` unchanged.

Slice B: the same detail page shows `rtsp://10.0.5.98`; the dialog still shows the full address; a
SQL-inserted legacy row reads as `<unparseable url>` and is byte-identical afterwards.

## 8. Success criteria

- **SC-001** A `sse.cameras.read`-only token can obtain no camera's address from any route (slice A:
  403 on the new route; slice B: origin on the old ones).
- **SC-002** Every 200 of the new route has exactly one audit row; no refusal has one; no row contains
  `rtsp`.
- **SC-003** The contract cannot gain an address-holding member without a test failing.
- **SC-004** An untouched Save never changes a stored address, before or after slice B.
