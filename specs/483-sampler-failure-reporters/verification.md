# Verification 483 — The samplers report failures through two reporters, not raw counters

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **On the kiosk path (§IV)**, so the evidence includes the
per-commit leg statements. Not observed on a running wall; the evidence is the existing sampler tests plus three new ones under jsdom.

## Size and surface, as measured (`develop` @ `e9b973a3` vs the branch)

|                                                                                                               | Before                  | After        |
| ------------------------------------------------------------------------------------------------------------- | ----------------------- | ------------ |
| Members `useSamplerReporting` returns                                                                         | 5                       | **4**        |
| Non-test uses of `reportSamplerFailure` / `decodeSampleFailuresRef` / `lagSampleFailuresRef` outside the hook | 2 files (both samplers) | **0**        |
| `useSamplerReporting.ts` + `useDecodeSampler.ts` + `useLagSampler.ts` (lines)                                 | 338                     | **333** (−5) |

The hand-typed pairing "decode counter ↔ `'decode-sampler-failed'`" (and lag) was held in the two samplers and is now held once, inside
the hook. Each sampler destructures `reportMissingStatsField`, its one reporter and `reportThrottleRef`.

## Characterisation (FR-001)

`git diff --name-status develop..HEAD -- apps | grep test` lists **one added file and no modified one**
(`CameraViewerSamplerFailureReporting.test.tsx`). Suites, unmodified tests plus the new file: `shared` **1393** (1390 + 3), the
`CameraViewer*` + `kioskLatency` set **153** (150 + 3), `kiosk-web` **392**, `management-cameras` **151**, `management-web` **737**;
all passing. `eslint src --max-warnings 0` and `tsc --noEmit` clean for the four apps; `npm run format:check` clean for the repo;
`Architecture.Tests` **3254 / 3254** (the two unrelated modified C# files were in the working tree and built, unstaged).

## Probing before moving: 2 of 10 breaks went unnoticed

Ten named, exactly-once breaks of the failure reporting (`probe483.cjs`), the covering set run (first the four files that cover
failure reporting, 44 tests; the two survivors re-run against all 150), the file restored from git:

| Break                                              | Result                           |
| -------------------------------------------------- | -------------------------------- |
| A decode sampler logs the lag transition           | noticed (9)                      |
| B lag sampler logs the decode transition           | noticed (5)                      |
| C one counter for both samplers                    | noticed (3)                      |
| **D counters reset on a camera swap**              | **none** (44, and 150)           |
| E1 / E2 a counter created per render               | noticed (2 / 4, in `…Timers`)    |
| F reportable-count rule bypassed                   | noticed (6)                      |
| **G sink report sent without the shared throttle** | **none** (44, and 150)           |
| H reporter is a new function per render            | noticed (3)                      |
| I reporter ignores the camera and token            | noticed (1, the `getToken` test) |
| J missing-field reporter rebuilt per render        | noticed (3)                      |

**Pinned by one new file, two tests** (green on today's code, then proved able to fail, message read):

```text
D  keeps counting across a camera change in the tile ...   expected [ 1, 1 ] to deeply equal [ 1 ]
G  tells the console about both samplers but the sink about only the first inside one throttle window
                                                            one POST for the two failures: they share the tile throttle: expected [ 1, 1 ] to deeply equal [ 1 ]
```

The lag count reaching 10 across the swap is itself the evidence of no reset (a restart would say "1" again and never get there).

## A break that only exists after the refactor: N

After the switches the lag reporter has its own dependency array, so a lag-only stale reporter became possible (the old shared
callback served both samplers). Break **N** (the lag reporter with `[]`) passed all 46 tests: the existing `getToken`-follows test
reaches the sink through the decode path only. A third test in the same new file pins the lag path, **green on the pre-switch lag
code** (so it characterises today's behaviour) and failing under N with `the failure report is sent with the current getToken:
expected "vi.fn()" to be called at least once`.

## Counterfactuals on the shared hook (after each switch)

After the decode switch, breaks A2 (decode reporter logs the lag transition), K (decode reporter uses the lag counter), M (decode
reporter `[]`), C, D, E1, F, G, H and I were applied to the **shared** hook: each failed the decode-sampler tests (11, 5, 1, 5, 1, 1,
8, 1, 3, 1; M and I fail the `getToken` test, E1 and D the new test). After the lag switch, B2, L (lag reporter uses the decode
counter), **N**, C, D, E2, F, G, H and I: each failed (7, 5, **1 after the extra test**, 5, 1, 1, 8, 1, 3, 1). The hook file was restored
after each (`git diff` on `apps/shared` empty).

## §IV: the legs, per commit

| Commit                                                   | §IV legs touched                            | Result                                                                                          |
| -------------------------------------------------------- | ------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| tests (`CameraViewerSamplerFailureReporting`)            | none                                        | tests only                                                                                      |
| `useSamplerReporting` also offers a reporter per sampler | none (unused when added)                    | unchanged; 124 in the set                                                                       |
| `useDecodeSampler` switches                              | SFU → kiosk decode (`receive_to_decoded`)   | unchanged; `CameraViewerTimers`, `…Reporting`, `…SamplerWindow`, `…Alignment` re-run unmodified |
| lag-failure `getToken` test                              | none                                        | tests only                                                                                      |
| `useLagSampler` switches                                 | presentation buffer (`presentation_buffer`) | unchanged; same files re-run unmodified                                                         |
| old surface removed                                      | none                                        | unchanged; `kiosk-web` 392 and `management-cameras` 151 re-run                                  |

The sampler effects rebuild on the same occasions: the reporters change identity exactly when `reportSamplerFailure` did (`[cameraIdentifier, getToken]`,
refs never), and `CameraViewerTimers` (interval counts on a plain re-render, a camera change and a `getToken` change) passes unmodified.
No state, effect, subscription or render-time work was added. **Not observed:** a running wall; the claim rests on the timer and
identity tests under jsdom with fake timers.

## Judgements and slips, kept visible

- **The memory limit.** The first probe run was stopped by the harness for low memory and left break A applied to a source file; I
  checked the diff, restored it, and reran the matrix in the foreground in batches of five. Nothing was restarted in the background.
- **My first lag expectation was wrong** (`[1]`; over 24 s the lag sampler legitimately reaches count 10). Corrected to `[1, 10]`,
  which is stronger evidence.
- **`run483.cjs` restored all three hook files** after each break, which would have silently reverted an uncommitted switch; narrowed to
  the one file it breaks before use.
- **I stashed the uncommitted lag switch by accident** while checking the new test; confirmed the stash held only that change and popped
  it. The old unrelated `stash@{0}` was untouched.
- Two unrelated modified C# files (`CameraCatalogClient.cs`, `StreamDistribution/Api/Program.cs`) were in the working tree
  throughout; neither was ever staged.

## QA

`/code-review medium` returned four findings. Dispositions:

| #    | Finding                                                                                                                           | Disposition                                                                                                                                                                                                                                                                                                            |
| ---- | --------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1, 2 | Uncommitted edits to `CameraCatalogClient.cs`, `Program.cs` (and an untracked `apphost.pid`) in the working tree                  | **Not in this branch.** Never staged; `git diff --name-only develop..HEAD` lists no `.cs` file.                                                                                                                                                                                                                        |
| 3    | The new reporters change identity whenever `reportSamplerFailure` does, so the sampler effects rebuild then                       | **Accepted, by design (plan D1, FR-005).** That is exactly the old trigger (`[cameraIdentifier, getToken]`), not a new one; `CameraViewerTimers` (camera change, `getToken` change, plain re-render) passes unmodified, and breaks H, I, M and N (identity) are each caught.                                           |
| 4    | The counter ↔ transition pairing is enforced inside the hook, not by the type system; `reportSamplerFailure` still takes any pair | **Accepted.** The pairing used to be hand-typed in two samplers and is now written once, in two three-line wrappers; the general helper is private. Crossing the pairs is caught by breaks A2, B2, K and L (11, 7, 5 and 5 failing tests). A tighter helper would be a further change; ADR-0036 says not to speculate. |

No finding required a code change.
