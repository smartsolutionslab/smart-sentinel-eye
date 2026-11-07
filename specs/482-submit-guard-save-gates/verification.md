# Verification 482 — The two Save gates adopt the shared `useSubmitGuard`

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **Not on the §IV path**: operator-console form submit
(`management-web`); no kiosk, SFU, overlay or render leg is touched, in any commit. Not observed in a browser; the evidence is the
existing gate tests plus six new ones under jsdom.

## Premise (issue #3223)

The issue asked for an extraction "only if the two call sites really match". `useSubmitGuard` already existed
(`apps/shared/src/hooks/useFormDialog.ts`, ADR-0151, six callers) and both Save gates hand-wrote its body verbatim with `saveBlocked`
for `isLoading`. They match; the work was adoption.

## Size, as measured (`develop` @ `e9b973a3` vs the branch)

|                                                                              | Before | After         |
| ---------------------------------------------------------------------------- | ------ | ------------- |
| Hand-written guard bodies (`if (saveBlocked)` + `preventDefault` in gates)   | **2**  | **0**         |
| `useSubmitGuard` call sites (non-test)                                       | 6      | **8**         |
| `overlayDraftForm/useSaveGate.ts`                                            | 66     | 61            |
| `layoutEditor/useSaveGate.ts`                                                | 92     | 87            |
| `useFormDialog.ts` (rename + doc, prettier folded the signature to one line) | 85     | 83            |
| Total of the three                                                           | 243    | **231** (−12) |

## Characterisation (FR-001)

`git diff --name-status develop..HEAD -- apps | grep test` lists **two added files and no modified one**. Suites, unmodified tests plus
the new files: `shared` **1390** (unchanged), `management-cameras` **151** (unchanged), `management-web` **737** (731 + 6 new); all
passing. `useFormDialog.test` 15/15 and the six existing callers' suites (cameras 148, web `walls`/`rules`/`systemVariables` 147)
re-run unmodified after the rename. `eslint src --max-warnings 0`, `tsc --noEmit` clean for the three apps; `npm run format:check`
clean for the repo.

**`Architecture.Tests` were not run locally.** The run was stopped by the harness for low system memory (not a test failure) and not
restarted. They scan `.tsx`; the PR's `backend` CI job runs them and is the evidence.

## Probing before moving: 5 of 14 guard breaks went unnoticed

Seven named, exactly-once breaks of the submit guard per gate (`probe482.cjs`), the covering suites (`overlays` 199 tests, `layouts` 210) run, the file restored from git:

| Break                                  | Overlay gate      | Layout gate  |
| -------------------------------------- | ----------------- | ------------ |
| A blocked: no `preventDefault`         | noticed (`…Host`) | **none**     |
| B blocked: still calls `onSubmit`      | noticed (4)       | noticed (3)  |
| C open: never calls `onSubmit`         | noticed (37)      | noticed (14) |
| D open: `onSubmit()` without the event | **none**          | **none**     |
| E open: also `preventDefault`          | **none**          | **none**     |
| F guard inverted                       | noticed (39)      | noticed (15) |
| G flag frozen at the first render      | noticed (4)       | noticed (14) |

**Pinned by two new files** (green on today's code, then proved able to fail, message read), each driving the real `useSaveGate`:
`OverlayDraftFormSubmitGuard.test.tsx` and `LayoutEditorDialogSubmitGuard.test.tsx`, three tests each (blocked prevents and does not
submit; open hands the very event to `onSubmit` once and does not prevent; follows blocked → open across re-renders). Under each
break the new files fail on the intended assertion, e.g. D `expected "vi.fn()" to be called with arguments: [ { preventDefault … } ]`,
E `expected "vi.fn()" to not be called at all, but actually been called 1 times`, A `expected "vi.fn()" to be called 1 times, but got 0
times`.

## Counterfactuals on the shared hook (after each switch)

After each gate switched, the same seven breaks were applied to the **shared** `useSubmitGuard` (`shared482.cjs`) and the gate's tests
run: overlay A–G failed 2, 4, 8, 1, 1, 10, 2 tests; layout 1, 5, 6, 1, 1, 8, 5. The shared file was restored after each (`git diff` on
`apps/shared` empty).

## `saveBlocked` terms (context only, no term changed)

Dropping each term once: overlay all six noticed. Layout: `currentChain === undefined` (**H3**) is **not noticed** by any test (the
sibling `chainFetching` term plausibly masks it in every scenario). Outside this spec's scope (it forbids changing terms) and not a
submit-guard concern; recorded here, not pinned.

## Judgements and slips, kept visible

- **D1 (rename `isLoading` → `blocked`).** All eight callers are positional; one `useFormDialog.test` title still says "Follows
  isLoading" (a title, not an assertion; not edited).
- **My first shared-hook frozen-flag break was not a break** (`const frozen = blocked` re-evaluates every render), and a syntax error
  in the probe script then left the previous results file in place, so the first "G: NONE" was meaningless. Rebuilt with `useRef`
  and rerun: G fails both gates as it should.
- Two unrelated modified C# files (`CameraCatalogClient.cs`, `StreamDistribution/Api/Program.cs`) were in the working tree throughout;
  neither was ever staged.

## QA

`/code-review medium` returned six findings. Dispositions:

| #    | Finding                                                                         | Disposition                                                                                                                                                                                                                                       |
| ---- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1, 2 | Uncommitted edits to `Program.cs`, `CameraCatalogClient.cs` on the branch       | **Not in this branch.** Never staged; `git diff --name-only develop..HEAD` lists no `.cs` file.                                                                                                                                                   |
| 3    | Rename changes the hook's contract; fresh closure per render; doc names callers | **Accepted.** All eight callers are positional (`tsc` clean). The old hand-written handler was a fresh function per render too (plan D4), so nothing changes. The doc names the two Save gates on purpose: that is why the parameter was renamed. |
| 4    | The FR-003 comment above `handleFormSubmit` described the inline body           | **Fixed** in both gates (comment only): "this guard" → "`useSubmitGuard`", so the comment points at where the logic now lives. Gate tests re-run green.                                                                                           |
| 5    | Import order differs between the two gates                                      | **Accepted.** No import-order rule is configured; eslint and prettier are clean on both files.                                                                                                                                                    |
| 6    | The two new test files are near-identical; they cannot show delegation          | **Accepted.** One file per gate follows the repo's per-site convention (spec 481 did the same). Delegation is shown by the counterfactual: breaks A–G on the **shared** hook fail each gate's tests (see above).                                  |
