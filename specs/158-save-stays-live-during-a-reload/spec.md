# Spec 158 — Save stays live during a re-read

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2379](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2379) · **Branch:** `fix/2379-save-stays-live-during-a-reload`
**Lane:** autonomous (ADR-0144).
**Base:** `develop` at `37011568`. Every line and file reference below was re-read at that SHA.
**ADRs:** ADR-0113 (two-layer optimistic concurrency — the `If-Match` version is
the mechanism this defect defeats), ADR-0075 (Redux Toolkit + RTK Query — the
`currentData`/`isFetching` semantics are the whole of the mechanism), ADR-0074
(two frontends; this is `management-web` only), ADR-0139 (new behaviour starts
red), ADR-0109 (`[P]` marking), ADR-0037 (phases), ADR-0144 (lane).
**Constitution:** §Testing (two obligations — this spec declares one of each, see §7).

**Latency budget (§IV): N/A.** No leg. `management-web` is the operator console;
this predicate is not on the `event arrival → overlay rendered` path, does not
touch the kiosk, the SFU, the composite or any overlay-state push. No cell of
the §IV table moves and no measurement is owed.

---

## 1. The finding, verified against the code

**The premise of #2379 holds at `37011568`.** Verified by reading, not by
trusting the issue:

`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx:303`

```tsx
<Button ref={saveRef} type="submit" disabled={isLoading || (isEdit && currentChain === undefined)}>
```

`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:419`

```tsx
disabled={isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching))}
```

Both dialogs read the chain with `currentData`, and both bind `isFetching` to a
local called `chainFetching` (`OverlayEditorDialog.tsx:89-94`). The overlay
dialog **binds `chainFetching` and then does not use it in the Save
predicate** — it is passed only to `ChainRecoveryNotice`'s `reReading` prop at
`:293`. The layout dialog uses it in both places.

RTK Query keeps `currentData` defined across a **same-argument** refetch; it
resets only on a `skipToken` step or an argument change
(`OverlayEditorDialog.tsx:72-79` states this, and it is what the sibling
`LayoutEditorDialogChainRetention.test.tsx` was written to prove). So while a
re-read of the *same* overlay is in flight, `currentChain` stays defined at the
**pre-re-read version**, the predicate is false, and Save is **enabled**.

`onSubmit` (`:186-194`) then sends `version: currentChain.version` as the
`If-Match` — the version the client has already asked the server to correct.

### The trigger the issue names, and the one it does not

#2379 names **Reload**, the control offered after a stale-version conflict
(`offerReload = staleConflict || notDraft`, `:230`). That path is real: clicking
Reload calls `refetchChain()` (`:294`), and the existing FR-008 test drives
exactly that window.

There is a second trigger, and it is the more common one. `editDraftOverlayRevision`
declares `invalidatesTags: [{ type: 'Overlay', id }, …]`
(`apps/shared/src/api/overlays.api.ts:124-127`) with no error branch, and
`getOverlay` provides `{ type: 'Overlay', id }` (`:74`). RTK Query applies
`invalidatesTags` on a **rejected** mutation too. So a stale-version **412
starts a background chain refetch by itself**, while the dialog is still
subscribed — precisely the moment an operator is about to click Save a second
time. `LayoutEditorDialog.tsx:403-413` records the same mechanism on the layout
side, verified there in phase 6 against a real `LAYOUT_REVISION_STALE` 412.

So the window is not a rare button press. It is the ordinary aftermath of the
conflict the whole recovery affordance exists for.

### Why it matters, and what it is not

This is ADR-0113's client half. The 412 the second submit earns means **nothing
is corrupted** — this is a usability and correctness-of-reasoning defect, not
data loss. The operator is refused for the exact reason the UI had already set
out to fix one round trip earlier, and the refusal arrives as a second alert
that looks identical to the first.

## 2. The second question #2379 asks, answered

> whether the layout dialog's `chainFetching` term is *itself* pinned by a test,
> or merely present.

**Merely present. Nothing pins it**, and the repository already says so in two
places — neither of which is a test:

- `LayoutEditorDialog.tsx:412-413`: *"Neither is pinned by a test in this repo,
  so the outcome is recorded here rather than asserted."*
- `LayoutEditorDialogChainRetention.test.tsx:29-44`: every `it` in that file that
  has `chainFetching` true **also** has `currentChain === undefined`, which the
  file's own header comment states and which reading the three tests confirms
  (`:207`, `:332`, `:409`). The `currentChain === undefined` half closes all
  three on its own.

`LayoutEditorDialogChainRecovery.test.tsx` does drive `isFetching: true` (via
`beginReRead()`, `:147-149`) and **does not assert Save's disabled state in that
window at all** — its FR-008 test (`:334-363`) asserts focus only.

This is the same defect shape as #2371's `createState.reset()`: a term that
would survive deletion with the whole suite green. It gets its own
characterisation test here (US2), not a production change.

## 3. Scope

**In scope:** one term in one predicate in one file, and two tests.

**Out of scope, and why** — each is a deliberate refusal to widen a bug fix,
which is the rule (`CLAUDE.md` §Karpathy, ADR-0036) that produced this issue in
the first place: spec 156's FR-010 forbade touching this predicate so its own
change could be reviewed for what it was.

1. **The `knownCameras.size === 0` term.** Layout-only, a camera-availability
   gate, unrelated.
2. **What happens to keyboard focus when Save becomes natively `disabled` while
   focused.** Reachable after this fix: click Save → focus is on Save → 412 →
   the invalidation refetch above → Save disables → the browser drops focus to
   `<body>`. This repository already knows the mechanism —
   `ChainRecoveryNotice.tsx:31-32` chose `aria-disabled` over native `disabled`
   for exactly this reason (spec 154), and spec 156 was an entire spec about a
   recovery control destroying its own focused element. **It is not fixed here**:
   it applies identically to `LayoutEditorDialog`, which has shipped the term for
   two specs; fixing it is an accessibility change to *both* dialogs and belongs
   in its own issue with its own review. T006 files that issue with this
   evidence so the finding does not evaporate — recording it in a spec nobody
   re-reads is how §IV's leg table drifted.
3. **A failed re-read leaving Save enabled on a stale `currentData`.** Adjacent,
   unverified, and affecting both dialogs equally. Folded into the same
   follow-up issue as a question to check, explicitly marked unverified — it is
   not asserted anywhere in this spec.

## 4. User stories

### US1 (P1) — an operator cannot submit a version the client is already replacing

**As** an operator editing an overlay draft in `management-web`,
**when** a re-read of that overlay's chain is in flight — because I clicked
Reload, or because a stale-version 412 started one — **I want** Save to be
unavailable until the re-read answers, **so that** I cannot spend a round trip
submitting the version the re-read exists to correct.

Independently shippable and independently observable: open the overlay editor,
provoke a 412, watch Save.

### US2 (P2) — the layout dialog's equivalent guard is held by a test

**As** a maintainer, **I want** the layout dialog's `chainFetching` term pinned
by an assertion, **so that** deleting it fails the build instead of leaving the
suite green.

No production change. Shippable on its own; does not depend on US1.

## 5. Acceptance scenarios (Gherkin)

All scenarios are `management-web`, overlay/layout editor dialog, **edit mode**.

### US1

```gherkin
Scenario: Reload — Save is unavailable while the re-read it starts is in flight   # happy
  Given the overlay editor is open on a draft revision whose chain read answered version 7
  And a save has been refused with a 409 OVERLAY_REVISION_STALE
  And the Reload control is offered
  When the operator clicks Reload
  And the chain re-read is in flight
  Then the Save control is disabled
  And no PATCH carrying If-Match "7" has been issued
```

```gherkin
Scenario: The re-read answers and Save comes back                                 # happy, complement
  Given a chain re-read is in flight and Save is disabled
  When the re-read answers with version 8
  Then the Save control is enabled
  And a subsequent save carries If-Match "8", never "7"
```

```gherkin
Scenario: Conflict — the 412's own invalidation refetch closes the gate           # conflict
  Given the overlay editor is open on a draft revision whose chain read answered version 7
  When the operator saves and the server answers 409 OVERLAY_REVISION_STALE
  And RTK Query's invalidatesTags starts a background re-read of the same overlay
  Then the Save control is disabled while that re-read is in flight
  And exactly one PATCH has been issued in total
```

```gherkin
Scenario: Create mode is untouched                                                # bad-request / regression
  Given the overlay editor is open in create mode
  Then the chain query is skipToken and chainFetching is inert
  And Save's availability is unchanged from today
```

Auth: **no auth surface.** This changes a `disabled` attribute in an
already-authenticated dialog; no new call, route, scope or token. The
overlay-write scope already gates `editDraftOverlayRevision` server-side and is
unaffected. Recorded as considered, not skipped.

### US2

```gherkin
Scenario: The layout dialog's existing guard is pinned                            # characterisation, GREEN today
  Given the layout editor is open on a draft revision whose chain read answered version 7
  And a save has been refused with a 409 LAYOUT_REVISION_STALE
  When the operator clicks Reload
  And the chain re-read is in flight
  Then the Save control is disabled
  And no PATCH carrying If-Match "7" has been issued
```

```gherkin
Scenario: The pin actually pins                                                   # counterfactual
  Given the layout dialog's Save predicate with "chainFetching" deleted
  When the layout dialog's test files are run
  Then the scenario above fails
```

## 6. Functional requirements

- **FR-001** `OverlayEditorDialog`'s Save control MUST be unavailable while a
  chain re-read for the current overlay is in flight, in edit mode.
- **FR-002** The predicate MUST match `LayoutEditorDialog`'s shape for the terms
  the two dialogs share — one added `chainFetching` term, nothing else. The
  layout-only `knownCameras` term is not copied.
- **FR-003** Create mode MUST be unaffected: the `isEdit &&` guard stays.
- **FR-004** No new control, label, wording or affordance. The operator is
  already told what is happening: `ChainRecoveryNotice` renders a live
  "Re-reading the overlay…" status for the same `reReading` flag (`:293`), so
  the disabled Save is explained without anything new being added.
- **FR-005** `LayoutEditorDialog`'s existing `chainFetching` term MUST be pinned
  by an assertion that fails when the term is deleted. No production behaviour
  change on the layout side.
- **FR-006** `LayoutEditorDialog.tsx:412-413`'s claim *"Neither is pinned by a
  test in this repo"* MUST be corrected once FR-005 lands. A comment that records
  the opposite of what the repository does is the exact defect class `CLAUDE.md`
  documents twice over.

## 7. Phase 4a declaration (ADR-0144, constitution §Testing)

Two obligations, one per story. The test-writer must treat them differently and
must not merge them into one file.

| Story | Colour | Why |
|---|---|---|
| **US1** | **RED — behaviour-changing** | Save must become disabled where it is enabled today. The test MUST be observed failing against unmodified `OverlayEditorDialog.tsx` and the failure quoted in the PR (ADR-0139). A US1 test that arrives green is a phase-4 failure. |
| **US2** | **GREEN — characterisation** | No production code changes on the layout side. The test is captured passing *before* anything is touched and MUST pass **unmodified** afterwards. An assertion that needs editing is evidence behaviour moved: block, do not adjust. |

This is **not** "a refactor that is also a bug fix" — there is exactly one
production edit (FR-001) plus one comment correction (FR-006). US2 adds a test
over code that does not change.

## 8. Independent end-to-end test procedure

Automated tests are the gate; this is the phase-5 observation, run by hand.

1. Boot the stack: `dotnet run --project src/AppHost` (one stack per machine).
2. In `management-web`, open **Overlays**, pick an overlay with a draft revision,
   click **Edit**.
3. In a second browser profile (or via the API with a second token), edit the
   same draft revision so the server's version advances.
4. Back in the first window, change the label and click **Save**. Observe the
   stale-version alert and the **Reload** control.
5. With devtools' Network panel throttled (or the gateway paused), click
   **Reload**. **Observe:** Save is greyed out and unclickable for the whole
   in-flight window, and the status line reads "Re-reading the overlay…".
6. Let the re-read land. **Observe:** Save re-enables; saving now succeeds.
7. **Before/after evidence:** step 5 on `develop` shows Save clickable and a
   second PATCH going out with the old `If-Match`; on the branch it does not.
   One PATCH total in the Network panel is the observable.

## 9. Locked tech choices

React + TypeScript + Vite (ADR-0074) · Redux Toolkit + RTK Query (ADR-0075) ·
Radix primitives (ADR-0077) · Vitest + Testing Library, the conventions already
used by the five `OverlayEditorDialog*.test.tsx` files. No new dependency, no new
pattern, no new file in `src/`.

## 10. Out of scope, restated for the reviewer

No ADR is needed. No backend change. No contract change. No migration. No
`Shared.Contracts` touch. No e2e (Playwright) test — the window under test is an
in-flight RTK Query state, which the component tests drive deterministically and
a real browser cannot hold open reliably.
