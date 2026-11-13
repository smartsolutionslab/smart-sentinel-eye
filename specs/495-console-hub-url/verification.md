# Verification: The operator console's live updates reach the hub

Observed, in the order the tasks ran. Nothing here is inferred.

## T001 — baseline (2026-11-13, branch `495-console-hub-url`, clean tree, **Release**)

| Suite                                                                                                                                             | Result                  |
| ------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------- |
| `tests/Integration.Tests`: `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests`, `LayoutHubCorsTests` | **31 passed**, 0 failed |
| `tests/Architecture.Tests`: `BoundaryTests` (includes `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs`)                            | **29 passed**, 0 failed |

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

| Suite                                                                                                                            | Result                                           |
| -------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| `AppHostOneOriginTests`, `AppHostKioskOneOriginTests`, `AppHostCompositionCharacterisationTests` (5 lanes), `LayoutHubCorsTests` | **32 passed** (baseline 31, plus T003), 0 failed |
| `BoundaryTests` (the gateway's realtime/media boundary test, **unmodified**)                                                     | **29 passed**                                    |

The hand-edited snapshots matched the composed model on the first green run, so the renumbering was right.

## T002 — the defect, observed (2026-11-13), before the fix — **PARTLY WITHDRAWN, see the correction below**

**What was observed against, and what that proves.** The Aspire stack already running on this machine (AppHost PID 80440, started 10:44)
was booted from the merged spec-494 tree, whose `AddManagementWeb` is identical to this branch's before `4f3b15e4`: no
`VITE_LAYOUT_HUB_URL` for the console and no console origin on `layout-composition`. It is therefore the pre-change wiring. It was observed
with `curl` (no sign-in, no token, read-only requests), **not** through the page in a browser; the page itself is T009.

| Request                                                                                       | Result                                                          |
| --------------------------------------------------------------------------------------------- | --------------------------------------------------------------- |
| `POST http://localhost:5173/hubs/layouts/negotiate?negotiateVersion=1` (the console)          | **404**                                                         |
| `POST http://localhost:5180/hubs/layouts/negotiate?negotiateVersion=1` (the one origin)       | **404**                                                         |
| Control: `POST` the same path on `layout-composition` directly (`:17627`)                     | **401** (the hub is there; it wants a token)                    |
| Preflight (`OPTIONS`) to `layout-composition`'s hub path from `Origin: http://localhost:5173` | `204`, **no `Access-Control-Allow-Origin`**                     |
| Control: the same preflight from `Origin: http://localhost:5174` (the kiosk)                  | `204` with `Access-Control-Allow-Origin: http://localhost:5174` |

Both halves of the defect are therefore real: the console's page-relative hub path reaches nothing at either of its origins (premise 6
holds), and `layout-composition` does not admit the console's own port, which is the entry `4f3b15e4` adds. The `5180` origin was already
admitted, so only the missing `VITE_LAYOUT_HUB_URL` stood between the page at the one origin and the hub.

### Correction to T002 (same day, found minutes later)

**The two console rows above (the `404`s at `:5173` and `:5180`) are withdrawn as evidence.** Running the e2e spec against that same stack
showed it was not healthy: `GET /` returned **404 on both `:5173` and `:5180`**, Keycloak did not answer, and `docker ps` listed no
containers. A stack that answers 404 for the console's own root also answers 404 for `/hubs/layouts/negotiate`, so those numbers say nothing
about the missing hub address. (Likely cause, not proven: files under the worktree that stack was serving were partly deleted during branch
cleanup earlier in the session.) The first e2e attempt on it, `console-live-scene-switch.spec.ts`, timed out at 300 s waiting for the
"Sign in" button, which is the **wrong red** and is not counted.

**What survives:** the rows about `layout-composition` itself (the control `401`, and the preflight that gets no
`Access-Control-Allow-Origin` from `http://localhost:5173` while `http://localhost:5174` does). Those backend processes were serving
their loaded binaries and are unaffected. They show the console's own port was not admitted before `4f3b15e4`; they do **not** show the
console's hub request failing in a browser.

**T002 is therefore not discharged.** It is redone on a healthy stack booted from the last commit before the fix (`fcc113a2`, the red
tests; the AppHost is unchanged there), then the fix is built and T009 repeats it. The task is unticked.

## T002 redone and T005 red — on a healthy stack at the last pre-fix commit (2026-11-13)

**Setup.** `fcc113a2` (the red tests; its `src/AppHost` has no console hub URL and no console origin) checked out detached, `dotnet build
SmartSentinelEye.slnx -c Release`, then `dotnet run --project src/AppHost -c Release --no-build -- ScenarioSimulator=false
PersistentStack=false`, the same command CI uses. `scripts/wait-for-e2e-stack.sh`: every composed resource started after ~285 s, every
database migrated, `:5173`, `:5174`, `:5175`, `:5176`, `:5177` serving. The previous stack, which was broken, was stopped with the
owner's go-ahead.

**T002, observed with `curl` (no token), each request made after `GET /` returned 200 at that origin:**

| Request                                                                      | Result                                                          |
| ---------------------------------------------------------------------------- | --------------------------------------------------------------- |
| `GET http://localhost:5173/` and `GET http://localhost:5180/`                | **200** (the console is up)                                     |
| `POST http://localhost:5173/hubs/layouts/negotiate?negotiateVersion=1`       | **404**                                                         |
| `POST http://localhost:5180/hubs/layouts/negotiate?negotiateVersion=1`       | **404**                                                         |
| Control: the same `POST` on `layout-composition` directly (`:14828`)         | **401** (the hub is there and wants a token)                    |
| Preflight to the hub from `Origin: http://localhost:5173`                    | `204`, **no `Access-Control-Allow-Origin`**                     |
| Control: the same preflight from `Origin: http://localhost:5174` (the kiosk) | `204` with `Access-Control-Allow-Origin: http://localhost:5174` |

So at both of the console's origins the hub path reaches nothing, while the hub is alive and admits the kiosk. Both halves of the defect
are observed. This supersedes the withdrawn rows above.

**T005 red.** `e2e/console-live-scene-switch.spec.ts` (`--project=chromium`) against that stack. It signs in, publishes two layouts, creates a
wall, and a second operator session switches it to scene B (the `other` page's assertion on line 48 **passes**); the first page, which did
nothing and can only learn of it through the hub, never changes:

```text
> 50 |     await expect(showing).toContainText(layoutBName, { timeout: 5_000 });
      - Expect "toContainText" getByTestId('wall-showing') with timeout 5000ms
        14 × locator resolved to <p data-testid="wall-showing" ...>Showing: E2E Console Live Scene A 1794571055884</p>
           - unexpected value "Showing: E2E Console Live Scene A 1794571055884"
  1 failed
    [chromium] › e2e\console-live-scene-switch.spec.ts:11:5 › a wall page open in the console follows a scene switch made by another
    operator, without a reload
  3 passed (2.8m)        # the three teardown projects
```

The earlier failure at `signInAsOperator` (300 s, "Sign in" never found) was the broken stack and is not counted.
The spec's name must not contain `wall-` or `kiosk-`: the config's patterns are unanchored, and a first draft named
`console-wall-live-update.spec.ts` was silently ignored by `chromium` ("No tests found").

## After the fix, on a healthy stack at `ddda535b` (T009, T012 in part)

Same boot as above: Release build, `dotnet run --project src/AppHost -c Release --no-build -- ScenarioSimulator=false PersistentStack=false`,
`wait-for-e2e-stack.sh` clean (every resource started after ~140 s).

**Observed on the hub (`curl`, no token), same requests as the "before" table:**

| Preflight to `layout-composition`'s hub path from… | Before the fix              | After the fix                                                     |
| -------------------------------------------------- | --------------------------- | ----------------------------------------------------------------- |
| `Origin: http://localhost:5173` (the console)      | `204`, **no** allow-origin  | `204`, `Access-Control-Allow-Origin: http://localhost:5173`       |
| `Origin: http://localhost:5174` (the kiosk)        | `204`, allow-origin `:5174` | `204`, allow-origin `:5174` (unchanged)                           |
| `Origin: http://localhost:5180` (the one origin)   | not run                     | `204`, allow-origin `:5180`                                       |
| `Origin: http://localhost:5199` (**not listed**)   | not run                     | `204`, **no** allow-origin: still refused (SC-004, observed live) |
| `POST …/hubs/layouts/negotiate` directly, no token | `401`                       | `401` (unchanged)                                                 |

**T005 green.** `e2e/console-live-scene-switch.spec.ts`, the spec that failed above, **passes** against this stack
(`4 passed (2.2m)`: the spec and the three teardown projects): the first page, which does nothing, follows the second operator's switch to
scene B within 5 s with no reload. This is at the console's own port, `:5173`.

### What could not be observed, and why

- **The one origin, `:5180`, signed in (T009 second half, T010, the `:5180` half of SC-001): not observable.** A temporary copy of the spec
  pointed at `:5180` fails at sign-in with "Sign-in failed — Failed to fetch". A browser capture shows
  `POST …/protocol/openid-connect/token` from origin `http://localhost:5180` **blocked by CORS** (no `Access-Control-Allow-Origin`); the same
  flow at `:5173` signs in and lands on _Cameras_. Not caused by this change (it touches no Keycloak setting); filed as **#3267** with the
  capture. All that can be said for `:5180` is the structural half above: the hub admits that origin.
- **The kiosks' direct hub connection, on the live stack (T011 Playwright half, T012 "`/kiosk/` and `/wall/` connect as before"): not
  discharged.** `kiosk-shows-a-wall.spec.ts` and `wall-changes-its-scene.spec.ts` (`--project=kiosk --project=wall`) were run three times
  and gave no usable result: run 1 `8 passed, 1 failed` (`Page crashed` in `page.goto` to a camera page, in the sweep helper, not the hub);
  run 2 failed in the `seed` setup project (a slow camera list) with the target test "did not run"; run 3 was **killed by the harness for
  critical memory pressure** with nothing written beyond "Running 9 tests using 3 workers". Per-test names for run 1 were not captured, so
  I do not claim which eight passed. I did not run it a fourth time.
  What stands in its place, and is weaker than observing it: the three composition snapshots change **only** for `management-web` and
  `layout-composition` (the diff touches no `kiosk-web` or `kiosk-wall` line), the kiosk theory
  `Each_kiosk_instance_reaches_the_hub_on_layout_composition_directly` passes unmodified, and `LayoutHubCorsTests` and `BoundaryTests` pass.

The stack was stopped afterwards (AppHost, services, containers: none left; free memory recovered from critical to 8.2 of 24 GB).

## Review (T016, phase 6) and dispositions

Two read-only reviewers (`security-reviewer`, `infra-reviewer`) were given the diff range and the intent, not this note's conclusions. They
did not run a build, a test or the stack; their statements about the snapshots and the lane gate come from reading the code.

**Neither found a blocker or a should-fix.** What each concluded:

- **Security.** Adding `management-web`'s origin does not widen who can call the hub: the hub is `[Authorize(Policy = Scope.Sse.Layouts.Read)]`,
  has no client-callable methods, joins only `fab:<x>` groups from the caller's assigned fabs, and every push goes to a fab group, never
  `Clients.All`. The CORS list only lets a page at that origin read a response to a request that already carries a valid token. Both new
  entries are set inside `AddWebApplications`, which runs only under `isRunMode && !isE2ETests`; the `Publish` and `IntegrationFixture`
  snapshots, untouched, show the origin absent there. No token is exposed anywhere new, and no guard was weakened.
- **Infra and composition.** No start-order cycle (the only wait edges remain the kiosks waiting for `layout-composition`); omitting
  `WithReference`, `VITE_LAYOUT_HUB_ORIGIN` and `WaitFor` for the console is consistent with the file; the three snapshots are correct line
  for line and `Unparseable` is still byte-identical to `Developer`; no shard filter or CI file needs an entry; the new spec lands in
  `chromium` only.

| #   | Finding                                                                                                          | Disposition                                                                                                                                                                                                                                                                                                               |
| --- | ---------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| S1  | `docs/deployment-frontend-env.md` said live updates work "at either origin"; only `:5173` was observed           | **Fixed.** Now says "at its own port (the one origin is admitted too, but sign-in there is blocked by #3267)".                                                                                                                                                                                                            |
| S2  | The new origins assertion compares resource names only                                                           | **Accepted.** The snapshots pin the binding (`{management-web.bindings.http.url}`), so nothing is unguarded.                                                                                                                                                                                                              |
| I1  | The comment at `WebApplications.cs:31-34` says kiosks `WaitFor` for config-time resolution; the console has none | **Fixed.** The paragraph now says the kiosk `WaitFor` is ordering hygiene only (the endpoint is proxied) and that the console omits it on purpose because its hub client retries a failed first connect.                                                                                                                  |
| I2  | The spec does not prove the _push_ was delivered, as distinct from a late first `connected` re-reading the wall  | **Accepted as a known limit.** Nothing else refreshes that badge (no polling, no refetch-on-focus), and the second operator's sign-in takes tens of seconds, so the first page is connected long before the switch; the red run showed it never connects without the fix. A "live" indicator would pin it; that is #3268. |
| I3  | The spec's layouts (`E2E Console Live Scene …`) do not match the layout teardown's `DISPOSABLE` regex            | **Fixed.** Renamed `E2E Layout Console Live A/B …`, which matches `^E2E Layout `. Not re-run after the rename (strings only; `wallName` and the lookups are unchanged); prettier and eslint clean.                                                                                                                        |
| I4  | `WebApplications.cs` and the three snapshots are contention files                                                | **Noted for the PR body.**                                                                                                                                                                                                                                                                                                |

**What a green CI run will and will not prove** (infra reviewer, agreed): it catches the wiring text and, at `:5173`, the real behaviour
(the spec was observed red before the fix). It does not catch the one origin signed in (#3267), the kiosks on the live stack (not
observed, above), production or Helm injection for the console (none exists; #1015), or a flaky 5 s assertion absorbed by CI's
`retries: 2`; read the retry summary, not only the green.

After the review edits: `dotnet build -c Release` on the AppHost succeeded; the four test classes pass again, **32 of 32**.
