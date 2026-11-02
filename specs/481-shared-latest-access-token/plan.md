# Implementation Plan: One latest-access-token getter, shared

**Branch**: `481-shared-latest-access-token` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/481-shared-latest-access-token/spec.md`

## Summary

Replace four copies of the stable `getToken`-over-a-ref (three hooks and one inline block) with **one** hook,
`useLatestAccessToken(accessToken)`, in `apps/shared/src/hooks/`, exported from the `hooks` barrel. **First probe, then pin, then
move**: break each concern once in today's code (the identity, the call-time read of the newest token, `undefined → null`, the
first-render value), see whether the existing token tests notice, and write characterisation tests in **new files** for what they do
not. Then add the shared hook (with its own unit test) and switch the four sites one per commit. Behaviour-preserving, so
**characterisation** (ADR-0139/0144 phase 4a). Same method as specs 476–480.

**Sizing, stated before building.** A deduplication: unlike the decompositions of 476–480 the file total should **fall** (three hook
files and one inline block go; one hook and its test arrive). Reported as measured (SC-005).

## Technical Context

- **Stack**: TypeScript, React 19.3, Vitest + Testing Library (jsdom). `apps/shared` (`@smart-sentinel-eye/shared`, `./hooks` already
  an export), `apps/kiosk-web`, `apps/management-web`, `apps/management-cameras`.
- **Baseline (2026-11-02, `develop` @ `bc7ac397`)**: four getter copies; four token-ref `react-hooks/refs` suppressions; token tests
  per site: `LayoutGridIdentity` (kiosk), `LayoutEditorDialogToken` and `OverlayDraftFormGetToken` (management-web),
  `CameraDetailPage.test` (management-cameras).
- **Touches**: one new hook and test in `apps/shared/src/hooks/` plus the barrel line; four call sites; three deleted files; new test
  files for the gaps. No existing test file, no config.
- **Latency budget (§IV)**: the getter's identity is what keeps the tile overlay-draw measurement, the decode sampler and the hub
  connect effects from rebuilding (issues 1888, 1889). **Every commit states the legs it touches and that they are unchanged**
  (FR-008).

## Constitution Check

| Principle | Verdict |
|---|---|
| §Testing — refactors stay green | Characterisation: existing suites unmodified, plus gap tests written first and seen green on today's code. |
| §IV latency budget is sacred | Identity kept exactly: one `useCallback` with `[]`, the newest token behind a ref assigned in render (the one suppression, kept). No new state, effect or subscription. |
| ADR-0036 — smallest change, no speculative generality | One hook with the one parameter all four sites need. The two ref-only sites and `useSubmitGuard` are not touched. |
| #2118 — the `react-hooks/refs` judgement | Kept, isolated in one module, rule and justification unchanged; none added. |

## Decisions

**D1. The hook takes the token value; it does not call `useAuth()`.** `useLatestAccessToken(accessToken: string | undefined)`.
Alternatives: (a) call `useAuth()` inside, as sites 2 and 3 do: changes site 4's tolerance (`auth.user?.` would become `auth?.user?.`),
makes the kiosk site read a provider it does not need to, and couples `apps/shared`'s hooks to the OIDC provider for one caller shape;
(b) two hooks (one pure, one wrapping `useAuth`): speculative second abstraction for three call sites that differ by one `?.`. Chosen:
the value form (site 1's shape). Each caller keeps its own `auth?.user?.access_token` / `auth.user?.access_token` exactly as written
today, so each tolerance stays where it is.

**D2. Location and export.** `apps/shared/src/hooks/useLatestAccessToken.ts`, exported from `apps/shared/src/hooks/index.ts`. The
barrel's rule (do not re-export a module that reads `VITE_API_GATEWAY_URL` at load) is met: the hook imports only React.
`apps/kiosk-web` does not import the hooks barrel today; the barrel also carries `useRevocationFallback`/`useAssignedFabs`
(Redux, `react-oidc-context`), both already dependencies of the kiosk app, so importing the barrel there is allowed. **Check at T-time:**
kiosk's bundle still resolves it and `tsc`/vitest are clean; if the barrel is a problem for the kiosk, a subpath export is the
fallback (as `useCameraStream` does), decided with evidence.

**D3. One comment, all reasons.** The shared hook carries one consolidated comment: why a stable identity (the overlay-draw
measurement and decode sampler rebuild per render otherwise, 1888/1889; a WHEP session reconnects), why `[]` and not the token value
(a silent renew would restart them), and why the render-time ref assignment is deliberate (#2118). Call sites keep at most a
one-line pointer where the old comment named a reason local to them (e.g. `CameraViewer`'s sampler).

**D4. Order: pin, add, switch, delete.** The shared hook is added **before** any site moves, so each switch commit is a pure
substitution. Old copies are deleted in the commit that switches their last user.

## Phases and commits

| Step | Commit | Legs touched |
|---|---|---|
| T-probe | Break each concern in the four copies (named, exactly-once substitutions, `probe481.cjs`); run the four token tests; record the matrix in `verification.md` | none |
| T-gap | Gap tests in **new** files for what nothing noticed, green on today's code, proved able to fail (message read) | none |
| 1 | Add `useLatestAccessToken` (shared) with its own unit test and the barrel export; counterfactual on it | none (unused) |
| 2 | Kiosk `LayoutGrid` switches; delete `layoutGrid/useLatestAccessToken.ts`; `LayoutGridIdentity` etc. re-run unchanged | overlay-draw, decode sampler, composite + render: unchanged |
| 3 | `LayoutEditorDialog` switches; delete its hook | token handed to the overlay editor/WHEP session: unchanged |
| 4 | `OverlayDraftForm` switches; delete its hook | same |
| 5 | `CameraDetailPage` switches; inline block removed; `CameraViewerTimers` and the page identity test re-run unchanged | decode sampler: unchanged |
| 6 | Verification note, tick tasks, QA dispositions | none |

After each switch, the matching break is applied to the **shared** hook and the same site test must fail there (counterfactual,
SC-004). New untracked files are staged before counterfactuals.

**Measure-and-ask (T-D1)?** Not needed in the usual form: a deduplication either removes the copies or it does not. If a site cannot
switch without an edited assertion, that is a **block**, reported, not adjusted.

## Risks and how they are covered

- **A site's tolerance changes** (`auth` undefined in tests): D1 keeps it at the call site; the two management-web "no auth provider"
  tests are the net.
- **Identity regresses to per-render**: pinned by `LayoutGridIdentity`, `OverlayDraftFormGetToken`, the page identity test and a new
  shared unit test; probed by breaking the dependency array.
- **Call-time read becomes render-time capture**: probed (assign the ref once instead of per render); gap test if unnoticed.
- **Barrel import side effects in kiosk**: D2 check.

## Project Structure

```text
apps/shared/src/hooks/useLatestAccessToken.ts         (new)
apps/shared/src/hooks/useLatestAccessToken.test.tsx   (new)
apps/shared/src/hooks/index.ts                        (one export)
apps/kiosk-web/src/features/cell/LayoutGrid.tsx       (import)
apps/management-web/src/features/layouts/LayoutEditorDialog.tsx
apps/management-web/src/features/overlays/OverlayDraftForm.tsx
apps/management-cameras/src/features/cameras/CameraDetailPage.tsx
  deleted: kiosk layoutGrid/, management-web layoutEditor/ and overlayDraftForm/ useLatestAccessToken.ts
```
