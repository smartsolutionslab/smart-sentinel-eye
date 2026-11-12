# Verification: The kiosk and the wall display on one origin

Written as observed, task by task. Stage 1 (T001-T004) only.

## T001 — rebase over spec 493 (merged as PR #3264)

Rebased onto `origin/develop`; no textual conflict in the spec docs. `.specify/feature.json` took develop's value
(`specs/493-gateway-edge-cases`) and was set back to `specs/494-kiosk-wall-single-origin` in the T001 commit.

Re-check of plan.md "What 494 rebases over" against 493's **merged** diff (`git diff fe07298c..origin/develop -- src apps tests`;
7 files, +291/-61):

| Plan row                                                  | Merged reality                                                                                                                                                                                                                  | Verdict                    |
| --------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| `MapShellFallback` in `UiHosting.cs`                      | **Gone.** The fallback moved into a new `src/ApiGateway/UiRoot.cs`: `record UiRoot(RequestPath, Directory, EntryFile)` with `ServeFiles(app)` and `MapFallback(app)`. `MapFallback` maps `RequestPath + "/{**path}"`.          | Differs (better than plan) |
| Static-file options (`StaticFiles` helper)                | **Gone** with its `PhysicalFileProvider` using; `UiRoot.ServeFiles` carries `RequestPath`, `nosniff`, and `no-cache` on `{RequestPath}/{EntryFile}` (the entry file of every root, `index.html` or `remoteEntry.js`).          | Differs                    |
| Fallback rule                                             | Claims a path when the request is a read (GET/HEAD) **and** (`Sec-Fetch-Dest` contains `document` **or** the last segment has no extension); `no-cache`, `nosniff`, `text/html; charset=utf-8`. Non-reads and other dotted paths: 404. | Holds (matches 493's spec) |
| `StaticUiStack`/`UiProxyStack` new required keys          | None added by 493. The fixtures still supply only Shell/Cameras/EventIngestion keys.                                                                                                                                             | Holds (no new keys)        |
| `streams/authorize` carve-out for every method            | `GatewayHost.cs`: `MapPost` became `Map` for both paths.                                                                                                                                                                        | Holds, untouched by 494    |
| `devProxy.ts` anchored keys, query string accepted        | `apps/management-web/src/app/devProxy.ts` (edited) and its test (+76).                                                                                                                                                        | Holds, untouched by 494    |
| Extended tests                                            | `GatewayUiStaticTests.cs` (+98) and `GatewayApiPrefixTests.cs` (+53). No change to `GatewayHostCharacterisationTests`. `GatewayNavigationRuleTests` already existed on develop.                                                  | Differs slightly           |
| Shard filter entries for new 493 classes                  | None: 493 added no test class, so `shard-*.filter` is unchanged and there is no conflict to resolve.                                                                                                                              | Differs                    |

Consequences for 494:

- Plan D5 ("reuse the fallback, prefix-taking helper") is **already satisfied**: `UiRoot` is per-root and takes the prefix. T003 is a
  no-op (below). Kiosk and wall become two more `UiRoot` values (`/kiosk`, `/wall`) with entry file `index.html`.
- A prefixed `MapFallback("/kiosk/{**path}")` is a route like any other; the plan's risk "the `/kiosk` redirect endpoint loses to the YARP
  `/kiosk/{**catch-all}` route" is unchanged and still pinned by the red test in W3.
- **`iframe` is not a navigation.** `IsPageNavigation` accepts `Sec-Fetch-Dest: document` only; a request with `iframe` (or `frame`)
  gets the extension rule instead, so a dotted deep link loaded inside a frame would 404. The kiosk and the wall are top-level
  documents: `grep -rniE iframe apps/kiosk-web/src apps/kiosk-web/*.html apps/shared` finds nothing, and the gateway has no framing
  code. The rule is therefore accepted unchanged for the `/kiosk/` and `/wall/` roots.
- Source comments: `UiRoot.cs` carries no spec/task numbers; 494 code follows suit.

## T002 — baseline on the rebased branch

`pnpm install --frozen-lockfile --prefer-offline` clean. One process at a time, no Aspire, no Docker.

| Suite                                                                                   | Result                                         |
| --------------------------------------------------------------------------------------- | ---------------------------------------------- |
| `apps/kiosk-web` vitest                                                                 | 39 files, 392 tests passed                     |
| `apps/shared` vitest                                                                    | 93 files, 1398 tests passed                    |
| `apps/kiosk-web` `tsc --noEmit` / `eslint src --max-warnings 0` / `prettier --check .`  | exit 0 / 0 / 0                                 |
| Repo root `pnpm test:guards`                                                            | exit 0, 350 tests, 350 pass, 0 fail            |
| Five gateway classes, Release (`GatewayHostCharacterisationTests`, `GatewayUiProxyTests`, `GatewayApiPrefixTests`, `GatewayNavigationRuleTests`, `GatewayUiStaticTests`) | 104 passed, 0 failed, 0 skipped |
| `tests/Architecture.Tests` Release                                                      | 3254 passed, 0 failed, 0 skipped               |

The CI baseline for the `kiosk`/`wall` Playwright projects and the three `layout-composition` push integration classes is read from
`develop`'s CI at PR time, not rerun here (needs Aspire).

## T003 — extract the fallback into a prefix-taking helper

**No-op.** 493's merged `UiRoot.MapFallback` is already the per-prefix helper this task asked for: it takes the route prefix
(`RequestPath`) and the entry file, and the shell calls it as `StaticRoots.Read(...).Shell.MapFallback(app)`. Extracting it again would
be an invented refactor. No source change and no commit for T003; the task is ticked on this note.

## T004 — gateway fixtures supply the four new keys

Owner-approved fixture-only edit (spec Q4). The gateway does not read `Ui:Proxy:Kiosk/Wall` or `Ui:Static:KioskRoot/WallRoot` yet, so
the edit must be a no-op for it. Observed: the same five gateway classes, Release, before and after: see below.
