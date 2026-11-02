# Spec 481 — One latest-access-token getter, shared

**Feature Branch**: `481-shared-latest-access-token`
**Created**: 2026-11-02
**Status**: Draft
**Issue:** #3222 (feature-level; follow-up recorded by the frontend refactoring survey, #3195 / spec 480 `verification.md`).
**ADRs:** ADR-0036 (smallest change, no speculative generality), ADR-0075 (RTK Query), ADR-0139 / ADR-0144 (phase 4a), ADR-0037 (phases).
**Refs:** #2118 (the `react-hooks/refs` judgement: kept), issues 1888 / 1889 (the failures the identity prevents).
**No new ADR.** A deduplication inside existing conventions.
**Latency budget (constitution §IV): ON the kiosk path, so the claim is made per leg.** `LayoutGrid` hands `getToken` to every tile; the
tile's **overlay-draw** measurement (ADR-0123), the **decode sampler** (SFU → kiosk decode) and the hub-connect effects key on its
identity. If a fresh function appeared per render, those effects would rebuild on every render: the draw measurement would time
renders that changed nothing, and the sampler's interval would be cleared before a second sample (issues 1888, 1889). The change moves
the getter into one module; it must keep **the identity exactly** (created once per mount, `[]` dependencies, newest token read from a
ref) and add no state, effect, subscription or render-time work. Every commit states the legs touched and that they are unchanged.
**Phase 4a colour:** behaviour-preserving → **characterisation**. Existing suites pass **unmodified**; an assertion that has to be
edited means behaviour moved: block (CLAUDE.md). A refactor that is also a bug fix is two issues.

## Problem

The "stable `getToken` over a ref" exists **four times** (premise check on `develop` @ `bc7ac397`, 2026-11-02; the issue said four and
that is right):

| # | Site | Reads the token from | Shape |
|---|---|---|---|
| 1 | `apps/kiosk-web/src/features/cell/layoutGrid/useLatestAccessToken.ts` | a parameter `accessToken: string \| undefined` (caller passes `auth.user?.access_token`) | hook |
| 2 | `apps/management-web/src/features/layouts/layoutEditor/useLatestAccessToken.ts` | `useAuth()` inside, `auth?.user?.access_token` | hook |
| 3 | `apps/management-web/src/features/overlays/overlayDraftForm/useLatestAccessToken.ts` | `useAuth()` inside, `auth?.user?.access_token` | hook |
| 4 | `apps/management-cameras/src/features/cameras/CameraDetailPage.tsx` (inline) | `useAuth()` in the page, `auth.user?.access_token` (no `auth?.`) | inline ref + `useCallback` |

Each carries the identical body (`useRef(token)`, a render-time assignment under one `eslint-disable-next-line react-hooks/refs`,
`useCallback(() => Promise.resolve(ref.current ?? null), [])`) and its own copy of the reasoning. Two further sites (`WallDetailPage`,
`WallPage`) keep the same ref assignment under the same suppression but do **not** build a getter: they read the ref inside a hub
`accessTokenFactory`. Different shape (`?? ''`, not a promise, not handed to a child), so they are **out of scope**.

### What the issue does not settle

1. **The sites differ in one real way**: the management-web hooks (2, 3) call `useAuth()` themselves and tolerate `undefined` (tests
   mount with no `<AuthProvider>`, where the real `useAuth()` warns and returns `undefined`); site 4 does `auth.user?.` and the kiosk
   hook (1) never sees `auth`. A shared hook that takes the **token value** (site 1's shape) covers all four and leaves the
   `auth?.` / `auth.` tolerance exactly where it is, at each call site. The plan decides; a shared hook that calls `useAuth()` itself
   would change site 4's tolerance and couple `apps/shared` to the provider.
2. **`apps/shared/src/hooks/index.ts` has a rule**: do not re-export a module that reads `VITE_API_GATEWAY_URL` at load. The new hook
   imports only React, so the barrel is safe; the plan states it.
3. **Suppression count**: the four getters hold three suppressions as hooks plus one inline; after the change there is one. Measured,
   not assumed (SC-003). Nothing is added.
4. **The comments are evidence.** Each copy records a different real failure (kiosk: 1888/1889 tile effects; site 3: the WHEP session
   reconnect; site 4: the decode sampler). The shared hook keeps one consolidated comment that loses none of the *reasons*.

## Scope

**In:** one hook in `apps/shared/src/hooks/`, exported through the barrel (`@smart-sentinel-eye/shared/hooks`); sites 1–4 switched to
it; the three hook files deleted; site 4's inline block removed.

**Out (explicitly):** `WallDetailPage` / `WallPage` ref-only sites; `useWallCredential`; `useWhepSession`'s own guard; any change to
what the token is, when it is renewed or who calls `getToken`; `useSubmitGuard` (#3223, a different follow-up whose premise needs
checking: `apps/shared/src/hooks/useFormDialog.ts` already exports one); any new abstraction beyond the one hook.

## User Scenarios & Testing

### User Story 1 (P1) — Every caller behaves exactly as before

As the wall, the layout editor, the overlay form and the camera page, I get a `getToken` that keeps one identity for my whole life and
yields the newest token, so no measurement, sampler or session restarts on a silent renew. As the maintainer I can prove it.

**Independent test:** the existing identity/token tests pass with no line edited.

```gherkin
Scenario: getToken keeps its identity across renders and a token renew
  Given a component using the shared hook
  When it re-renders with a new access token
  Then the returned function is the same reference
  And calling it yields the new token

Scenario: no token yields null, not a throw
  Given the token is undefined (signed out, or no AuthProvider in a test)
  When getToken is called
  Then it resolves null

Scenario: the existing callers are unchanged
  Given LayoutGridIdentity, LayoutEditorDialogToken, OverlayDraftFormGetToken and CameraDetailPage tests
  When they are run after each switch
  Then every test passes and git diff shows none of them edited
```

### User Story 2 (P2) — One copy to maintain

```gherkin
Scenario: one implementation
  When the change is done
  Then the getter body exists in exactly one module
  And the react-hooks/refs suppression for it exists once
```

### Edge cases

- **Hook order and call position** at each site is unchanged (a hook replaces the lines it covers).
- **`auth` undefined** in management-web tests: the call sites keep `auth?.user?.access_token`.
- **Initial value**: the first render's ref holds the token passed on that render, as today.
- **Renew between render and call**: `getToken()` reads the ref at call time, so a call after a renew sees the new token.

## Requirements

- **FR-001** Every existing test passes after each commit; none is modified.
- **FR-002** One hook in `apps/shared/src/hooks/` is the only implementation of the stable latest-token getter; it is exported from
  the `hooks` barrel.
- **FR-003** Sites 1–4 call it; their local copies are removed.
- **FR-004** Identity and value semantics are unchanged: created once per mount (empty dependencies), newest token read from a ref at
  call time, `undefined` resolves `null`.
- **FR-005** The token-ref `react-hooks/refs` suppressions go from four (three hooks plus the inline block) to one; none is added;
  rule and the reasons stay.
- **FR-006** Each commit builds and passes the affected suites on its own (bisect); one site per commit.
- **FR-007** `eslint --max-warnings 0`, `tsc --noEmit`, `prettier --check` clean for `shared`, `kiosk-web`, `management-web`,
  `management-cameras`; `Architecture.Tests` pass (they scan `.tsx`).
- **FR-008** **Latency (§IV).** Each commit states the legs touched and that they are unchanged; `LayoutGridIdentity` and
  `CameraViewerTimers` are re-run unchanged after the kiosk and camera switches.
- **FR-009** No change outside `apps/shared/src/hooks/`, the four sites and their now-unused imports, `specs/481-…`,
  `.specify/feature.json`.

## Success Criteria

- **SC-001** The covering suites: same test names before and after, all passing, `git diff` on them empty.
- **SC-002** The getter body appears once (`grep` for `accessTokenRef.current ?? null` finds one hit in non-test code).
- **SC-003** Token-ref `eslint-disable` for `react-hooks/refs`: four before, one after, counted by search of the four apps.
- **SC-004** For each concern (identity, newest-token-at-call, undefined → null, first-render value), a deliberate break is noticed by
  a named test; where nothing notices, a new test in a **new file** is written first, green on today's code, proved able to fail (the
  message read), and kept; equivalents and jointly defended behaviours are recorded with evidence.
- **SC-005** Line counts reported before/after for the touched files, with the total.

## Assumptions

- Passing the token value to the hook is enough for all four sites (decided in the plan; the alternative couples shared to the OIDC
  provider and changes site 4's tolerance).
- The existing token tests cover identity per site (`LayoutGridIdentity`, `LayoutEditorDialogToken`, `OverlayDraftFormGetToken`,
  `CameraDetailPage.test`); the probes decide where they do not (likely: the `?? null` branch and the newest-at-call-time read).

## Dependencies

Independent of the other follow-ups (#3223–#3226). Builds on the extractions of specs 477–480, which created three of the four copies.
