# Implementation Plan: The test helpers every API test rebuilds live in one place

**Branch**: `468-shared-test-helpers` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/468-shared-test-helpers/spec.md`

## Summary

Add **one** test-support module (`jsonResponse`, `problemResponse`) to `apps/shared/src/test`, and
migrate existing tests to it; migrate hand-built RTK Query test stores to the **existing**
`createApiStore`. Test-only, behaviour-preserving, one app per PR. The issue proposed three new
helpers; research (below) found two of them unwarranted, and the spec was amended to say so.

## Technical Context

**Language/Version**: TypeScript, React 19.3.1, Vitest 5.0.5, RTK/RTK Query (ADR-0075)
**Primary Dependencies**: none new
**Storage**: N/A
**Testing**: Vitest + Testing Library (ADR-0052); each app has `src/test/setup.ts`
**Target Platform**: jsdom (tests only)
**Project Type**: frontend monorepo (`apps/*`), shared package `@smart-sentinel-eye/shared`
**Performance Goals**: none — no production path touched
**Constraints**: migrated assertions unmodified (FR-007); no production module changes (FR-010)
**Scale/Scope**: 22 `jsonResponse` + 6 `problemResponse` copies, 19 hand-built stores

## Constitution Check

*GATE: passes before research; re-checked after design.*

| Gate | Result |
|---|---|
| §Testing — behaviour-preserving: covering tests green before, unchanged after | The migrated files **are** the covering tests; each PR records them green on `develop` first (T-before). |
| §Testing — new behaviour: red first, failure quoted in PR | The new helper module is new behaviour: its test is written first, observed failing (module absent), quoted in PR 1. |
| §IV latency budget | No leg touched; test-only (stated in spec header). |
| §II primitives / DDD | Frontend only; N/A. |
| ADR-0036 smallest change, no speculative generality | The fetch-stub and store helpers the issue proposed are **dropped** for this reason (R2, R3). |
| ADR-0150 waiting | No wait is added or changed. |
| No ADR / constitution amendment | None needed; lane may not write one. |

Post-design re-check: unchanged. No violations to justify.

## Research (decisions that shaped the plan)

### R1 — The `Response` builders: new module, exact copy
22 of 23 `function jsonResponse(body: unknown, status = 200): Response` are byte-identical
(`new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })`);
`problemResponse(status)` is `jsonResponse({ title: 'PROBLEM', status }, status)` in 6. The shared
version is that exact code. **Decision:** new `apps/shared/src/test/responses.ts`, subpath
`./test/responses` in `apps/shared/package.json` (the `./test/focusRefetch` precedent, line 68).
**Divergent, kept:** `kiosk-web/.../CameraTile.test.tsx:153` (`jsonResponse(body)` with no status
parameter, plus a bodyless `errorResponse(status)`), and `shared/.../CameraViewerRefusal.test.tsx:90`
(`errorResponse(status, body)`, a different helper name and shape).

### R2 — Stores: reuse `createApiStore`, add nothing
`createApiStore(slices)` (`apps/shared/src/store/createApiStore.ts`) already wires every slice's
reducer and middleware and **prepends a `createListenerMiddleware()`** ahead of them (spec 314).
Every hand-built test store that prepends a listener (14 files) does so with a locally-created
middleware that is **never used beyond the prepend** (no `startListening`, no `addListener` on it).
So it is the same store. The ~5 that do not prepend gain one; that is a change in the dispatch chain,
so each migrated file is **run**, not assumed. Leaves alone: stores with a stand-in reducer
(`_unused: (state = 0) => state`, 2 files) and any file where the extra middleware changes an outcome.
**Decision:** no new store helper (FR-003 amended). `createApiStore.ts` is not modified; its own test
(`createApiStore.test.ts`) already covers the listener wiring and the dev-tools guard.

### R3 — Fetch stub: withdrawn
All 26 files that `vi.stubGlobal('fetch', …)` already call `vi.unstubAllGlobals`, so no stub leaks.
The 73 call sites build bespoke mocks (`vi.fn((request: Request) => …)` ×16, `vi.fn(() => …)` ×15,
`mockResolvedValue` chains) around a one-line `stubGlobal`. A helper would wrap the line it
replaces. **Decision:** none. Recorded so the next survey does not rediscover it.

### R4 — The four tests #3190 depends on are out
`Overlays|Layouts` `PageFocusRefetch` / `PageRetryRecovery` tests are the characterisation suite
#3190 requires **unmodified**. Migrating their local helper and store here would change that suite
under #3190's feet. They stay as they are; re-survey after #3190 lands.

## Project Structure

```text
apps/shared/
├── package.json                      # + "./test/responses": "./src/test/responses.ts"
└── src/test/
    ├── responses.ts                  # NEW  jsonResponse, problemResponse
    └── responses.test.ts             # NEW  written first, observed red
apps/{management-cameras,management-web,kiosk-web,management-event-ingestion,shared}/**/*.test.ts*
                                      # EDIT: local helper/store -> import; assertions untouched
```

No production file changes (FR-010). `src/index.ts` is not touched (FR-005).

## Delivery: three PRs, one app (or two small ones) each

| PR | Content | Files |
|---|---|---|
| 1 | `responses.ts` + test (**red first**) + subpath export; migrate **management-cameras** | 8 `jsonResponse` (+ `problemResponse`), 4 stores |
| 2 | **management-web**, excluding R4's four | 9 `jsonResponse`, 8 stores |
| 3 | **management-event-ingestion**, **kiosk-web** | 1 `jsonResponse` (event-ingestion); kiosk-web's `CameraTile` `jsonResponse` is divergent and kept, its 1 store migrates. `shared`'s 2 stores use a stand-in reducer and stay. |

PR 1 lands the module because a migration with no module cannot compile; it is also the smallest
useful app to prove the loop. Rebase every parked branch after each merge (CLAUDE.md).

## Verification per PR (what "unchanged" means, concretely)

1. **Before:** run the app's Vitest suite on `develop`; record the list of test names and the sorted
   list of assertion lines (`expect(`/`toHaveBeen…`/`toBe…`) of the files to be migrated.
2. **After:** same two lists, diffed — must be identical (SC-003, SC-005). A diff is a block, not an edit.
3. `prettier --check`, `eslint --max-warnings 0`, `tsc --noEmit` for the app (memory: eslint-clean is
   not prettier-clean), and the .NET architecture tests (they scan `.tsx`).
4. **Counterfactual (PR 1):** break the helper three ways — status default, `Content-Type`, body
   shape — and show `responses.test.ts` fails each time; quote the output.
5. **Store migration (per file):** run it; if the extra listener middleware changes an outcome, revert
   that file and comment why (FR-008 analogue).

## Risks

- **A "byte-identical" copy that differs by a header case or a trailing comma-position** is caught by
  the diff in step 2, not by eye.
- **Focus-refetch tests are order-sensitive** (`setupListeners` has module-global state, see
  `focusRefetch.ts`): run those files in isolation and in the full suite.
- **#3190 / #3197 collide** if either starts first on the same files: R4 keeps the page tests out;
  #3197 stays blocked on this spec's PR 3.
