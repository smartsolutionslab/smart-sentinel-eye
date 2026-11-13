# Verification: The operator console's live updates reach the hub

Observed, in the order the tasks ran. Nothing here is inferred.

## T001 — baseline (2026-11-13, branch `495-console-hub-url`, clean tree, **Release**)

| Suite                                                                                                                                  | Result                        |
| -------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| `tests/Integration.Tests`: `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests`, `LayoutHubCorsTests` | **31 passed**, 0 failed       |
| `tests/Architecture.Tests`: `BoundaryTests` (includes `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`)                  | **29 passed**, 0 failed       |

Two things that looked like failures and are not:

- **Run the composition snapshots in `Release`.** A Debug run fails all four lanes at `arg[3] "Release"` against `"Debug"` (the migrations
  rebuilder's configuration argument is part of the pinned composition). Not a defect on `develop`; the snapshot is a Release literal.
- **The full `Architecture.Tests` run does not finish locally.** `AgentBriefClaimTests.Every_brief_in_the_repository_is_scanned_by_this_guard`
  fails with 39 `unscanned` briefs, all under `.claude/worktrees/494-track-frontend/…`, and the test host then crashes. The guard walks the
  filesystem under the repo root and meets the nested worktree. CI has no worktrees, so this is a local-environment artefact. The other
  `AgentBriefClaimTests` pass in isolation. It clears when the `494-track-frontend` worktree is resolved.

## Red, observed before the change (T003, T004, T007) — Release, AppHost unchanged

**T003** `AppHostOneOriginTests.The_console_reaches_the_hub_on_layout_composition_directly`:

```text
Shouldly.ShouldAssertException : environment
    should contain key
"VITE_LAYOUT_HUB_URL"
    but does not
```

**T004** `AppHostKioskOneOriginTests.Layout_composition_admits_the_kiosk_the_wall_the_gateway_and_the_console_origins_from_endpoint_references`
(the existing assertion, edited first because this feature changes behaviour; it is not a characterisation test):

```text
Shouldly.ShouldAssertException : origins
    should be
["api-gateway", "kiosk-wall", "kiosk-web", "management-web"]
    but was (case sensitive comparison)
["api-gateway", "kiosk-wall", "kiosk-web"]
    difference
["api-gateway", "kiosk-wall", "kiosk-web", *]
```

**T007** `AppHostCompositionCharacterisationTests`, three lanes red (`Developer`, `EndToEndJob`, `Unparseable`), two green and unchanged
(`Publish`, `IntegrationFixture`, which compose no web apps). The snapshots are literals with no regeneration path, so they were
**hand-edited** (script kept out of the repo; every later `[N]` renumbered) to add, on `management-web`, a `Reference` relationship to
`layout-composition` and `VITE_LAYOUT_HUB_URL={layout-composition.bindings.http.url}/hubs/layouts`, and on `layout-composition` a
`Reference` relationship to `management-web` and `LayoutHub__Cors__AllowedOrigins__3={management-web.bindings.http.url}`:

```text
lane 'Developer', resource 'resource layout-composition-rebuilder : ProjectRebuilderResource': the composed model differs from
AppHostComposition/Developer.snapshot.txt at line 715.
  snapshot:   [50] ResourceRelationshipAnnotation { Resource=resource:management-web, Type="Reference" }
  composed: resource layout-composition-rebuilder : ProjectRebuilderResource
```

(the same at line 683 for `EndToEndJob` and line 715 for `Unparseable`). The class's own message says a moved line means the composition
moved; here it is meant to.

## Green, after the AppHost change (T006, T008) — Release

`WebApplications.cs`: `management-web` gets `VITE_LAYOUT_HUB_URL` from `LayoutHubUrl(layoutComposition)` (no `WithReference`, no
`VITE_LAYOUT_HUB_ORIGIN`, no `WaitFor`), and `layout-composition` gets `LayoutHub__Cors__AllowedOrigins__3` from `managementWeb`'s endpoint.
The two stale comments and the dev paragraph and origins sentence of `docs/deployment-frontend-env.md` are corrected; its production
section already said `management-web` needs all five variables, so FR-007 needed no new text.

| Suite                                                                                                                    | Result                                       |
| ------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------- |
| `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests` (5 lanes), `LayoutHubCorsTests` | **32 passed** (baseline 31, plus T003), 0 failed |
| `BoundaryTests` (the gateway's realtime/media boundary test, **unmodified**)                                              | **29 passed**                                |

The hand-edited snapshots matched the composed model on the first green run, so the renumbering was right.

## T002 — the defect, observed (2026-11-13), before the fix

**What was observed against, and what that proves.** The Aspire stack already running on this machine (AppHost PID 80440, started 10:44)
was booted from the merged spec-494 tree, whose `AddManagementWeb` is identical to this branch's before `4f3b15e4`: no
`VITE_LAYOUT_HUB_URL` for the console and no console origin on `layout-composition`. It is therefore the pre-change wiring. It was observed
with `curl` (no sign-in, no token, read-only requests), **not** through the page in a browser; the page itself is T009.

| Request                                                                              | Result                                       |
| ------------------------------------------------------------------------------------ | -------------------------------------------- |
| `POST http://localhost:5173/hubs/layouts/negotiate?negotiateVersion=1` (the console) | **404**                                      |
| `POST http://localhost:5180/hubs/layouts/negotiate?negotiateVersion=1` (the one origin) | **404**                                   |
| Control: `POST` the same path on `layout-composition` directly (`:17627`)            | **401** (the hub is there; it wants a token) |
| Preflight (`OPTIONS`) to `layout-composition`'s hub path from `Origin: http://localhost:5173` | `204`, **no `Access-Control-Allow-Origin`** |
| Control: the same preflight from `Origin: http://localhost:5174` (the kiosk)         | `204` with `Access-Control-Allow-Origin: http://localhost:5174` |

Both halves of the defect are therefore real: the console's page-relative hub path reaches nothing at either of its origins (premise 6
holds), and `layout-composition` does not admit the console's own port, which is the entry `4f3b15e4` adds. The `5180` origin was already
admitted, so only the missing `VITE_LAYOUT_HUB_URL` stood between the page at the one origin and the hub.
