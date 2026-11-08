# Verification 484 — `useBackdropCapture` returns three groups, not eleven values

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **Not on the §IV path**: `OverlayEditor` is the authoring
canvas and only `management-web` imports it. Not observed in a browser; the evidence is the existing editor suites plus two new tests
under jsdom.

## §IV legs touched: none

None of the six legs. The change moves where a hook's return values are named; no media, event, overlay-state or render-loop code is
involved, and `kiosk-web` does not import the editor. No state, effect or subscription was added or removed (D2: the group objects are
built per render, as the eleven values were read per render).

## Size and surface, as measured (`origin/develop` @ `1745446a` vs the branch)

|                                                                                                                       | Before  | After                   |
| --------------------------------------------------------------------------------------------------------------------- | ------- | ----------------------- |
| Names `OverlayEditor` destructures from the hook                                                                      | 11      | **3**                   |
| Loose reads of `setBackdrop`, `handleCameraChange`, `handleCapture`, `handleCaptured`, `fail`, `cancel` in the editor | 6 names | **0**                   |
| Hand renames at the `<BackdropControls>` call site                                                                    | 4       | **0** (`{...controls}`) |
| `useBackdropCapture.ts` (lines)                                                                                       | 55      | 58                      |
| `OverlayEditor.tsx` (lines)                                                                                           | 410     | 395                     |
| Both                                                                                                                  | 465     | **453** (−12)           |

T007's grep of `OverlayEditor.tsx` for the hook and the six names returns only the import, the single three-name destructure and one
unrelated comment ("success, failure, timeout, cancel, camera change").

## Characterisation (FR-001)

`git diff --name-status $(git merge-base HEAD origin/develop)..HEAD -- apps` lists **one added test file and no modified one**
(`OverlayEditorCaptureCancel.test.tsx`), plus the two source files. Suites, unmodified tests plus the new file:

| Suite                                   | Before | After           | Duration after |
| --------------------------------------- | ------ | --------------- | -------------- |
| `shared` (92 files)                     | 1393   | **1395** (+2)   | 59.7 s         |
| editor + `BackdropControls` set         | 174    | **176** (+2)    | —              |
| `management-web` (101 files)            | 737    | **737**         | 101 s          |
| management-web `overlays`               | 202    | **202**         | —              |
| `Architecture.Tests` (they scan `.tsx`) | —      | **3254 / 3254** | 4 m 18 s       |

Durations before were not re-measured; the counts are from the before-run files and spec 483's note. `eslint src --max-warnings 0` and
`tsc --noEmit` clean for `shared` and `management-web`; `npm run format:check` clean for the repo. `Architecture.Tests` ran once in the
foreground and finished (the two unrelated modified C# files were in the working tree and built, unstaged).

## Probing before moving: 6 of 25 breaks went unnoticed

Twenty-five named, exactly-once breaks of the hook (A–K3) and of the editor's wiring (L–W), the covering set (174 tests) run per break, the two
files restored from git each time (`probe484.cjs`, `run484.cjs`). Failure messages were read, not just counts.

| Break                                                     | Result                                                         |
| --------------------------------------------------------- | -------------------------------------------------------------- |
| A initial camera ignored                                  | noticed (1: "Starts the picker on the camera the host passed") |
| **B `handleCapture` without the empty-camera guard**      | **none** — jointly defended, see below                         |
| C camera change does not cancel                           | noticed (2: grabber not released; failed alert not cleared)    |
| D camera change does not select                           | noticed (5)                                                    |
| E / F a captured frame sets no backdrop / no frame        | noticed (1 each)                                               |
| G captured frame does not cancel the session              | noticed (1: grabber not released)                              |
| H initial backdrop `white`                                | noticed (2)                                                    |
| I / J capture from the initial / a stale camera           | noticed (1 / 5)                                                |
| **K1 / K2 / K3 handler is a new function per render**     | **none** — equivalent, see below                               |
| L canvas paints no frame; M `hasCapturedFrame` constant   | noticed (1 each)                                               |
| **N grabber without its `key`**                           | **none** — equivalent, see below                               |
| **O Cancel capture wired to nothing**                     | **none** in the existing set → gap (below)                     |
| P `onFailed` a no-op                                      | noticed (1)                                                    |
| Q captured/failed swapped                                 | noticed (2)                                                    |
| R / S capture / camera change no-ops                      | noticed (6 / 5)                                                |
| T `selectedCamera` empty; U controls backdrop constant    | noticed (6 / 3)                                                |
| V backdrop change no-op; W `captureState` constant `idle` | noticed (3 / 1)                                                |

The six survivors: B (jointly defended), K1/K2/K3 and N (equivalent), and O (a real gap, pinned next).

### The gap: O — the operator's own **Cancel capture** press

`OverlayEditorCapture.test.tsx` covers every other way a capture ends (frame, failure, camera change, token gone) but never presses
Cancel. One new file, two tests, green on today's code, then proved able to fail (`484-pin`):

```text
O  releases the grabber and offers Capture again…    the grabber is released: expected <div data-testid="grabber" …(1)></div> to be null
O  lets the operator capture again from the same camera after cancelling    expected "vi.fn()" to be called at least once
R  (capture no-op) both tests fail: Unable to find an element by: [data-testid="grabber"]
```

### Recorded, not pinned

- **B (empty-camera guard) — jointly defended.** `handleCapture` returns early on `''`; `BackdropControls` also disables the Capture
  button for `selectedCamera === ''`. Removing either alone changes nothing the operator can reach; removing both fails. Keeping the
  guard is a deliberate belt-and-braces; a test of one alone would assert an unreachable state.
- **K1 / K2 / K3 (handler identity) — equivalent.** Nothing downstream keys on a handler's identity: `BackdropControls` is a plain function
  component (not `React.memo`, checked), and `FrameGrabber`'s effect re-subscribes harmlessly while `settledRef` guards a double
  settle. Plan D2 records that this is revisited if `BackdropControls` is ever memoised.
- **N / X14 (grabber `key`) — equivalent under the suite.** The key remounts the grabber if the camera changes while a session is
  active; the camera-change path already cancels the session first (break C is caught), so no test reaches a second grabber that
  reuses an instance. Recorded, not pinned.

## Counterfactuals on the new code (after the switch, committed first)

The runner restores from git, so T005 was committed (`629f386d`) before probing. Hook-internal breaks A, C–J (`484-sw1`) all failed the
tests, each with the same message as before the switch. Then 15 group-wiring breaks (`484-sw2`, `484-sw3`):

| Break                                     | Failing      | Break                                    | Failing                  |
| ----------------------------------------- | ------------ | ---------------------------------------- | ------------------------ |
| X1 `onCancelCapture` no-op                | 2 (new file) | X9 `onBackdropChange` no-op              | 3                        |
| X2 `hasCapturedFrame` constant            | 1            | X10 `captureState` constant `idle`       | 3                        |
| X3 `onCaptured`/`onFailed` swapped (hook) | 2            | X11 controls `backdrop` constant         | 3                        |
| X4 `onFailed` no-op                       | 1            | X12 controls **not spread**              | 12                       |
| X5 canvas has no frame                    | 1            | X13 editor canvas paints `null`          | 1                        |
| X6 `onCapture` no-op                      | 8            | **X14 grabber without `key`**            | **none (equivalent, N)** |
| X7 `onCameraChange` no-op                 | 5            | X15 editor swaps `onCaptured`/`onFailed` | 2                        |
| X8 `selectedCamera` empty                 | 8            |                                          |                          |

The only survivors are B, K1/K3 and X14, all recorded above. The two files were restored after each break; `git status` is clean
apart from the foreign files.

## Judgements and slips, kept visible

- **A dry-run loop of mine contained `git checkout -- .`**, which would have discarded the uncommitted switch. The harness denied it;
  nothing ran. I redid the loop with explicit paths and committed T005 before probing.
- **The probe runner restores from git**, so new code must be committed before it is probed. It was, in `629f386d`.
- **Spec number.** 483 existed only on a parked PR when this number was chosen; checked against remote branches and open PRs.
- Two unrelated modified C# files (`CameraCatalogClient.cs`, `StreamDistribution/Api/Program.cs`) and an untracked `apphost.pid` were in
  the working tree throughout; none was ever staged.

## QA

(To be filled by T008.)
