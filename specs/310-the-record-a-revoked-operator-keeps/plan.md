# Plan 310 — The record a revoked operator keeps

**Spec:** [spec.md](./spec.md) · **Issue:** #2725 · **Apps:** `apps/shared`, `apps/management-web`
**Backend / infra:** none. **Bounded context:** none (browser only; no `Shared.Contracts` change).

## 1. Shape

Three layers, each knowing only what it must:

| Layer | File | Knows |
|---|---|---|
| Error envelope | `apps/shared/src/api/problemDetail.ts` (edit) | That a status is 403. |
| Count | `apps/shared/src/hooks/useRevocationFallback.ts` (new) | Settled responses per subject, and the threshold. Never renders. |
| Page | six `*Page.tsx` (edit, one line of wiring each) | A boolean. Never sees a status. |

### 1.1 `isForbidden` — beside `isConflict`

```ts
/** True when the server answered 403. Read by useRevocationFallback only; a page must not call it (spec 030 FR-008). */
export function isForbidden(error: unknown): boolean
```

Same shape as `isConflict` (`typeof error === 'object' && error !== null && 'status' in error &&
status === 403`). RTK's `FETCH_ERROR` / `PARSING_ERROR` string statuses are therefore false.
Not added to `api/index.ts` — that barrel does not re-export `problemDetail.ts` today.

### 1.2 `useRevocationFallback`

```ts
export const REVOCATION_STRIKE_THRESHOLD = 3;

export interface RevocationQueryState {
  error: unknown;
  isFetching: boolean;
  requestId: string | undefined;
}

/** True once REVOCATION_STRIKE_THRESHOLD consecutive settled responses for `subject` were 403. */
export function useRevocationFallback(subject: string, query: RevocationQueryState): boolean
```

State: one `useState<{ subject: string; counted: string | undefined; strikes: number }>`.
Updated **during render** (React's "adjusting state when a prop changes" pattern, the same one
`CamerasPage.tsx:44-48` uses for `lastFragment`) — no effect, so StrictMode cannot double-count
and the boolean is correct in the same commit as the response.

Per render:

1. If `subject !== state.subject` → next = `{ subject, counted: undefined, strikes: 0 }`.
2. A response is **settled and new** when `!isFetching && requestId !== undefined &&
   requestId !== next.counted`. Then `strikes = isForbidden(error) ? strikes + 1 : 0` and
   `counted = requestId`.
3. If next differs from state → `setState(next)`.
4. Return `next.strikes >= REVOCATION_STRIKE_THRESHOLD`.

Why `requestId`: RTK 2.12 assigns a new `requestId` to the cache entry at `pending` and keeps it
through `fulfilled`/`rejected`, so "not fetching, and an id not yet counted" is exactly "one new
response". A `requestId` of `undefined` (the existing page-test doubles) never counts, so every
existing page test renders unchanged. **Phase 4a must prove this against real RTK, not only
against hand-built results** — T006.

Export from `apps/shared/src/hooks/index.ts` (the `./hooks` package export already exists).

### 1.3 Page wiring

Each page adds `requestId` to its destructure, calls the hook, and masks its data. Everything
downstream keeps reading the same variable, so the refused render **is** the existing
no-data-plus-error render (FR-004) by construction, not by imitation.

| Page | Subject | Change |
|---|---|---|
| `cameras/CameraDetailPage.tsx` | `cameraIdentifier` | `const refused = useRevocationFallback(cameraIdentifier, { error, isFetching, requestId });` then `const record = refused ? undefined : error !== undefined ? currentData : camera;` → the existing `record === undefined` branch renders "No such camera"; viewer and the three dialogs unmount. |
| `cameras/CamerasPage.tsx` | `JSON.stringify(listArgs)` | Hoist the arg object literal into `listArgs`; rename `data` → `fetched`; `const data = refused ? undefined : fetched;` |
| `audit/AuditPage.tsx` | `JSON.stringify(applied)` | Same `fetched` / `data` mask. |
| `layouts/LayoutsPage.tsx` | `'layouts'` | Same mask (query arg is `undefined`, so the subject is a literal). |
| `overlays/OverlaysPage.tsx` | `'overlays'` | Same mask. |
| `systemVariables/SystemVariablesPage.tsx` | `JSON.stringify(variablesArgs)` | Hoist the ternary arg into `variablesArgs`; same mask. |

The `RetryBanner` stays on every list page (gated on `error`, unchanged), so a refused list reads
"Could not load …" with no rows — what a first-load 403 shows today. On the detail page the
banner is inside the record branch, so it disappears with the record, matching a first-load
refusal.

"Per camera identifier" (the decision's words) generalises to "per query argument set" on list
pages: a different argument set is a different RTK cache entry with no stale data of its own.

## 2. Entities / invariants

No domain model. Invariants of the hook:

- strikes ∈ ℕ; reset to 0 by any settled non-403 and by a subject change.
- A given `requestId` contributes at most one strike.
- `refused ⇔ strikes ≥ 3`.

## 3. Messaging

None. No domain or integration event; no SignalR.

## 4. Boundary rules

- `apps/shared` stays app-agnostic: the hook takes plain fields, not a slice or an endpoint.
- Status inspection stays in `problemDetail.ts` (the documented envelope layer). A page that
  imports `isForbidden` violates FR-005; phase 6 checks with
  `grep -rn "error\.status\|isForbidden" apps/management-web/src/features --include=*Page.tsx`
  (empty on `3f209556`; `record.status` is a camera field, not an error).
- No change to `gateway.ts`, `store.ts`, or any API slice.

## 5. Tests (behaviour-changing → red first, ADR-0139)

| # | File | Proves | Expected red |
|---|---|---|---|
| 1 | `apps/shared/src/api/problemDetail.test.ts` (add cases) | `isForbidden`: 403 true; 401, 404, 409, 503, `'FETCH_ERROR'`, `undefined`, `null` false. | `isForbidden` not exported. |
| 2 | `apps/shared/src/hooks/useRevocationFallback.test.ts` (new, `renderHook` + `rerender`) | 3×403 → true; 2×403 → false; 403,403,503,403 → false; 3×403 then 200 → false; same `requestId` re-rendered 3× counts once; `isFetching: true` never counts; `requestId: undefined` never counts; subject A's two strikes do not carry to B. | Module missing (helper — acceptable; tests 3-5 carry the behavioural red). |
| 3 | `cameras/CameraDetailPage.test.tsx` (add a `describe`) | Drive the existing `getCamera` mock through r1-r3 403 with `currentData: camera` via `rerender`: "No such camera" heading; no name / RTSP URL / viewer / Rename / Retire; `innerHTML` equals a first-load 404 render. And r1-r2 only → record still shown. | Record still rendered after r3. |
| 4 | `CamerasPage.test.tsx`, `AuditPage.test.tsx`, `LayoutsPage.test.tsx`, `OverlaysPage.test.tsx`, `SystemVariablesPage.test.tsx` (add one `describe` each) | r1-r3 403 with stale `data`: stale row text absent, failure banner present; r1-r2 → rows present. | Stale rows still rendered. |
| 5 | `apps/management-web/src/features/cameras/CameraDetailRevocation.test.tsx` (new) | **Real** `store` + `camerasApi`, `fetch` stubbed (`vi.stubGlobal`) as 200 then 403×3; operator presses Retry three times; `findByRole('heading', { name: /no such camera/i })` (ADR-0150 — no tick counts). Pins §1.2's `requestId` / `isFetching` assumption against RTK 2.12. Setup mirrors `src/app/staleBearerRetry.test.tsx`. | Record still rendered after the third Retry. |

Existing assertions are **not edited**. If any existing test breaks, that is evidence the
behaviour moved somewhere unintended — block, don't adjust.

No Playwright spec: revocation needs Keycloak role surgery mid-session; the real-RTK test (5) plus
phase 5's browser observation (spec §6) cover the end-to-end claim.

## 6. Contention

Every edited file is owned by this slice; `gh pr list` showed no open PR touching them
(2026-10-07). Single frontend engineer — the six page edits depend on the hook and are each one
line, so fan-out would cost more than it saves.

## 7. Constitution / ADR check

- §II (primitives in the domain): N/A — no domain model.
- §IV latency: N/A.
- ADR-0075 RTK Query: consumed, not altered.
- ADR-0089: status read in the envelope helper, as `isConflict` already does.
- ADR-0144: the threshold and reset rule are the human's; the 404 and passive-tab gaps are recorded
  for follow-up, not decided.
- No new ADR.
