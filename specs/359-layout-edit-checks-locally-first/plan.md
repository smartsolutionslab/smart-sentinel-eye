# Plan 359: A layout draft edit runs its local refusals before asking CameraCatalog

**Spec**: [spec.md](./spec.md). **Issue**: #2937.

## 1. Constitution / ADR alignment

| Rule | How this plan meets it |
|---|---|
| §Testing / ADR-0139 | Phase 4a RED: the tests in §4 are observed failing on the untouched handler first. |
| ADR-0113 | The version check stays after load and before mutation; only its position relative to the remote refusal moves. |
| ADR-0047 / ADR-0089 | No new `ApiError`; an unreachable CameraCatalog stays an exception — it is simply no longer reached by a refusable request. |
| ADR-0116, spec 017 plan §III | The bounded cross-context call is made only when its answer can decide the request. |
| No cross-context refs | Unchanged; `ICameraFabGuard` remains the Application port. |
| ADR-0084 | `HandleAsync` keeps its shape (one block moves); no metric worsens. |

## 2. Bounded context and layers

LayoutComposition, **Application only**: `src/LayoutComposition/Application/Commands/Handlers/EditDraftRevisionCommandHandler.cs`.
No Domain, Infrastructure, Api, contract or migration change. No entity, value object or event changes.

## 3. The change

In `HandleAsync`, swap the two refusal blocks so the `RefuseRevision(...)` block precedes the
`RefuseCrossFabTilesAsync(...)` block. Nothing else: both private methods, their comments, the log
call inside `RefuseCrossFabTilesAsync`, and the mutate/save/log tail are untouched. Resulting order:

```
ValidateGrid -> GetByIdentifierAsync -> RefuseRevision -> RefuseCrossFabTilesAsync -> EditDraft/Save
```

## 4. Tests (test-writer, red first)

All in existing files — no new test class.

**Fake** — `tests/LayoutComposition.Application.Tests/Fakes/FakeCameraFabGuard.cs`:
- add `public int Calls { get; private set; }`, incremented on every `CamerasOutsideFabAsync` call
  (both modes);
- add `public static FakeCameraFabGuard Unreachable()` whose call throws
  `HttpRequestException("CameraCatalog unreachable")` (after counting). Existing `Permissive`/`Strict`
  behaviour is unchanged.

**Red tests** — `tests/LayoutComposition.Application.Tests/Commands/DraftRefusalOrderCharacterisationTests.cs`:

| Test | Today | After |
|---|---|---|
| **replaces** `Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_TileCameraOutsideFab` with `Edit_with_a_camera_outside_the_fab_and_a_stale_version_returns_LayoutRevisionStale_without_asking_camera_catalog` (Strict guard; asserts `LayoutRevisionStale`, expected 41 / actual `layout.Version`, `guard.Calls == 0`) | red: `TileCameraOutsideFab` | green |
| new `Edit_with_a_stale_version_never_asks_camera_catalog` (Permissive) | red: `Calls == 1` | green |
| new `Edit_naming_a_missing_revision_never_asks_camera_catalog` (Permissive, current version, revision 42 -> `LayoutRevisionNotFound`) | red: `Calls == 1` | green |
| new `Edit_of_a_published_revision_never_asks_camera_catalog` (Permissive, current version, revision 1 published -> `NotADraft`) | red: `Calls == 1` | green |
| new `Edit_with_a_stale_version_is_answered_LayoutRevisionStale_while_camera_catalog_is_unreachable` (Unreachable) | red: throws `HttpRequestException` | green |

**Supporting, expected green before and after** (regression guard against over-correction, AS-6):
extend `Edit_with_a_camera_outside_the_fab_returns_TileCameraOutsideFab_listing_those_cameras` with
`guard.Calls.ShouldBe(1)` — hold the Strict fake in a local to read it. Its other assertions stay.

Update the class `<summary>` so it no longer claims to pin the cross-fab-before-stale order: it now
states the edit order this spec decides (spec 359) while the branch tests remain characterisation
(spec 345).

**Untouched and must stay green unmodified:** the other four edit tests and three branch tests in the
same file; `EditDraftRevisionCommandHandlerTests`; `StaleVersionRejectionTests`;
`CreateLayoutDraftCommandHandlerTests`.

## 5. Proof level

**Unit-level proof suffices.** The order lives entirely in the handler; the endpoint maps the
`Result` code to status unchanged, and the guard implementation is untouched. An integration test
would need a CameraCatalog outage or call counting inside the Aspire stack for no additional
evidence. Phase 5 states this explicitly.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Fix skips the guard on the valid path (security regression, FR-014) | AS-6 `Calls == 1` + the existing cross-fab test stays green |
| Local refusal order shifts | `Edit_with_a_stale_version_and_a_missing_revision_returns_LayoutRevisionStale`, `..._published_revision_returns_LayoutRevisionStale`, `..._current_version_and_a_missing_revision_returns_LayoutRevisionNotFound`, `..._empty_tile_set_and_an_unknown_layout_returns_GridEmpty` unmodified |
| Coverage gate (ADR-0065) | No new branches; count unchanged |
