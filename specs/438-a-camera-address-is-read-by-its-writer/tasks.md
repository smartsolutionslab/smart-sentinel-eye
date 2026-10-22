# Tasks 438: A camera's address is read in full only by its writer

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3016 (feature-level; no
per-task issues). **ADR**: 0179.

**This run delivers slice A only (US1, T001-T050).** Slice B (US2, T101-T150) is a second PR on the
same issue, started only once slice A is merged to `develop` (R-3: B before A corrupts stored data).
PR A says "Part of #3016"; PR B says "Closes #3016".

**Phase 4a colour: RED** (behaviour-changing, both slices). HTTP-level reds compile on unchanged code
and fail on an assertion; reds that reference new types (R5, R6) are compile-red, quoted verbatim.
A new-behaviour test arriving green is a phase-4 failure. Characterisation updates (route-table
baseline, `VerbatimInventory`) are named in the PR, never silent.

**Security-sensitive: yes.** Phase 6 runs `security-reviewer` (mandatory) alongside `/code-review`.

**Forbidden in slice A:** `GetCameraQueryHandler.cs`, `ListCamerasQueryHandler.cs`, `CameraDto.cs`,
`CameraSummaryDto.cs` (slice B's), `RtspUrl.cs`/`FromStored`, `UrlRedaction.cs`, `Camera.cs`,
`CameraConfiguration.cs`, any migration or stored row (#2990), `CameraRegisteredV1`/
`CameraAddressChangedV1`, `AuditPayloadRedaction.cs`, the realm JSON and the scope catalogue (no new
scope), `cameras.schema.ts`, `CamerasPage.tsx`, ADRs, the constitution, CLAUDE.md, suppressions,
thresholds, analyzer configs, any existing assertion not named here.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109).

---

## Slice A — US1

### Phase 1: Red (test-writer; production code untouched)

- [ ] **T001** [P] [US1] New `tests/Integration.Tests/CameraCatalog/CameraAddressReadIntegrationTests.cs`:
  plan §5.1 R1-R4. **Add the class to `tests/Integration.Tests/ci-shards/shard-1.filter`** (a missing
  entry fails deterministically). R3 is asserted alongside a 200 for the caller's own camera so the
  404 cannot pass vacuously. R4 does refusals first, then one 200, then asserts exactly one row.
- [ ] **T002** [P] [US1] New `tests/Shared.Contracts.Tests/CameraAddressReadV1Tests.cs`: R5, with the
  `StreamEndpoint` counterfactual probe.
- [ ] **T003** [P] [US1] New `tests/CameraCatalog.Application.Tests/Queries/GetCameraAddressQueryHandlerTests.cs`:
  R6 (hand-written `IEventBus`/`ITransactionalCommit` fakes recording order; existing
  `ICameraQuerySource` fake).
- [ ] **T004** [P] [US1] `V1ResourceMapTests.cs` (R7, one `MappingCase`) and
  `AuditPayloadRedactionCoverageTests.cs` (R8, one inventory line). Nothing else in either file.
- [ ] **T005** [P] [US1] `apps/management-cameras/src/features/cameras/EditCameraAddressDialog.test.tsx`:
  R9 (a)-(e). Update setup (not assertions) of existing dialog tests and of any `CameraDetailPage*`
  test that opens the dialog, to serve the address route; list every such file.
- [ ] **T006** [P] [US1] `e2e/camera-detail.spec.ts` `:42` test: R10. Run `pnpm typecheck:e2e`,
  `pnpm lint:e2e`, `prettier --check` on it locally; CI runs it.

```sh
dotnet build tests/Integration.Tests -c Release
dotnet test tests/Shared.Contracts.Tests -c Release --filter "FullyQualifiedName~CameraAddressReadV1Tests"
dotnet test tests/CameraCatalog.Application.Tests -c Release --filter "FullyQualifiedName~GetCameraAddressQueryHandlerTests"
dotnet test tests/AuditObservability.Application.Tests -c Release --filter "FullyQualifiedName~V1ResourceMapTests"
dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~AuditPayloadRedactionCoverageTests|FullyQualifiedName~BoundaryTests"
pnpm --filter @smart-sentinel-eye/management-cameras exec vitest run src/features/cameras/EditCameraAddressDialog.test.tsx
# CI shard-1: FullyQualifiedName~CameraAddressReadIntegrationTests
```

Return **verbatim** output for every command; it is quoted in the PR body.

### Phase 2: Green (engineers; tests not edited)

**Foundational — blocks T011-T013:**

- [ ] **T010** [US1] backend-engineer: `src/Shared.Contracts/CameraCatalog/CameraAddressReadV1.cs`
  (plan §3.1). T002 green.

**Then, in parallel:**

- [ ] **T011** [P] [US1] backend-engineer: CameraCatalog Application — `GetCameraAddressQuery.cs`,
  `GetCameraAddressErrors.cs`, `Handlers/GetCameraAddressQueryHandler.cs`, `DTOs/CameraAddressDto.cs`,
  and its DI registration (plan §3.2). T003 green.
- [ ] **T012** [US1] backend-engineer (after T011, same context): Api — `MapGetCameraAddress` in
  `CameraEndpoints.cs`, `GetAddress` in `CameraEndpoints.Reads.cs` (plan §3.3); route-table baseline
  and `EndpointScopeDeclarationTests` characterisation updates, named in the commit body.
- [ ] **T013** [P] [US1] backend-engineer: `IntegrationEventAuditHandler.cs` one arm (plan §3.4).
  T004 + `BoundaryTests` green.
- [ ] **T020** [P] [US1] frontend-engineer: `apps/shared/src/api/cameras.api.ts` — `CameraAddress`
  type, `getCameraAddress` query (`keepUnusedDataFor: 0`, no `providesTags`), exported hook.
- [ ] **T021** [US1] frontend-engineer (after T020): `EditCameraAddressDialog.tsx` +
  `CameraDetailPage.tsx` props (plan §3.5). T005 green; whole `apps/management-cameras` Vitest green;
  `eslint`, `prettier --check`, `tsc --noEmit` clean for `apps/shared` and `apps/management-cameras`.

`dotnet build -c Release` clean (no new suppression). Backend (T010-T013) and frontend (T020-T021) own
disjoint files. T001 and T006 go green only on the running stack (CI).

### Phase 3: Adversarial (test-adversary; new test files only)

- [ ] **T030** [US1] Hunt: `fabId` naming a fab the caller does not hold; a multi-fab caller without
  `fabId`; a retired camera; a non-guid / empty segment; `HEAD` and `OPTIONS` on the route; a token with
  `sse.cameras.write` but no fab; whether any 4xx body, header or log line carries the address; whether
  a response reaches the client when the commit fails; concurrent reads → one row each.

### Phase 5: Verify

- [ ] **T040** Spec §7 slice-A procedure (or CI shard-1 + Playwright as the record), quoting the
  `no-store` header, the 403 for a kiosk token and the audit row's payload. Latency: N/A.

### Phase 6: QA

- [ ] **T050** `/code-review` + **`security-reviewer`**, checklist:
  1. Route is in the `writes` group (`sse.cameras.write`); a read-only/kiosk/device token gets 403.
  2. Fab resolved by `ResolveWriteFabAsync` **before** the lookup; other-fab and unknown are the same 404.
  3. `Cache-Control: no-store` on the 200; nothing in the gateway (`src/ApiGateway/appsettings.json`
     `camera-catalog` route) caches or rewrites it.
  4. Audit committed **before** the body is returned; a commit failure discloses nothing.
  5. `ReadBy`/actor come from the token subject (`ResolveOperator`), never request input.
  6. `CameraAddressReadV1` has no address-capable member; audit rows contain no `rtsp`; no log line,
     problem body or trace attribute carries more than the origin.
  7. One camera per request; no list/search/export form; no `Idempotency-Key` path that could replay.
  8. Frontend: the address lives only in the open dialog (no shared tag, `keepUnusedDataFor: 0`, no
     persistence, not in the listing/detail cache); a refused fetch renders no form; the masked/page
     value cannot reach the form (props removed); `kiosk-web` never calls the route.
  9. The wider disclosure ADR-0179 §3 accepts (every console user in the fab) is not widened further.

### Commits (ADR-0030; each builds alone, ADR-0087; no Co-Authored-By, ADR-0086)

1. `test(3016): …` T001-T006 (red).
2. `feat(3016): add the CameraAddressReadV1 contract` T010.
3. `feat(3016): read a camera's full address on a write-scoped, audited route` T011-T013.
4. `feat(3016): pre-fill the address correction from the full-address read` T020-T021.
5. `test(3016): …` T030.

---

## Slice B — US2 (second PR; start only after slice A is on `develop`)

- [ ] **T101** [P] [US2] test-writer: plan §5.2 unit reds (`CameraUrlServedAsOriginTests`,
  `ListCamerasQueryHandlerTests.cs:150`).
- [ ] **T102** [P] [US2] test-writer: integration — `StoredMisreadCameraAddressIntegrationTests.cs:39,54`
  (+ column byte-identical), kiosk-token `GET /cameras` marker-free fact; then re-point to a database
  read `ChangeCameraAddressIntegrationTests.cs:64,107,129,158,163` and
  `RepointStreamIntegrationTests.cs:84` (green before and after; never weakened to an origin).
- [ ] **T103** [P] [US2] test-writer: Playwright `camera-detail.spec.ts:27,57`,
  `in-flight-focus.spec.ts:133` → origin, `exact: true`. R10 (slice A) stays **unmodified** and green.
- [ ] **T110** [P] [US2] backend-engineer: `UrlRedaction.ToOrigin` in `GetCameraQueryHandler.cs:56`,
  `ListCamerasQueryHandler.cs:72`; doc comments on both DTOs.
- [ ] **T120** [P] [US2] frontend-engineer: doc comments on `CameraSummary.rtspUrl` /
  `CameraDetail.rtspUrl` (no logic change; FR-B03).
- [ ] **T130** test-adversary: every filter/sort/page/`includeRetired`/`fabId` combination, 4xx bodies,
  `ETag`, legacy shapes `FromStored` admits (specs 389/392/394) → origin or `<unparseable url>`.
- [ ] **T150** `/code-review` + `security-reviewer` (no read path left carrying the address; no
  dialog regression; stored rows untouched).

Commits: `test(3016): …` → `fix(3016): serve camera addresses as their origin on read` →
`docs(3016): …` (TS doc comments) → `test(3016): …` (adversarial).
