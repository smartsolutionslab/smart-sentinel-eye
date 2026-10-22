# Plan 439 — The wall that knows its own credential

**Spec:** [spec.md](spec.md) · **Issue:** #2781 · **Base:** `b5579a60`

## 1. Shape

Frontend only (`apps/shared` + `apps/kiosk-web`). No bounded context, no contract, no AppHost, no Keycloak
realm change, no migration. No ADR: it reuses ADR-0131's grant and spec 051/205's renewer; the owner's decisions
on #2781 are the product record. One engineer: **frontend-engineer**.

## 2. Changes

| #   | File                                                                                | Change                                                                                                                                                                                                                                                                                                                                                              |
| --- | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| C1  | `apps/shared/src/ui/composites/useWhepSession.ts`                                   | On terminal refusal (`:364-371`) also call an optional `onRefused(kind: 'unauthorized' \| 'forbidden')`, held in a layout-effect ref like `getTokenRef` (ADR-0167). Status/message unchanged.                                                                                                                                                                       |
| C2  | `apps/shared/src/ui/composites/CameraViewer.tsx`                                    | Thread optional `onRefused` prop through to the hook.                                                                                                                                                                                                                                                                                                               |
| C3  | `apps/shared/src/api/gateway.ts`                                                    | `export const renewSession = (): Promise<string \| undefined> => renewSessionOnce();` — reuse the single-flight; no second renewal path.                                                                                                                                                                                                                            |
| C4  | `apps/kiosk-web/src/features/cell/wallCredential.ts` (new)                          | Pure `isWallCredentialRefusal(tiles, windowMilliseconds)` + `WALL_CREDENTIAL_WINDOW_MILLISECONDS = 10_000`, `MINIMUM_TILES = 2`, the two message constants.                                                                                                                                                                                                         |
| C5  | `apps/kiosk-web/src/features/cell/useWallCredential.ts` (new)                       | State machine `idle → renewing → (recovered \| refused)`, latched `attempted` ref (one attempt per episode; `reportLive` ends the episode). Inputs: mounted tile keys, `onRefused` reports with `performance.now()`, the current access token. Recovered → bump `credentialGeneration`. Logs `wall-credential→renewing/recovered/refused` via `logResilienceEvent`. |
| C6  | `apps/kiosk-web/src/features/cell/WallCredentialNotice.tsx` (new)                   | Dark panel, `bg-bg-base`/`text-fg-muted`, `role="status"` (renewing) / `role="alert"` (refused), `data-testid="wall-credential-state"`, no animation/transition classes.                                                                                                                                                                                            |
| C7  | `apps/kiosk-web/src/features/cell/LayoutGrid.tsx` (+ `CameraTile.tsx` pass-through) | Wire tile `onRefused`, prune unmounted keys, render the notice, append `credentialGeneration` to each `Tile` key so a recovery remounts refused tiles.                                                                                                                                                                                                              |

## 3. Invariants

- I1 A 403 never enters the set; a tile whose last refusal is 403 makes (a) false.
- I2 Entry requires `tiles.length ≥ 2` **and** every mounted tile refused 401 **and** `max − min ≤ 10 000`.
- I3 `renewSession` is called at most once per `LayoutGrid` mount (latched before the await).
- I4 After `recovered`, the machine is spent: later 401s are per-tile only (spec A2).
- I5 `refused` clears only when the access token changes (spec A3), remounting once.
- I6 No change to `App.tsx` precedence; `NotAuthorizedScreen` still wins (spec A1).

## 4. Messaging

None. In-process React state; observability via the existing `logResilienceEvent('session'|'stream', …)`.

## 5. Tests (phase 4a: red)

- `wallCredential.test.ts` — 1 tile → false; 2×401 at 0/10 000 → true; 0/10 001 → false; 401+403 → false;
  2×403 → false; 2×401 + 1 live/connecting/offline → false; unmounting the non-refused tile → true.
- `useWallCredential.test.ts` — renewer called once on entry; a second burst after `refused` → still once;
  success → generation +1 and state idle; `undefined` → refused text; post-recovery 401 burst → idle, no call;
  a token change from `refused` → idle + generation +1; a tile mounting later blocks entry until it 401s.
- `WallCredentialNotice.test.tsx` — exact strings for both states; class list carries no `animate-`,
  `transition-`, or bright fill (`bg-white`, `bg-status-*`, `bg-accent*`).
- `CameraViewerRefusal.test.tsx` (extend, existing cases unmodified) — `onRefused('unauthorized')` on 401,
  `('forbidden')` on 403, never on `stream-unavailable` or network.
- `gateway` test — `renewSession` concurrently twice → one renewer call.
- `App.test.tsx` — wall-level state entered then `invalid_grant` → `identity-not-authorized`, no notice.
- `e2e/kiosk-credential-refused.spec.ts` — spec §"Independent end-to-end test"; plus a mixed 401/403 variant
  asserting the 403 tile's per-tile text and no notice.
- Guards unmodified: `MotionLanguageTests`, `WallStatusChipTests`, `CameraViewerRefusal` existing cases.

Counterfactuals (each run once, quoted in the PR): drop `≥ 2` → 1-tile test red; `≤` → `<` → 10 000 ms test red;
count `forbidden` → mixed test red; remove the latch → "once" test red; add `animate-pulse` to the notice →
notice test and `MotionLanguageTests` red.

## 6. Latency — composite + render (≤ 50 ms), §IV

`render-leg-gate` is report-only (`ci.yml:1077`). Per spec 297 plan V3: scratch baseline from the latest nine
`develop` push runs' `render-leg-attempt-*.json` (`scripts/render-leg-check.mjs` schema, mean / 3σ), download
`playwright-report-*-of-4` before any re-run, three PR records, `node scripts/render-leg-check.mjs` each; all three
`within tolerance` passes. Record p50/p95/max/T, run ids and SHAs in `verification.md`. `regressed` → ADR-0123
triage (T first); a second `regressed` or `unmeasured` blocks.

## 7. Risks

| #   | Risk                                                                  | Mitigation                                                                                              |
| --- | --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| R1  | Remount restarts alignment/overlay subscriptions for the tile subtree | Only on recovery, once; e2e asserts tiles reach Live                                                    |
| R2  | Renewal stampede with the RTK 401 path                                | Same single-flight (C3); `kiosk-identity-herd.spec.ts` unchanged and green                              |
| R3  | Recoverable IdP outage shows "re-enrol"                               | Closed by review: no token keeps the first state; the second needs a positive provider answer (spec A2) |
