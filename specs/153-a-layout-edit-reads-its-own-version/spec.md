# Spec 153 — A layout edit reads its own version

**Issue:** #2368 · **Branch:** `fix/2368-a-layout-edit-reads-its-own-version`
**Kind:** bug fix (client-side cache semantics). Nothing under `src/**` changes.
**ADRs:** ADR-0113 (two-layer optimistic concurrency), ADR-0075 (RTK Query),
ADR-0037 (phases), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane).

## The defect, restated from what the code actually does

`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:65`

```ts
const { data: currentChain, refetch: refetchChain } = useGetLayoutQuery(editTarget?.layoutIdentifier ?? skipToken);
```

`data` is RTK Query's last successful result for **any** argument the hook has
ever taken. It survives a `skipToken` step and an argument change.
`currentData` is the one that resets on both.

`LayoutsPage.tsx:333-340` renders the edit dialog **permanently mounted** —
`open={editTarget !== undefined}` with `editTarget` handed as a prop — so
editing layout A, closing, then editing layout B is `editTarget: A → undefined
→ B` on one component instance, never an unmount. B's dialog therefore reads
A's chain, **including A's `version`**, before B's own GET has answered. That
version goes into `If-Match` (ADR-0113).

Versions are small per-chain integers, so A's version and B's current version
**coincide often**. When they coincide the server accepts a write whose
precondition was never actually checked. That is the hazard: not a loud 412,
but a silent success on an unverified precondition.

The re-read is wired correctly — `LayoutEditorDialog.tsx:59-65` explains why it
must exist — and then discarded.

## Three failure windows, and which change closes which

They are not one bug, and the fix is not one line. Established by reading the
code, not inferred from the overlay twin:

| # | Window | Today | Closed by |
|---|---|---|---|
| 1 | Edit A, close, edit **B**; B's GET in flight | `data` = A's chain, so A's version is submitted under B's identity. **Unverified precondition, can succeed.** | `data` to `currentData` |
| 2 | **First** edit of a layout this session; its GET in flight | `data` undefined, so `if (currentChain === undefined) return;` fires — Save does nothing, silently, with no message | a visible Save gate (FR-002) |
| 3 | Edit **A**, close, edit A again within `keepUnusedDataFor` (60 s); the branch has bumped the version and the invalidation refetch is in flight | `currentData` **still serves the stale cached value** — it resets on an arg change, not on a refetch of the same arg. Submits A's own older version, so 412: safe, but confusing ("changed since version 7") about the operator's own branch | gating on the fetch state, not only on `undefined` (FR-002) |

**Window 3 is the correction to the issue text.** #2368 says the branch path is
fixed by reading `currentData`; it is not. `branchDraftRevision` invalidates
`{type:'Layout', id}` (`apps/shared/src/api/layouts.api.ts:135`), and an
invalidation-driven refetch of the *same* argument leaves `currentData` holding
the previous value. Only a fetch-state gate closes it. The outcome there is a
loud 412 rather than a silent wrong write, so it is the least severe of the
three — but the file's own comment names the branch path as the reason the
re-read exists, so leaving it open would fix the defect while missing its
stated motivation.

## Why the gate drags a fourth requirement in

Layouts has **no** Save-disabled-until-version-read gate — unlike overlays,
which gained FR-013 in spec 152. It has a silent `return` inside `onSubmit`.
Adding the gate without an error surface would make a *failed* chain read a
permanently disabled Save with nothing on screen explaining it: a dead end.
So the chain-read failure has to become visible, with a Retry, the way
`OverlayEditorDialog.tsx:277-283` does it. That is a consequence of FR-002,
not scope creep.

## Functional requirements

- **FR-001** — The dialog reads the chain for **the layout currently being
  edited**, never a previously read one. (`currentData`, not `data`.)
- **FR-002** — Save is **disabled**, visibly, while editing and the current
  layout's version is unknown — either not yet read, or being re-read. It is
  never a silent no-op.
- **FR-003** — Reopening the dialog on a layout read earlier still **re-reads
  from the server** rather than answering from the 60 s cache window
  (`refetchOnMountOrArgChange: true`).
- **FR-004** — A chain read that fails says so, with a Retry, and takes
  priority over a stale mutation banner — one `role="alert"`, never two
  siblings.
- **FR-005** — No change to create mode. `isEdit === false` submits no version
  and must not acquire a gate.

## Acceptance scenarios (Gherkin)

### Happy path

```gherkin
Given layout A has been edited and the dialog closed
When the operator opens the edit dialog on layout B
And B's chain read has not yet answered
Then Save is disabled
And no request carries B's revision with A's version
When B's chain read answers with version 3
Then Save is enabled
And saving sends If-Match "3"
```

### Conflict — another operator moved the chain

```gherkin
Given the dialog holds layout A at version 7
And another operator has published, taking the chain to version 8
When the operator saves
Then the server answers 412
And the banner offers Reload, never "try again"
And Reload refetches the chain so the resubmission carries 8
```

### Branch path — the version the page held is already behind

```gherkin
Given the operator clicks Edit on a published layout
When the page branches a new draft, bumping the chain version
And the invalidation refetch is still in flight
Then Save is disabled until that refetch answers
And the submitted version is the post-branch one
```

### Bad request

```gherkin
Given a grid holding more than four tiles
When the operator saves
Then the server refuses it and the dialog shows the refusal
```

`GridDimensions.MaxTiles = 4` — verified in
`src/LayoutComposition/Domain/Layout/GridDimensions.cs:18`. A merged ADR saying
250 is wrong; see #2363. Nothing in this spec depends on the number.

### Auth / scope

```gherkin
Given an operator without the layout-write scope
When the dialog is opened
Then the chain read is refused
And FR-004's alert is shown rather than a silently dead Save button
```

### Create mode unaffected

```gherkin
Given the dialog is opened with no editTarget
Then Save is enabled as soon as a camera choice exists
And no chain is read
```

## Independent end-to-end test procedure

1. Boot the stack (`AspireFixture` or `dotnet run` on AppHost); sign in to
   management-web as an operator with layout write scope.
2. Create and publish two layouts, A and B.
3. Open devtools Network, throttle to "Slow 3G".
4. Click Edit on A. Wait for Save to enable. Close the dialog.
5. Click Edit on B. **Before** B's `GET /layouts/{B}` completes, observe Save
   is disabled. Force a click; confirm no `PATCH` is issued.
6. Let B's GET land. Save. In Network, read the `If-Match` on the PATCH and
   confirm it equals the `version` in **B's** GET response, not A's.
7. Repeat steps 4-6 with A twice in a row inside 60 s: confirm Save disables
   during the post-branch refetch and the PATCH carries the post-branch
   version, with no 412.

## Latency budget impact: N/A

This is `apps/management-web`, the operator console. None of §IV's six legs
runs through it — the ≤ 50 ms composite-and-render leg is the kiosk wall's, and
that leg is already over budget at p50 54.2 ms (ADR-0123), so this spec adding
nothing to it is a requirement, not an observation. No file under
`apps/kiosk-web/**` or `apps/shared/src/ui/composites/**` is touched.

## Out of scope

- **`apps/management-web/src/features/overlays/`** — PR #2369 is open against
  those files. Do not touch them.
- **`src/`** — the server is correct; it checks the `If-Match` it is given.
- **The reset-on-close defect in the same file.** `LayoutEditorDialog.tsx:66,70-72`
  resets `isEdit ? editState : createState`, but `LayoutsPage` drives `open` and
  `editTarget` together, so by the time the effect sees `!open`, `isEdit` is
  already false — a refused edit's error survives into the next open. This is
  the exact twin of the second defect commit 9357b65e fixed in overlays. It is
  a **different user-visible behaviour**, so per CLAUDE.md's two-issues rule it
  is filed separately rather than folded in.
- **Three further instances of the same RTK trap**, listed below. Each is a
  separate issue.

## What the sweep found — this is instance 3 of at least 6

Eighteen call sites destructure `data` from a query hook across the three apps;
ten pass an argument that can change on a mounted component. Beyond #2341,
#2364 and this one, three are genuine hazards and one is latent:

- `apps/shared/src/ui/composites/CameraViewer.tsx:100` —
  `useGetStreamQuery(cameraIdentifier)` with `data:`. The file's own spec-095
  comment states that a layout revision reassigning a camera leaves the tile
  **mounted** and changes only the prop. `useWhepSession`'s session effect is
  keyed on `[whepUrl, …]` (`useWhepSession.ts:304`), **not** on
  `cameraIdentifier` — so while B's stream GET is in flight the tile keeps
  playing **camera A's** WHEP session under camera B's identity, and the effect
  does not even re-run. Wrong camera on a security wall. Highest consequence of
  the set.
- `apps/shared/src/ui/composites/FrameGrabber.tsx:37` — same hook, same shape;
  a frame grabbed "for camera B" can come from A's stream.
- `apps/kiosk-web/src/features/cell/CellPage.tsx:435,469` — `useGetOverlayQuery`
  and `useGetOverlaySnapshotQuery`; the previous overlay's label renders over
  the new one until its GET answers.
- `apps/management-web/src/features/cameras/CameraDetailPage.tsx:42` — a route
  param on a mounted component; **latent**, because no camera-to-camera link
  exists today (the only `Link`s go back to `/cameras`).

The list pages (`CamerasPage`, `RulesPage`, `SystemVariablesPage`, `AuditPage`,
`PickerPage`) retain the previous list across a filter change, which is the
desired behaviour there, not a defect.

**Six instances of one trap is an argument for an enforcement rule** — an ESLint
rule forbidding a bare `data:` destructure from a query hook whose argument is
not a literal. That is an ADR-class decision, which the autonomous lane may not
make (ADR-0144). Raised here, not decided here.

## Risks

- Switching to `currentData` turns every existing mocked `useGetLayoutQuery`
  into `currentChain === undefined`. Two mock sites must be updated in the same
  commit or the suite goes red for the wrong reason
  (`LayoutEditorDialog.test.tsx:24-28`, `LayoutsPage.test.tsx:20`).
- FR-002 without FR-004 creates a dead-silent Save on a failed read. They ship
  together.
