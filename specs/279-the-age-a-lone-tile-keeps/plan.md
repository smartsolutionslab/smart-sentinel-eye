# Plan 279

## Files touched

- `apps/kiosk-web/src/features/cell/useWallAlignment.ts` — add the always-on pruning effect; tighten
  `frameAgeFor`'s doc comment to say the aging is independent of tile count.
- `apps/kiosk-web/src/features/cell/useWallAlignment.test.ts` — one new fact.

## Sequence

1. Red: add the new fact against the current implementation, run it, capture the failure verbatim.
2. Implement: add the second `useEffect`/interval, unconditional.
3. Green: re-run the new fact plus the full file's suite.
4. Doc comment: tighten `frameAgeFor`'s comment for precision (no longer "on the settle cycle" as the
   only mechanism).
5. Self-review the diff against §3's out-of-scope list before opening the PR.

## Verification

- `pnpm --filter ./apps/kiosk-web test useWallAlignment` — full file, twice (red capture, then green).
- `pnpm --filter ./apps/kiosk-web typecheck`.
- No live Aspire stack needed — this is a pure frontend hook with fake timers, no network.
