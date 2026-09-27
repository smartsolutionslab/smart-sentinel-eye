# Plan 274: The names a route swallowed

**Spec:** `spec.md` · **Issue:** #2358 · **Lane:** supervised (ADR-0037)

## 0. Shape of the change

**Route shape only.** Two template strings in one endpoints file, the two RTK Query
`url`s that call them, and every test and doc comment that names the old paths. No
handler, query, DTO, domain type, message, migration, AppHost resource or gateway route
changes. Bounded context: **SystemVariables** (Api layer only), plus its client slice in
`apps/shared`.

**Phase-4a colour: RED (behaviour-changing).** Today `GET /system-variables/snapshot`
reaches the wrong handler. After the change it reaches `GetOne`. That is new observable
behaviour, so the tests are written first and observed failing (ADR-0139, constitution
§Testing).

## Constitution / ADR check

| Rule | Status |
|---|---|
| §III bounded-context isolation | Untouched. No project reference changes, nothing crosses a context. |
| §IV latency budget | **Not on the event-to-overlay path.** See §4. |
| §VIII trust boundaries | The read scope stays on both moved routes. This is asserted (T003), not assumed. |
| ADR-0070 Minimal APIs, one group per context | Kept. The two routes stay in the existing `/system-variables` group with the same `.RequireAuthorization(Scope.Sse.Variables.Read)` chain. |
| ADR-0106 gateway | `/system-variables/{**catch-all}` + `PathRemovePrefix` already forwards `/-/…`. No config change. |
| ADR-0087 each commit builds | Holds (§5). |
| ADR gap | No ADR covers a literal-escape convention. Flagged in spec, not written. |

## 1. Change

`src/SystemVariables/Api/SystemVariableEndpoints.cs`:

```csharp
group.MapGet("/-/snapshot", GetSnapshot)   // was "/snapshot", line 67
group.MapGet("/-/resolve", ResolveText)    // was "/resolve",  line 81
```

Endpoint names (`GetOverlaySnapshot`, `ResolveOverlayText`), summaries, `Produces*` and
handler bodies stay as they are. The `ResolveOverlayText` summary references
"GetOverlaySnapshot" by endpoint name, not by path, so it stays correct. Registration
order does not matter: once no literal shares a segment with `{name}`, precedence has
nothing to decide.

`apps/shared/src/api/systemVariables.api.ts:202,225`: `url: '/-/snapshot'` and
`url: '/-/resolve'`. The cache key, tags and `upsertQueryData('getOverlaySnapshot', ...)`
use the endpoint **name**, not the URL, so the kiosk's push path
(`useOverlayHubHandlers.ts:196`) is unaffected.

## 2. Every call site

**Must change (runtime callers):**

| File:line | What |
|---|---|
| `src/SystemVariables/Api/SystemVariableEndpoints.cs:67` | `MapGet("/snapshot")` |
| `src/SystemVariables/Api/SystemVariableEndpoints.cs:81` | `MapGet("/resolve")` |
| `apps/shared/src/api/systemVariables.api.ts:202` | `url: '/snapshot'`. Kiosk tile via `useGetOverlaySnapshotQuery` (`apps/kiosk-web/src/features/cell/LayoutGrid.tsx:381`) |
| `apps/shared/src/api/systemVariables.api.ts:225` | `url: '/resolve'`. Management-web editor via `useResolveOverlayTextQuery` (`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx:171`) |

**Must change (tests calling the literal path):**

| File:line | Note |
|---|---|
| `tests/Integration.Tests/Fixtures/OverlaySnapshotReadiness.cs:27` | the shared fixture (spec 144 / #2201). It covers `NFR_VariableResolutionLatencyTests` (:172 wait, :225 measured loop), `ResolvedTextReachesItsFabTests:528`, `TwoPlaceholdersInOneLabelTests:86,88,135,137` and `VersionSurvivesARestartTests:289` indirectly. **One edit.** |
| `tests/Integration.Tests/SystemVariables/VersionSurvivesARestartTests.cs:141` | direct literal URL |
| `tests/Integration.Tests/SystemVariables/VariableReadScopeIntegrationTests.cs:57` | direct literal URL. **Would stay green unedited, and would be wrong.** After the move `/snapshot` hits `GetOne`, which also requires the read scope, so the 403 still arrives and the test silently stops covering the snapshot endpoint. It must move, and a `/-/resolve` row joins it. |
| `tests/Integration.Tests/SystemVariables/ResolveOverlayTextTests.cs:187,240,250,258` | direct literal URLs |
| `tests/Architecture.Tests/SystemVariableReadScopeTests.cs:40,43` | `Snapshot` const. After the move it throws "registered no GET". Add `Resolve`, which spec 148 left out, so "every read" is true again. |
| `apps/shared/src/api/systemVariables.api.test.ts:95` | `endsWith('/resolve')` **still passes** against `/-/resolve`. Tighten it to the full path, and add the same assertion for `getOverlaySnapshot`, which currently asserts no URL at all. |
| `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx:152` | same `endsWith('/resolve')` weakness, tighten |

**Doc comments naming the old path (no behaviour, update for accuracy):**
`src/SystemVariables/Application/DTOs/ResolvedOverlaySnapshotDto.cs:4`,
`src/SystemVariables/Application/DTOs/ResolvedTextPreviewDto.cs:4`,
`src/SystemVariables/Application/Queries/Handlers/ResolveOverlayTextQueryHandler.cs:10`,
`tests/Integration.Tests/Fixtures/OverlaySnapshotReadiness.cs:14`,
`tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs:43,131`,
`tests/Integration.Tests/SystemVariables/TwoPlaceholdersInOneLabelTests.cs:7`,
`tests/Integration.Tests/SystemVariables/ResolveOverlayTextTests.cs:14`,
`tests/SystemVariables.Application.Tests/Queries/ResolveOverlayTextQueryHandlerTests.cs:13`,
`apps/kiosk-web/src/features/cell/CellPage.test.tsx:1061`.

**Not changed, listed for the reviewer:** `docs/adr/0115-overlays-are-fab-neutral-templates.md:23`,
`docs/adr/0145-a-kiosks-fab-is-derived-from-its-wall.md:41`, which are decision records
of their date, and historical `specs/*`. No e2e spec intercepts either path. The only
Playwright route matchers on system-variables target `/{name}/value` and the list. There
is no checked-in OpenAPI document. The `.http` file is the template stub.

## 3. Tests (phase 4a, red first)

1. **Structural guard, new, `tests/Architecture.Tests/SystemVariableRouteShadowingTests.cs`.**
   Map `MapSystemVariableEndpoints` into a `WebApplication` in-process, the same way as
   `SystemVariableReadScopeTests.MappedEndpoints()`. Take every pair of endpoints that
   share an HTTP verb and a segment count, have identical preceding segments, and where
   one has a **literal** at the position where the other has the unconstrained `{name}`
   parameter. Assert that `VariableName.From(literal)` throws `ArgumentException`: *no
   legal variable name is shadowed*. Today that is red, naming `snapshot` and `resolve`.
   Afterwards it is green. The only other two-segment templates are PUT `{name}/value`
   and POST `{name}/archive`, which use other verbs, so no pair is formed, and `-` would
   fail `VariableName.From` even if one were. The guard
   states the invariant, not today's two names, so a third `MapGet("/foo")` fails it.
   **Prove it by counterfactual** in the PR: re-add `MapGet("/foo", ...)` locally and
   quote the red.
2. **Behaviour, new, `tests/Integration.Tests/SystemVariables/ShadowedVariableNameIntegrationTests.cs`.**
   A theory over `snapshot` and `resolve`. As admin, define the variable in `munich`
   (accept 201, or 409 if a previous run left it Defined), then
   `GET /system-variables/{literal}?fabId=munich` and expect 200, `name == literal` and
   an `ETag`. Archive it at the end, because a released name is free for reuse
   (`GetVariableQueryHandler:19`), so reruns stay clean. Red today: 400 on both.
   **The name cannot be uniquified.** The literal name is the subject, which is why the
   test tolerates 409 and cleans up. **Add the class to a `ci-shards/shard-N.filter`**,
   otherwise `integration-shard-coverage` fails deterministically.
3. **Moved-route assertions, edits:** `SystemVariableReadScopeTests` (Snapshot → `/-/snapshot`,
   add `/-/resolve`), `VariableReadScopeIntegrationTests` rows, the `OverlaySnapshotReadiness`
   URL, `VersionSurvivesARestartTests:141`, the `ResolveOverlayTextTests` URLs, and the two
   frontend `endsWith` assertions tightened to `/system-variables/system-variables/-/resolve`
   (and `.../-/snapshot`). Each of these is red before the move: a 404/400 from the wrong
   handler, or the architecture test's "registered no GET".

The engineer may not edit any of these to reach green.

## 4. Latency (§IV), definitive

**Not on the event-to-overlay path. PR line: "Latency: N/A, route shape of two on-demand
reads; not on event → overlay path."** The live path is SignalR push, and the kiosk
upserts the RTK cache with no HTTP request (`useOverlayHubHandlers.ts:196-206`,
ADR-0106 §Context). `GetSnapshot` serves the tile's opening label on mount and cache
invalidation (`LayoutGrid.tsx:381`). `ResolveText` serves the management-web preview.
**Caveat:** `NFR_VariableResolutionLatencyTests` measures this context's share of the
*Event → overlay state* leg **by polling the snapshot GET**. Its URL moves, and its
measured work (write → domain event → reverse index → resolve) does not. Phase 5 cites
that test's figure from the PR's CI TRX against the last green `develop` run.

## 5. Messaging, entities, boundaries

None touched. No domain or integration event, no value object and no invariant changes.
`VariableName` is read by the guard (Architecture.Tests already references the
SystemVariables assemblies), not modified.

**Commits (ADR-0087, each builds on its own):** (1) `fix(system-variables): move the
snapshot and resolve reads off the variable-name segment`, covering the endpoints, the
shared slice and every test and fixture edit together, so no commit carries a test/route
mismatch. (2) `docs(system-variables): name the moved routes in comments`, comment-only.
The red output from phase 4a is quoted in the PR body, not preserved as a commit.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A caller missed by grep keeps using the old path and gets `GetOne`'s 404 | §2 was built from three greps (literal path, `'/snapshot'`/`'/resolve'` url strings, endpoint and hook names). The structural guard does not catch this; the integration and frontend edits do. |
| Rollout skew: an old kiosk bundle calls `/snapshot` | Falls back to the published text (`LayoutGrid.tsx:389`); live pushes still land (spec A2). |
| A test goes on passing against the old path (the `endsWith`, the 403 row) | Named explicitly in §2 and tightened in 4a. |
| `-` mis-parsed as a route token | It is a plain literal segment. The architecture test and integration test both exercise the registered template. |
