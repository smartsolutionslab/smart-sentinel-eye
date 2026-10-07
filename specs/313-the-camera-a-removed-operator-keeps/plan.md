# Plan 313 — The camera a removed operator keeps

**Spec:** [spec.md](./spec.md) · **Issue:** #2750 · **Builds on:** spec 310 (`useRevocationFallback`)

## 1. Shape

Frontend only. Three production files, all already touched by spec 310. No bounded context, no
`Shared.Contracts`, no messaging, no AppHost.

### 1.1 `isNotFound` — `apps/shared/src/api/problemDetail.ts`, beside `isForbidden`

Same shape as `isForbidden` / `isConflict`: `typeof error === 'object' && error !== null &&
'status' in error && status === 404`. RTK's string statuses (`FETCH_ERROR`, `PARSING_ERROR`) never
match. Doc comment says a 404 by itself means nothing to the revocation fallback; it is read by
`useRevocationFallback` only, gated by a cached record (spec 313 FR-001/005).

### 1.2 `useRevocationFallback` — one optional field, one condition

`RevocationQueryState` gains an optional field:

```ts
/**
 * Opt-in (spec 313): true when this subject's cache entry already holds a
 * successfully loaded record AND the resource is never deleted, so a 404 can
 * only mean access was lost. Omit on list pages — a 404 there resets.
 */
notFoundRevokes?: boolean;
```

Strike rule (line 60):

```ts
strikes: isForbidden(query.error) || (query.notFoundRevokes === true && isNotFound(query.error))
  ? next.strikes + 1
  : 0,
```

No new hook state. The "prior 200" fact is not tracked by the hook — it is read from the cache by
the caller (see 1.3 and spec §2 edge case "Remount within 60 s" for why hook-held state is wrong).
The threshold, subject reset, `requestId` de-duplication and render-time adjustment are unchanged.
Update the hook's doc comment: "403" → "refusal (403, or 404 where `notFoundRevokes`)".

Field name: `notFoundRevokes` says what the boolean *does*; the page's comment says why it is true.

### 1.3 Page wiring — `CameraDetailPage.tsx:60`

```ts
const refused = useRevocationFallback(cameraIdentifier, {
  error, isFetching, requestId,
  notFoundRevokes: currentData !== undefined,
});
```

Comment (why, not what): cameras are never deleted and `currentData` is only ever this
identifier's cache entry, so a 404 while it holds a record means the operator lost the fab (spec
029 FR-006 answers fab refusals 404). Must be `currentData`, **not** `data` — `data` carries the
previous identifier's record across navigation and would make a fresh identifier's first-load 404
a strike.

The five list pages are not edited (spec FR-004).

## 2. Entities / invariants

- Strike ⇔ settled, new `requestId`, and (`403` or (`404` and `notFoundRevokes`)).
- `notFoundRevokes` false/omitted ⇒ behaviour identical to spec 310.
- One threshold (`REVOCATION_STRIKE_THRESHOLD = 3`) for both kinds.

## 3. Messaging

None.

## 4. Boundary rules

`apps/shared` exports, `apps/management-web` consumes — existing direction. Page reads no status
(spec 310 FR-005 kept). `isNotFound` is exported from `problemDetail.ts` but not re-exported
anywhere new; `OverlayEditPage.tsx`'s private copy is left alone (spec §8).

## 5. Tests (behaviour-changing → red first, ADR-0139)

| # | File (new unless noted) | Cases | Red today because |
|---|---|---|---|
| 1 | `apps/shared/src/api/problemDetail.test.ts` (append a `describe('isNotFound')`) | 404 → true; 403/401/409/503/400 → false; `FETCH_ERROR`/`PARSING_ERROR` → false; `null`/`undefined` → false | `isNotFound` not exported (run `tsc --noEmit` too — Vitest binds a missing named import to `undefined`) |
| 2 | `apps/shared/src/hooks/useRevocationFallbackNotFound.test.ts` | three 404s with `notFoundRevokes: true` → true; two → false; 403,404,403 → true; 404,404,503,404 → false; three 404s with `notFoundRevokes` false → false; three 404s with it omitted → false; 404s then 200 → false | 404 resets today |
| 3 | `apps/management-web/src/features/cameras/CameraDetailFabRemoval.test.tsx` — real `camerasApi` store, `fetch` stubbed, same mechanism as `CameraDetailRevocation.test.tsx` (first refusal via `invalidateTags`, then two real Retry clicks; settle on store `isLoading`, ADR-0150) | 200 then 404×3 → "No such camera", no viewer/controls/banner; 200 then 404×2 → record + banner; first-load 404 for a never-loaded GUID → "No such camera" (unchanged, fence) | 404 resets today; pins RTK's `currentData` retention on a rejected **404** refetch against the real library |

Rows 2–3 are new files so they do not collide with #2762 (open: flake investigation of
`CameraDetailRevocation.test.tsx` in worktree `sse-2762c`). Row 1 appends to an existing file no
open branch touches.

A list-page 404 case is not added: list pages are not edited and pass no `notFoundRevokes`; row 2's
"omitted → false" case is the contract they rely on.

## 6. Contention

- **#2762** (`chore/2762-reproduce-revocation-test-flake`, worktree `sse-2762c`) may edit
  `useRevocationFallback.ts` and/or `CameraDetailRevocation.test.tsx`. This plan edits the former
  (two lines + doc). If #2762 merges first, rebase; if #2762 finds a hook timing defect, row 3
  inherits the same settling approach it lands on.
- Spec number 313: re-check before PR.

## 7. Constitution / ADR check

- §II (value objects) — frontend, not applicable.
- ADR-0075 RTK Query: relies on documented `currentData` semantics, no store change.
- ADR-0089: status read only in `problemDetail.ts`.
- ADR-0139: red first, quoted in PR.
- ADR-0144: the 404 rule is a human decision recorded this session (spec §0); the lane makes none.
- No new ADR needed.
