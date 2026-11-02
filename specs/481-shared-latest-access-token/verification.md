# Verification 481 — One latest-access-token getter, shared

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **On the kiosk path (§IV)**, so the evidence includes the
per-commit leg statements. Not observed on a running wall; the evidence is the existing token tests plus nine new ones under jsdom.

## Size and suppressions, as measured (base `bc7ac397` vs the branch)

|                                                                                         | Before                                      | After                                                   |
| --------------------------------------------------------------------------------------- | ------------------------------------------- | ------------------------------------------------------- |
| Stable-getter bodies (`accessTokenRef.current ?? null`, non-test)                       | **4** (3 hooks + `CameraDetailPage` inline) | **1** (`apps/shared/src/hooks/useLatestAccessToken.ts`) |
| Token-ref `react-hooks/refs` suppressions in getters                                    | 4                                           | **1**                                                   |
| Same suppression at the two ref-only sites (`WallPage`, `WallDetailPage`, out of scope) | 2                                           | 2 (untouched)                                           |
| Production lines: 4 call sites + 3 hook files (before) / + shared hook (after)          | 947                                         | **925** (−22)                                           |

Call sites: `LayoutGrid` 185 → 185, `LayoutEditorDialog` 174 → 177, `OverlayDraftForm` 295 → 298, `CameraDetailPage` 240 → 226; the shared
hook is 39 lines (one consolidated comment carrying every reason of the four). The two management-web dialogs grew by three lines each
because they now call `useAuth()` themselves (D1). A first count of the getter bodies excluded any path containing "test" and so hid
every `useLatestAccessToken` file ("Lat-**est**"); it was redone against the base commit with `--exclude=*.test.*`.

## Characterisation (FR-001)

`git diff --name-status bc7ac397..HEAD -- apps | grep '\.test\.'` lists **five added files and no modified one**. Suites, unmodified
tests plus the new files: `shared` **1390** (1387 + 3), `kiosk-web` **392** (390 + 2), `management-cameras` **151** (+1),
`management-web` **731** (of which 4 new); all passing. `eslint src --max-warnings 0`, `tsc --noEmit` clean for the four apps;
`npm run format:check` clean for the repo; `Architecture.Tests` **3254 / 3254**.

## Probing before moving: 13 of 28 deliberate breaks went unnoticed (46 %)

Seven named, exactly-once substitutions per copy (`probe481.cjs`), the site's suite run, the file restored from git:

| Break                       | K (kiosk)          | L (`LayoutEditorDialog`) | O (`OverlayDraftForm`) | C (`CameraDetailPage`) |
| --------------------------- | ------------------ | ------------------------ | ---------------------- | ---------------------- |
| B1 deps keyed on the token  | noticed            | **none**                 | noticed                | noticed                |
| B2 no `useCallback`         | noticed (18 tests) | **none**                 | noticed                | noticed                |
| B3 no per-render assignment | noticed            | noticed                  | noticed                | noticed                |
| B4 `?? ''`                  | **none**           | noticed                  | **none**               | **none**               |
| B5 `?? undefined`           | **none**           | noticed                  | **none**               | **none**               |
| B6 initial ref `undefined`  | none               | none                     | none                   | none                   |
| B7 non-promise return       | noticed            | **none**                 | noticed                | noticed                |

The `LayoutEditorDialog` row is the one that matters: its existing test holds the first callback and calls it, so a getter rebuilt on
every render passed. That is the failure of issues 1888 / 1889.

**Nine are pinned by four new files** (green on today's code, then proved able to fail, message read):

```text
K  B4/B5  LayoutGridToken                      expected '' to be null / expected undefined to be null
L  B1/B2  LayoutEditorDialogTokenIdentity      expected [Function] to be [Function] // Object.is equality
L  B7     LayoutEditorDialogTokenIdentity      expected 'token-1' to be an instance of Promise
O  B4/B5  OverlayDraftFormGetTokenNull         expected '' to be null / expected undefined to be null
C  B4/B5  CameraDetailPageToken                expected '' to be null / expected undefined to be null
```

**B6 is equivalent, in all four copies**: the render-time assignment runs straight after `useRef` and before anything can read the ref,
so the initial value is never observable. B3 (no assignment) is the break that would expose it, and every copy already fails that one.

## Counterfactuals on the shared hook (unit test and each site)

On the shared hook itself (`useLatestAccessToken.test.tsx`, 3 tests): B1 and B2 `expected [Function] to be [Function]`, B3 `expected
'token-1' to be 'token-2'`, B4 `expected '' to be null`, B5 `expected undefined to be null`, B7 `expected null to be an instance of
Promise`; B6 equivalent. After each switch the matching break was applied to the **shared** hook and the site's own tests failed:
kiosk (`LayoutGridIdentity`, `LayoutGridToken`), `LayoutEditorDialog` (`…TokenIdentity`, `…Token`), `OverlayDraftForm`
(`…GetToken`, `…GetTokenNull`), `CameraDetailPage` (`CameraDetailPage.test`, `…Token.test`).

## §IV: the legs, per commit

| Commit                          | §IV legs touched                                                                                  | Result                                                                       |
| ------------------------------- | ------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `useLatestAccessToken` (shared) | none (unused when added)                                                                          | unchanged                                                                    |
| `LayoutGrid`                    | overlay draw, decode sampler, composite + render (the tile effects key on this getter's identity) | unchanged; `LayoutGridIdentity` and the 14-file grid family re-run unchanged |
| `LayoutEditorDialog`            | token handed to the tile view's overlay editor / WHEP session                                     | unchanged                                                                    |
| `OverlayDraftForm`              | token handed to `OverlayEditor`'s WHEP session                                                    | unchanged                                                                    |
| `CameraDetailPage`              | decode sampler (`CameraViewer`'s interval keys on this getter)                                    | unchanged                                                                    |

No state, effect, subscription or render-time work was added; the hook is the same `useRef` + render-time assignment + empty-deps
`useCallback` the four copies held. **Not observed:** a running wall or operator console; the claim rests on identity tests under jsdom.

## Judgements and slips, kept visible

- **D2 (barrel).** The kiosk app did not import `@smart-sentinel-eye/shared/hooks` before; it resolves, and `tsc`, `eslint` and the 392
  tests are clean, so no subpath export was needed.
- **The shared unit test needed `// @vitest-environment jsdom`**: `apps/shared` defaults to `node`; the first run failed with
  `document is not defined`, which made every counterfactual "fail" for the wrong reason until it was fixed and rerun.
- **My first `eslint .` ran over `dist/`** (hundreds of `no-undef`); the real script is `eslint src --max-warnings 0`.
- Two unrelated modified C# files (`CameraCatalogClient.cs`, `StreamDistribution/Api/Program.cs`) were in the working tree throughout;
  neither was ever staged and neither is in any commit of this branch.
- **#3223's premise** (a `useSubmitGuard` in the Save gates): `apps/shared/src/hooks/useFormDialog.ts` already exports one. Not
  touched here; needs its own check.

## QA

Recorded below after `/code-review medium`.
