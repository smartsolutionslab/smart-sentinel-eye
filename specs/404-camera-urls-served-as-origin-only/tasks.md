# Tasks 404: Camera URLs are served as their origin only

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3016 (feature-level;
no per-task issues).

**Gate before phase 4: D-1 (spec §6) confirmed by the owner.** These tasks implement option A. Any
other option redoes phase 3; option C stops the lane (needs an ADR).

**Phase 4a colour: RED** (behaviour-changing). Every red test compiles on unchanged code (no new
production type is referenced), so each must be seen **failing on an assertion**. A new-behaviour test
that arrives green is a phase-4 failure. The re-pointed integration assertions (T004) and the
characterisation set (plan §5.5) are expected **green** before and after.

**Engineers:** backend-engineer (T010, T011), frontend-engineer (T020, T021). **Test-writer:** T001-T005.
**Security-sensitive: yes**: phase 6 runs `security-reviewer` alongside `/code-review`.

**Forbidden in every task:** `RtspUrl.cs` (incl. `FromStored`), `UrlRedaction.cs`, `UrlAuthority.cs`,
`CameraConfiguration.cs`, `Camera.cs`, `CameraEndpoints.cs`, the request records, any record in
`Shared.Contracts` (incl. `CameraRegisteredV1`/`CameraAddressChangedV1`), any migration, any stored row
(no data rewrite: #2990), the DTO records' shape or field names, `cameras.schema.ts`,
`CamerasPage.tsx`/`CameraDetailPage.tsx` logic, the route-table baseline, any existing assertion not
named below, Vitest fixtures with a full `rtspUrl`, project references, shard filters, ADRs, the
constitution, CLAUDE.md, suppressions, thresholds, analyzer configs. No client-side masking.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No foundational
task: nothing in `Shared.Kernel`, `Shared.Contracts` or `AppHost` changes, so backend and frontend fan
out from the start of each phase.

---

## Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] New `tests/CameraCatalog.Application.Tests/Queries/CameraUrlServedAsOriginTests.cs`
  per plan §5.1 (Get and List; port kept; path, query, fragment dropped; legacy `FromStored` row ->
  `UrlRedaction.Unparseable`; markers absent; two rows redacted independently). Expected red: served
  value equals the stored string.
- [ ] **T002** [P] [US1] `tests/CameraCatalog.Application.Tests/Queries/ListCamerasQueryHandlerTests.cs:150`:
  expected value `"rtsp://10.0.5.77/h264"` -> `"rtsp://10.0.5.77"`. The **only** edit in that file.
  Expected red.

```sh
dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~CameraUrlServedAsOriginTests|FullyQualifiedName~ListCamerasQueryHandlerTests"
```

- [ ] **T003** [P] [US1] Integration red (argued, observed in CI shard-1; Docker is down locally):
  `tests/Integration.Tests/CameraCatalog/StoredMisreadCameraAddressIntegrationTests.cs:39,54` ->
  `<unparseable url>` + body `ShouldNotContain("12345")`; add a stored-column read proving the row is
  unchanged; update the class doc paragraph that says masking is a separate issue. In
  `ChangeCameraAddressIntegrationTests.cs` add one fact: after a correction, `GET` `rtspUrl` is
  `"rtsp://10.0.5.44"`.
- [ ] **T004** [US1] (same files as T003, so sequential after it) Re-point to the database, per the table
  in plan §5.2: `ChangeCameraAddressIntegrationTests.cs:64, :107, :129, :158/:163` and
  `tests/Integration.Tests/StreamDistribution/RepointStreamIntegrationTests.cs:84`. Each gets a private
  `StoredUrlAsync`. **These must not be weakened to origin assertions** (`:129` and `RepointStream:84`
  would then pass vacuously). Expected: green against unchanged code once the stack runs (they read
  what is stored), and green after.

```sh
dotnet build tests/Integration.Tests -c Release   # compile proof locally
# CI shard-1 runs: FullyQualifiedName~StoredMisreadCameraAddressIntegrationTests|FullyQualifiedName~ChangeCameraAddressIntegrationTests|FullyQualifiedName~RepointStreamIntegrationTests
```

- [ ] **T005** [P] [US1] `apps/management-cameras/src/features/cameras/EditCameraAddressDialog.test.tsx`:
  the four new facts of plan §5.3 (empty on open; origin shown as text and as the input's description;
  untouched Save sends nothing and shows the required error; reopen starts empty). Adjust **only the
  setup** of `:185` "Submits a form-level submit once when nothing is in flight" to type a valid
  address first; its assertion is untouched. Expected red: the field holds `currentUrl`, Save calls
  `changeAddress`.
- [ ] **T006** [P] [US1] Playwright, per plan §5.4: `e2e/camera-detail.spec.ts:27, :57` (+ the
  empty-field assertion in the `:42` test) and `e2e/in-flight-focus.spec.ts:133`. CI only. Run
  `pnpm typecheck:e2e`, `pnpm lint:e2e` and `prettier --check` on the two files locally.

```sh
pnpm --filter @smart-sentinel-eye/management-cameras exec vitest run src/features/cameras/EditCameraAddressDialog.test.tsx
pnpm --filter @smart-sentinel-eye/management-cameras typecheck
```

Return **verbatim** output for every command; it is quoted in the PR body.

## Phase 2: Green (engineers; tests not edited)

- [ ] **T010** [P] [US1] backend-engineer: `GetCameraQueryHandler.cs:55` and
  `ListCamerasQueryHandler.cs:72` wrap the URL in `UrlRedaction.ToOrigin(...)` (plan §3.1-3.2). T001,
  T002 green; the characterisation set green; `dotnet build -c Release` clean.
- [ ] **T011** [P] [US1] backend-engineer: doc comment on the `RtspUrl` parameter of `CameraDto.cs`
  and `CameraSummaryDto.cs` (plan §3.3). Same commit as T010 or the next.
- [ ] **T020** [P] [US1] frontend-engineer: `EditCameraAddressDialog.tsx` per plan §4.1. T005 green;
  the rest of `apps/management-cameras` green; `eslint`, `prettier --check`, `tsc --noEmit` clean.
- [ ] **T021** [P] [US1] frontend-engineer: doc comments on `CameraSummary.rtspUrl` and
  `CameraDetail.rtspUrl` in `apps/shared/src/api/cameras.api.ts` (plan §4.3). `apps/shared` lint,
  prettier, typecheck clean.

Dependencies: T010 <- T001, T002. T020 <- T005. T011, T021 have no test dependency. Backend (T010,
T011) and frontend (T020, T021) are disjoint and run in parallel.

## Phase 3: Adversarial (test-adversary)

- [ ] **T030** [US1] Hunt for any response path that still carries the stored URL: name filter,
  `includeRetired`, every sort/order, last page, a `fabId` narrowing, an `Idempotency-Key` replay of
  `POST /cameras`, the `ETag`, every 4xx body on `GET`/`PATCH`; and for `ToOrigin` inputs the stored
  column can hold that `FromStored` admits but `From` refuses (spec 389/392/394 shapes): each served
  value is an origin or `<unparseable url>`, never containing a marker. New test files only.

## Phase 5: Verify

- [ ] **T040** Spec §7 procedure end to end once Docker is available, or CI shard-1 + the Playwright
  job as the record, with the stored-column read quoted. Latency: N/A (spec header).

## Phase 6: QA

- [ ] **T050** `/code-review` + `security-reviewer` (disclosure through `sse.cameras.read`; the D-1
  round trip; no new write path to `FromStored`).

## Commits (ADR-0030; each builds alone, ADR-0087)

1. `test(3016): ...` T001-T006 (red; suite red until 2).
2. `fix(3016): serve camera URLs as their origin only` T010-T011.
3. `fix(3016): correct a camera address from an empty field` T020-T021.
4. `test(3016): ...` T030.
