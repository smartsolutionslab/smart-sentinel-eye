# Verification 485 — Double-break `OverlayLayerList`'s add/remove/move guards

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). **Tests only: no production file changed.** Not on the
§IV path (see below). Not observed in a browser; the evidence is the existing suites plus three new tests under jsdom.

## §IV legs touched: none

`OverlayLayerList` and `useElementSelection` belong to the operator's overlay authoring form in `management-web`. No media, event,
overlay-state or render-loop code is involved, and nothing in this change touches a source file.

## What was measured

Twenty-eight named, exactly-once breaks (`probe485.cjs`; every pattern dry-run first and matching once) of the guards in
`OverlayLayerList.tsx` (**L**), `useElementSelection.ts` (**H**), the call site `OverlayDraftForm.tsx` (**C**: `canAdd` / `canRemove`)
and the Add menu gate (**M**: `readOnly || !canAdd ? plain button : menu`). Each break ran against three things, read separately:

- **real tree** — the `management-web` overlays folder minus `OverlayDraftFormProps` (the real list inside the real form);
- **seam** — `OverlayDraftFormProps.test.tsx` (spec 477's stub seam, which calls the form's callbacks directly);
- **list isolated** — the shared `OverlayLayerList*` tests.

The three files were restored from git after every break; the runner refused to start on a dirty file; `git status` was clean on `apps`
after every batch. Counts are failing tests.

| Guard                          | Break  | Real tree | Seam | List isolated |
| ------------------------------ | ------ | --------- | ---- | ------------- |
| **A1** add under `readOnly`    | L      | none      | none | **none**      |
|                                | H      | none      | 1    | —             |
|                                | LH     | none      | 1    | none          |
|                                | M      | 1         | none | 2             |
|                                | ML     | 1         | none | 2             |
|                                | MH     | 1         | 1    | 2             |
|                                | MLH    | 1         | 1    | 2             |
| **A2** add at the cap          | L      | none      | none | **none**      |
|                                | H      | none      | 1    | —             |
|                                | C      | 2         | none | —             |
|                                | LH     | none      | 1    | none          |
|                                | CLH    | 2         | 1    | none          |
|                                | M      | 2         | none | 2             |
|                                | ML     | 2         | none | 2             |
|                                | MH     | 2         | 1    | 2             |
|                                | MLH    | 2         | 1    | 2             |
| **R1** remove under `readOnly` | L      | none      | none | 1             |
|                                | H      | none      | 1    | —             |
|                                | **LH** | **none**  | 1    | 1             |
| **R2** remove the last         | L      | none      | none | 1             |
|                                | H      | none      | 1    | —             |
|                                | C      | 1         | none | —             |
|                                | **LH** | **none**  | 1    | 1             |
|                                | CLH    | 1         | 1    | 1             |
| **M1** move under `readOnly`   | L      | none      | none | 1             |
|                                | H      | none      | 1    | —             |
|                                | **LH** | **none**  | 1    | 1             |
| **M2** move past either end    | L      | none      | none | 2             |

Messages read (examples): seam, R1-H `expected [ 'B' ] to deeply equal [ 'A', 'B' ]`; seam, M1-H `expected [ 'B', 'A' ] to deeply equal [
'A', 'B' ]`; list isolated, R2-L `expected "vi.fn()" to not be called at all, but actually been called 1 times`; real tree, A2-C and
A2-M `expect(element).toHaveAttribute("aria-disabled", "true")` in `OverlayDraftFormLayers` ("Makes Add unavailable at 8 elements and
says why") and `OverlayDraftFormShapes`; real tree, A1-M `OverlayDraftFormHost` "Makes the layer list controls unavailable while
readOnly even though there are several layers".

## Dispositions

**Gaps, now pinned (R1-LH, R2-LH, M1-LH).** Each layer's guard was pinned only by a test that stops working when the _other_ layer is
restored. With both removed, nothing that renders the real list inside the real form failed. The buttons are `aria-disabled`, not
natively disabled, so a click reaches the handler. Three tests in one new file, `OverlayDraftFormGuards.test.tsx`, green on today's
code and then proved able to fail by their own double break:

```text
R1-LH  a read-only form keeps both layers: expected [ 'B' ] to deeply equal [ 'B', 'A' ]
M1-LH  a read-only form keeps the layer order: expected [ 'A', 'B' ] to deeply equal [ 'B', 'A' ]
R2-LH  removing the last layer must not even be attempted: expected [ …(1) ] to deeply equal []
```

R1-L alone also fails the first test (`no removal is announced`), a bonus: the list's `readOnly` guard on Remove is now pinned in
the real tree on its own too.

**A look at R2 first misled me.** The existing test "Makes Remove unavailable while a single element remains" _does_ click Remove, and
it still passes with both guards gone. Without them `removeElement` computes an empty `remaining` and throws a `TypeError` before
the state changes, so the layer list looks untouched. The new test also listens for the uncaught error, which is why it fails.

**Equivalent / unreachable, recorded and not pinned (A1-L/H/LH, A2-L/H/LH).** The handler guards for Add are unreachable through the
DOM: under `readOnly` and at the cap the list renders a plain unavailable button, not the menu, so no menu entry exists to click. The
menu gate (M) is the real defence, and it is noticed by named render tests in the real tree and in isolation (rows above). The list's
own `readOnly || !canAdd` guard in `add` (A1-L, A2-L) is noticed by nothing anywhere, and neither is the hook's pair in the real tree
(A1-LH, A2-LH); a test of either would assert a state the component cannot reach (FR-004). The hook's Add guards stay pinned by the
stub seam, as spec 477 left them.

**Defended end to end already.** The call site's `canAdd` and `canRemove` (A2-C, R2-C, A2-CLH, R2-CLH), the Add menu gate (all M
rows), and the move bounds in isolation (M2-L: `Makes Move up unavailable on the top row and inert`).

**Spec 477's caveat is closed:** "No double break was run against that shared file" is now answered by the table above, and the three
reachable ones are pinned.

## Suites

|                                         | Before | After                     |
| --------------------------------------- | ------ | ------------------------- |
| `management-web` overlays folder        | 202    | **205** (+3)              |
| `management-web`, whole app             | 737    | **740**                   |
| `shared`, whole app                     | 1395   | **1395**                  |
| shared `OverlayLayerList*`              | 36     | **36**                    |
| `Architecture.Tests` (they scan `.tsx`) | —      | **3254 / 3254**, 4 m 43 s |

`git diff --name-status origin/develop..HEAD -- apps` lists **one added file and no modified one**
(`OverlayDraftFormGuards.test.tsx`). `eslint src --max-warnings 0` and `tsc --noEmit` clean for `shared` and `management-web`;
`npm run format:check` clean. The whole-app `management-web` run (740) was taken before the lint fix below; the overlays folder (205)
and the three break proofs were re-run after it.

## Judgements and slips, kept visible

- **Lint failed on my first version of the new test** (`'ErrorEvent' is not defined`, then `'Event' is not defined`): the project's
  eslint config declares neither. My command chain then committed anyway. The branch was not pushed, so I rewrote its two unpushed
  commits into one test commit and one docs commit; every commit now passes eslint. The listener is typed structurally and names no
  global.
- **Break proofs were repeated after the test changed**, not carried over from the first version.
- The run order: probes were committed-clean before use; the runner restores from git, so the gap test was committed before it was
  proved by its breaks.
- A rebase onto `origin/develop` after #3230 merged conflicted only on `.specify/feature.json`; kept this branch's value.
- Two unrelated modified C# files (`CameraCatalogClient.cs`, `StreamDistribution/Api/Program.cs`) and an untracked `apphost.pid` were in
  the working tree throughout; none was ever staged.

## QA

(To be filled by T008.)
