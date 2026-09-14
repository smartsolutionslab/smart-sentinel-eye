# Spec 152 — An overlay draft you can edit

**Issue:** #2364 — *A saved overlay draft cannot be edited: the whole stack
exists and no UI calls it.*
**Branch:** `feat/2364-an-overlay-draft-you-can-edit` (cut from `origin/develop`,
nothing stacked)
**Lane:** autonomous (ADR-0144) — `agent:ready`, Project #13, status In Progress.
**Phase 4a colour:** **red.** New behaviour: a new action, a new mutation call,
a new dialog mode. A test that arrives green is a phase-4 failure (ADR-0139,
constitution §Testing).
**ADRs:** ADR-0113 (two-layer optimistic concurrency), ADR-0119 (`_STALE` codes),
ADR-0121 (an archived chain stays reachable), ADR-0089 (`ApiError` → RFC-7807),
ADR-0074/0075 (two React apps, RTK Query), ADR-0079 (RHF + Zod),
ADR-0143 (`PATCH` is not retried by default), ADR-0109 (`[P]` markers),
ADR-0037 (phases), ADR-0144 (lane).
**New ADR needed:** no. Every decision below is governed by an existing one, or
is a UI choice an ADR does not reach.

---

## What this is

An operator can open the overlay editor on an overlay that is already saved,
change the label, and save the change onto the same revision.

Today they cannot. The route into the editor exists exactly once, in the seconds
between clicking **New overlay** and clicking **Save as draft**. After that the
label is frozen for the life of the revision, and the only way to a different
label is a second overlay with a different name.

Every layer below the UI is built and reachable. `PATCH
/overlays/{id}/revisions/{n}` exists (`OverlayEndpoints.cs:108`), the domain
method exists (`Overlay.cs:94`), the RTK Query mutation exists
(`overlays.api.ts:117`), and its hook is exported
(`overlays.api.ts:150`). **Nothing calls the hook.** This spec calls it.

## What this is not

- **Not a backend change.** No file under `src/` is touched. The endpoint, the
  command handler, the domain method, the error catalogue and the migration all
  exist and are correct. If a task in `tasks.md` proposes a C# edit, it is wrong.
- **Not a change to the create path.** `DEFAULT_INPUT`, `createOverlayDraftSchema`,
  `useCreateOverlayDraftMutation` and the `New overlay` button behave exactly as
  they do today, asserted by three suites that must pass **unmodified** (see
  *Guards*).
- **Not editing a Published or Archived revision in place.** `EditDraft` is named
  for what it permits, and the domain refuses anything else with
  `OVERLAY_REVISION_NOT_DRAFT`. Reaching a published label is *branch, then edit*
  — which is the lifecycle publish/archive/branch/revert already govern.
- **Not design tokens** (#2342, gated on #2332). The new controls reuse the
  existing `Button` primitive and the existing Tailwind classes on this row.
- **Not undo** (#2347). This spec is the thing that makes #2347's premise true
  — a dialog that opens on something other than `DEFAULT_INPUT` — but it adds no
  undo of its own.
- **Not a fix for layouts.** `LayoutsPage` has the same hole for an already-open
  draft (see *Contradictions*, item 5). Out of scope; filed as an observation.

---

## What was verified, not assumed

The issue was filed with evidence. Five of its claims were re-checked against
`origin/develop` at `7c6a984e`. Three hold, one is materially wrong, and one
correction changes the design.

| Claim | Verdict |
|---|---|
| `useEditDraftOverlayRevisionMutation` has zero call sites in `apps/` | **Holds.** `grep -rn` returns only `overlays.api.ts:150`, the export. Not a call site, not a mock. |
| `PATCH /{id}/revisions/{n}` exists and permits drafts only | **Holds.** `OverlayEndpoints.cs:108`; `EditDraftRevisionError.NotADraft` → `OVERLAY_REVISION_NOT_DRAFT`, 409. |
| `useRevertOverlayRevisionMutation` is wired, so this is one orphan among used neighbours | **Holds.** `OverlaysPage.tsx:54`. |
| `useBranchDraftOverlayRevisionMutation` is "also unused by any UI" (prompt, not the issue body) | **Wrong.** Used at `OverlaysPage.tsx:53`, driving a button at `OverlaysPage.tsx:191`. |
| "There is no Edit action on any overlay row" (issue body) | **Wrong, and this is the important one.** There is a button labelled **"Edit (new draft)"**. |

### The correction that changes the design

`OverlaysPage.tsx:186-196` already renders a button reading **"Edit (new
draft)"**, shown when `live !== undefined || fullyArchived`. It calls
`branchDraft(...)` and then does nothing. The page's own comment says so, in
terms that read as a note left for this spec:

> `OverlaysPage.tsx:134-136` — *No `newest`: unlike LayoutsPage, Edit here
> branches without opening a designer, so there is no baseline to hand it.*

So the gap is narrower and sharper than "no Edit action": **overlays branch a
draft and abandon the operator there.**

And the consequence matters, because **copying `LayoutsPage` verbatim does not
fix the scenario the issue describes.** Walk it:

1. Operator clicks **New overlay**, fills it in, clicks **Save as draft**.
   `createOverlayDraft` creates revision 1 in state **Draft** — the dialog's own
   description says *"The overlay starts as a draft."*
2. The chain is now `{D}`. `chainView` gives `live = undefined`,
   `draft = rev1`, `fullyArchived = false`.
3. `LayoutsPage`'s edit condition is `live !== undefined || fullyArchived`.
   On `{D}` that is **false**.

The row offers **Publish** and **Discard draft**, and nothing else — which is
precisely what the operator reports. An "Edit (new draft)" button copied from
layouts is not shown on the shape that has the problem. The action this spec
needs is the one neither page has: **edit the draft that is already there.**

`OverlaysPage.test.tsx:406` pins this today: `['{D}', [rev(1, 'Draft')],
['Publish', 'Discard draft']]`.

---

## Latency budget (constitution §IV)

**N/A — this change is not on the event-to-overlay path.**

Saying so rather than over-claiming: §IV's `composite + render ≤ 50 ms` leg is
the **kiosk wall's** leg — `apps/kiosk-web` compositing a label over a decoded
frame. This spec changes `apps/management-web` (the operator console) only, plus
one Playwright spec. No file that runs on a wall is touched, and no shared
component's render path is altered: `apps/shared/src/ui/composites/OverlayEditor.tsx`
is *called* differently (a seeded `value` instead of `DEFAULT_INPUT.label`) but
not *changed*.

That leg is already over budget — ADR-0123 records p50 54.2 ms — so nothing may
be added to it casually. This adds nothing to it. The obligation under §VII
(ADR-0117) attaches to the spec that builds or changes a leg; this spec does
neither, and claims no measurement.

---

## User stories

### US1 — An operator fixes a label they already saved (P1)

*As a fab operator who has saved an overlay draft, I can reopen the editor on
that draft, change the label, and save, so that a mistake costs a correction
rather than a second overlay.*

This is the issue's literal scenario and the smallest shippable slice. It needs
no branch, no new schema, and no backend change. Shipped alone it closes #2364.

**Independently observable:** create an overlay, click **Edit draft**, change
the text, save, and the row's summary line shows the new text.

### US2 — "Edit (new draft)" lands the operator in the editor (P2)

*As an operator editing a published overlay, clicking **Edit (new draft)** opens
the editor on the new draft, seeded from the revision it branched off, so that
one intent is one interaction.*

**Why it is P2 and not P1:** once US1 ships, the branch button is no longer a
dead end — branch, the list refetches, the row shows a draft, and **Edit draft**
opens it. US2 removes a second click and brings overlays to parity with layouts.

**Why it is in this spec at all:** without it a `{P,D}` row carries two
edit-flavoured buttons where one does half the job. That is a knowingly worse
row than today's, and it would be created by US1. If US2 is dropped, that
intermediate state must be recorded in the PR, not discovered.

---

## The three decisions

### Decision 1 — Which revisions are editable, and what the row looks like

**Drafts are edited in place. Live and fully-archived chains branch first. Both
end in the same dialog.**

| Chain shape | Today | After US1 | After US2 |
|---|---|---|---|
| `{D}` | Publish, Discard draft | **+ Edit draft** | — |
| `{P}` | Edit (new draft), Revert, Archive | — | Edit (new draft) now *opens* the editor |
| `{A}` | Edit (new draft) | — | likewise |
| `{P,D}` | Publish, Discard draft, Edit (new draft), Revert, Archive | **+ Edit draft** | likewise |
| `{P,A}` | Edit (new draft), Revert, Archive | — | likewise |
| `{A,D}` | Publish, Discard draft | **+ Edit draft** | — |
| `{D,D}` | Publish, Discard draft | **+ Edit draft** (newest draft) | — |
| `{P,D,D}` | all five | **+ Edit draft** (newest draft) | likewise |

**Reasoning.**

- *Drafts in place, because that is what the endpoint permits.* `EditDraft`'s
  only refusal beyond not-found and stale is `NotADraft`. Offering the action
  where the server would refuse it is offering a button that cannot work.
- *No Edit on a Published or Archived revision that isn't a branch.* Publish,
  Archive, Revert and Branch already govern that half of the lifecycle, and
  ADR-0121 made a fully-archived chain recoverable **by branching**. A
  fourth route would be a second answer to a settled question.
- *The draft acted on is `chainView(...).draft` — the **newest** Draft, not
  "the" draft.* `chainView.ts:31-37` is explicit that a chain can hold several,
  and the app itself produces `{D,D}` (branch off a published revision, then
  revert it). Edit targets the newest, consistent with Publish and Discard
  on the same row, which already do.

**The button label is `Edit draft`, static.** On a `{P,D}` row it sits beside
the existing `Edit (new draft)`, and the two must be distinguishable:

- `Edit draft` — opens the draft that exists.
- `Edit (new draft)` — makes a new draft, then opens it. The parenthetical
  already carried that distinction before this spec.

**Rejected: a version-bearing label** (`Edit draft v2`). It reads better on the
row and costs more than it earns — `OverlaysPage.test.tsx:440` filters offered
actions by exact `ACTION_LABELS.includes(textContent)`, so a dynamic label
silently drops out of the exhaustive shape table, and the guard that exists to
catch a missing action would stop covering the new one. The precision goes where
it is actually needed instead: **the dialog's description names the revision and
the overlay** (Decision 2).

**Rejected: renaming `Edit (new draft)`.** It is asserted by name in
`OverlaysPage.test.tsx` (three places) and reverses a settled spec-037/038
wording decision, for a cosmetic gain.

### Decision 2 — What the dialog is seeded with, and how create stays untouched

**Seed:** `{ name: <chain name>, label: <the target revision's six label
fields> }`.

`OverlayRevision extends OverlayLabel` (`overlays.api.ts:18`), so a revision
already carries `text`, `normalizedX`, `normalizedY`, `normalizedWidth`,
`normalizedHeight`, `fontSizePx` flat. The seed picks exactly those six.

- **US1** seeds from the draft being edited.
- **US2** seeds from `live ?? newest` — the same revision the server branches
  from (`Overlay.cs:76-77`: `CurrentPublishedOrNull() ?? NewestWhenFullyArchivedOrNull()`),
  and the same rule `LayoutsPage.tsx:205` uses. `Revision.Branch` copies the
  baseline label verbatim, so the seed matches what the server actually stored.

**How the create path stays untouched — three properties, each testable:**

1. **`DEFAULT_INPUT` remains the create seed and is not edited.** The edit seed
   is a second value computed from `editTarget`, exactly as
   `LayoutEditorDialog.tsx:155-162` computes one beside `EMPTY_CREATE`.
2. **`overlays.schema.ts` is not touched.** No `editOverlayDraftSchema`, no
   mode-keyed resolver. In edit mode the **Name field is not rendered** (as
   `LayoutEditorDialog.tsx:273` does) but `name` is still seeded from the chain's
   real name, so `createOverlayDraftSchema` validates unchanged. Only `label` is
   sent — `PATCH` takes only `label` (`EditDraftRequest.cs:9`).
   *Alternative considered:* a separate edit schema and a union form type.
   Rejected — it forces a union through `Controller`'s generics for a
   four-line gain, and layouts solved the analogous problem without one.
3. **The three existing dialog suites render with no `editTarget`**, so they
   exercise create mode exactly as today and must pass **unmodified**.

**The dialog's description names what is being edited**, because on a `{P,D}`
chain there are two drafts' worth of ambiguity and the operator needs to know
which revision they are about to overwrite:

- create — unchanged: *"Pick a name, type the label, and drag it to position.
  The overlay starts as a draft."*
- edit — *"Editing draft v{n} of {name}. The change is saved onto this draft."*

Title: `New overlay` / `Edit overlay draft`.

### Decision 3 — One dialog with an optional `editTarget`, or two components

**One dialog.** The same choice layouts made, and the reasoning holds *more*
strongly here than it did there.

- The body being shared is not a form — it is a **live WHEP session**. The
  dialog holds a stable-identity `getToken` behind a ref
  (`OverlayEditorDialog.tsx:36-52`), and its comment records that a fresh
  function per render tears the session down and rebuilds it every render —
  *"the same failure that silently killed the decode sampler once already
  (issue 1889)"*. A second component is a second copy of that hazard, and the
  copy would have no test pinning it (`OverlayEditorDialogGetToken.test.tsx`
  covers this component, by name).
- A debounced placeholder-resolution query sits beside it
  (`OverlayEditorDialog.tsx:71-95`) whose comment explains why it lives in the
  dialog and not in `OverlayEditor`, plus two phase-6 blockers encoded as
  `currentData`/`settled` handling. Duplicating that is duplicating two fixed
  bugs.
- Specs 146-151 all land in or around this component. Two copies means every
  future change lands twice, and the day one lands once is the day they diverge.
- **The divergence between modes is genuinely small**: title, description, the
  name field's presence, which mutation fires, which 409 copy applies, and one
  `useGetOverlayQuery`. Six local branches.
- The component itself anticipated this. `OverlayEditorDialog.tsx:105-109`:
  *"Keyed on the code rather than the status so it stays right **if editing is
  added here later**."* That comment was written for this change.

**Where overlays genuinely differ from layouts, and why it does not flip the
decision:** create carries a uniqueness constraint edit does not, so the 409
means different things per mode (`OVERLAY_NAME_TAKEN` vs
`OVERLAY_REVISION_STALE` / `OVERLAY_REVISION_NOT_DRAFT`). That is four lines of
branching on `problemCode`, already half-written, and the existing comment
picked keying-on-code precisely so the branch would be safe to add.

**Rejected: extracting a shared `OverlayEditorFields` body under two thin
dialogs.** A new abstraction for two call sites, against "no speculative
generality" (ADR-0036), with no precedent in the sibling context.

---

## Functional requirements

**Row and routing**

- **FR-001** `OverlaysPage` renders an **Edit draft** action on any chain whose
  `chainView(...).draft` is defined, disabled while any mutation is in flight
  (the existing `disabled` expression).
- **FR-002** Clicking it opens `OverlayEditorDialog` in edit mode against that
  draft's `revisionNumber`, with **no write of any kind first**. No branch, no
  PATCH, no optimistic update.
- **FR-003** (US2) Clicking **Edit (new draft)** branches as it does today and,
  **only on success**, opens the dialog on the returned revision number seeded
  from `live ?? newest`. On failure it opens nothing and the existing
  `mutationError` banner surfaces the refusal, as now.
- **FR-004** `Edit draft` joins `ACTION_LABELS` in the exhaustive shape table
  and appears in the expectation for every shape whose `draft` is defined. A
  new action that the table does not list is invisible to the assertion that
  exists to catch a missing action.

**The dialog**

- **FR-005** `OverlayEditorDialogProps` gains one optional field,
  `editTarget?: OverlayEditTarget`, where `OverlayEditTarget` is
  `{ overlayIdentifier, revisionNumber, name, label }`. Absent means create.
- **FR-006** In edit mode the form is seeded per Decision 2, and re-seeded when
  `editTarget` changes between opens.
- **FR-007** In edit mode the **Name** field is not rendered. Submit sends only
  `label`.
- **FR-008** Title and description per Decision 2; the description names the
  revision number and the overlay.
- **FR-009** Submit button reads `Save draft` in edit mode, `Save as draft` in
  create mode (matching layouts' pairing).
- **FR-010** On success the dialog closes and the form resets to its seed. On
  failure it stays open with the label as typed.

**Concurrency (ADR-0113)**

- **FR-011** The `If-Match` version is **re-read from the server**, via
  `useGetOverlayQuery(editTarget?.overlayIdentifier ?? skipToken)`, and the
  PATCH sends `currentChain.version`.
- **FR-012** **The client never computes a version.** `version + 1` is
  forbidden, and so is reusing the version the page held. In US2 the branch is
  itself a write, so the page's version is already one behind; in US1 it merely
  might be. One rule for both, and it is the rule
  `LayoutEditorDialog.tsx:59-65` states.
- **FR-013** In edit mode **Save is disabled until the chain version has been
  read**, and a failed read surfaces an alert with a retry.
  *This deliberately does not copy `LayoutEditorDialog.tsx:183`*, whose
  `if (currentChain === undefined) return;` makes Save a silent no-op while the
  GET is in flight. A button that does nothing and says nothing is the defect
  class this programme keeps filing.
- **FR-014** A `_STALE` refusal (ADR-0119) shows `CONFLICT_FALLBACK` or the
  server's detail, plus a **Reload** control that refetches the chain — never
  "try again", which replays a stale intent over the other writer.
- **FR-015** An `OVERLAY_REVISION_NOT_DRAFT` refusal says the revision is no
  longer a draft and offers **Reload**. It must not say "try again" (retrying
  cannot succeed) and must not use the stale wording (nobody's version moved
  under them in the lost-update sense — the revision changed state).
- **FR-016** Create-mode error copy is unchanged: `OVERLAY_NAME_TAKEN` keeps
  "choose a different one", everything else keeps "try again".

**Not-negotiable non-changes**

- **FR-017** No file under `src/` is modified.
- **FR-018** `apps/shared/src/api/overlays.api.ts` and
  `apps/shared/src/api/overlays.schema.ts` are not modified. Every hook and type
  needed already exists and is exported.
- **FR-019** `apps/shared/src/ui/composites/OverlayEditor.tsx` is not modified.
- **FR-020** No `RetryEveryMethod()` opt-in is added. Under ADR-0143 `PATCH`
  gets one attempt, which is correct here: the caller is a human clicking Save,
  and a silent replay of a label edit is exactly what the default prevents.
  No `Idempotency-Key` either — ADR-0142's pattern covers creates and
  rotations, and this is neither.

---

## Acceptance scenarios (Gherkin)

### Happy path

```gherkin
Scenario: An operator corrects a label they just saved
  Given an overlay chain whose only revision is draft v1 with text "Line 1"
  When the operator clicks "Edit draft" on that row
  Then the overlay editor opens
  And its description names draft v1 and the overlay
  And the label text field contains "Line 1"
  And no Name field is rendered
  And no write has been sent

Scenario: The corrected label is saved onto the same revision
  Given the editor is open on draft v1 of overlay 1111...
  And the server reports the chain at version 7
  When the operator changes the text to "Line 2" and clicks "Save draft"
  Then editDraftOverlayRevision is called exactly once
  And it is called with overlayIdentifier 1111..., revisionNumber 1, version 7
  And its label carries text "Line 2" and the six label fields
  And the dialog closes

Scenario: The row shows the new text
  Given the edit succeeded
  When the overlay list refetches
  Then the row's summary line reads "Line 2"

Scenario: The newest draft is the one edited
  Given a chain holding draft v2 and draft v3
  When the operator clicks "Edit draft"
  Then the editor opens on revision 3

Scenario: Editing after branching (US2)
  Given a chain with published v1 and no draft
  And branching returns revision number 2
  When the operator clicks "Edit (new draft)"
  Then branchDraftOverlayRevision is called with the chain's version
  And the editor opens on revision 2
  And the label field is seeded from published v1's label
```

### Conflict

```gherkin
Scenario: Someone else moved the chain while the dialog was open
  Given the editor is open on draft v1
  When saving is refused with 409 OVERLAY_REVISION_STALE
  Then the banner shows the server's detail
  And a "Reload" control is offered
  And the words "try again" do not appear
  And the dialog stays open with the typed label intact

Scenario: Reload re-reads rather than resubmitting
  Given a stale refusal is showing
  When the operator clicks "Reload"
  Then the chain is refetched
  And no edit request is sent by that click

Scenario: The draft was published underneath the operator
  Given the editor is open on draft v1
  When saving is refused with 409 OVERLAY_REVISION_NOT_DRAFT
  Then the banner says the revision is no longer a draft
  And a "Reload" control is offered
  And the stale wording is not used

Scenario: A name collision still reads as a name collision
  Given the editor is open in create mode
  When creating is refused with 409 OVERLAY_NAME_TAKEN
  Then the banner tells the operator to choose a different name
  And no "Reload" control is offered
```

### Bad request

```gherkin
Scenario: An empty label is refused before a request is sent
  Given the editor is open on draft v1
  When the operator clears the text and clicks "Save draft"
  Then a validation message says the text is required
  And editDraftOverlayRevision is not called

Scenario: The version has not been read yet
  Given the editor has just opened on draft v1
  And the chain query has not resolved
  Then "Save draft" is disabled

Scenario: The version cannot be read at all
  Given the chain query failed
  Then "Save draft" is disabled
  And an alert says the overlay could not be read, offering a retry
```

### Auth / scope

```gherkin
Scenario: The edit carries the operator's bearer token through the gateway
  Given an operator signed in with sse.overlays.write
  When the edit is saved
  Then the PATCH goes to the gateway path overlay-designer/overlays/...
  And it carries the operator's bearer token
  And it carries If-Match with the chain's current version

Scenario: A caller without the scope is refused, and told so
  Given the endpoint requires sse.overlays.write
  When the server answers 403
  Then the dialog stays open and the banner shows the refusal
  And nothing in the row changes
```

*(No new scope, no new endpoint, no new authorization decision — the PATCH
already requires `Scope.Sse.Overlays.Write`, `OverlayEndpoints.cs:109`.)*

### Regression — the guards

```gherkin
Scenario: The create path is byte-identical
  Given the dialog is rendered with no editTarget
  Then it renders the Name field
  And its title is "New overlay"
  And submitting calls createOverlayDraft with { name, label }
  And no chain query is issued

Scenario: Every chain shape still offers everything it offered
  Given the exhaustive shape table
  Then no shape loses an action it offered before
  And every shape holding a draft gains exactly "Edit draft"
```

---

## Independent end-to-end test procedure

Runs against the real Aspire stack through the gateway (ADR-0103, ADR-0106).
This is the procedure a human repeats at Phase 5, and it is the **only** level
that proves the wiring — every component test in this area mocks the API hooks,
which is exactly how a mutation stayed exported and uncalled for four specs.

1. Boot the AppHost. Sign in to `management-web` as an operator.
2. Navigate to **Overlays**. The heading renders with no alert.
3. Click **New overlay**, set the name to `E2E Edit <timestamp>`, leave the
   default label, click **Save as draft**. The row appears, badge `v1 · Draft`,
   summary `Overlay text`.
4. **The row offers `Edit draft`.** (Before this spec it does not — this is the
   observable that fails on `develop`.)
5. Click it. The editor opens, described as *editing draft v1* of that overlay,
   with the text field reading `Overlay text` and **no Name field**.
6. Change the text to `E2E Edited`. Click **Save draft**. The dialog closes.
7. The row's summary line reads `E2E Edited`, and the badge still reads
   `v1 · Draft` — the same revision, not a new one.
8. Reload the page. The summary still reads `E2E Edited`, proving it was
   persisted rather than held in the cache.
9. Check the Aspire dashboard trace: one `PATCH
   /overlay-designer/overlays/{id}/revisions/1` carrying `If-Match`, answered
   200. Exactly one — not two, which would mean the retry default was
   overridden (ADR-0143).

**Latency:** not measured, and none is claimed. Not on the §IV path.

---

## Locked tech choices

| Concern | Choice |
|---|---|
| Where the change lives | `apps/management-web/src/features/overlays/` only, plus `e2e/overlays.spec.ts` |
| Data access | Existing RTK Query hooks — `useEditDraftOverlayRevisionMutation`, `useGetOverlayQuery`, `useBranchDraftOverlayRevisionMutation` |
| Form | React Hook Form + `zodResolver(createOverlayDraftSchema)` — unchanged (ADR-0079) |
| Concurrency | `If-Match` from a server re-read (ADR-0113); no client arithmetic |
| Error copy | `problemCode` / `isStaleConflict` / `problemDetail` / `CONFLICT_FALLBACK` (ADR-0089, ADR-0119) |
| Retry | Default — `PATCH` gets one attempt (ADR-0143) |
| Dialog shape | One component, optional `editTarget` (Decision 3) |
| UI primitives | Existing `Button`, `Dialog`, `Input`, `FormField` — no new primitives, no design tokens (#2342) |
| Tests | Vitest + RTL for components; Playwright for the gateway path |

---

## Guards — the tests that must pass unmodified

These pin behaviour this spec does not change. If one needs editing, the change
has gone further than it should and that is a phase-6 blocker, not an
adjustment.

| File | Why it must not move |
|---|---|
| `apps/management-web/src/features/overlays/OverlayEditorDialog.test.tsx` | Renders with no `editTarget`. Pins the create path: name input, default-label submit, blank-name validation, `OVERLAY_NAME_TAKEN` copy, and the whole spec-147 frame-capture block — including *"Submits a byte-identical `{ name, label }` payload whether or not a frame was captured"*. |
| `apps/management-web/src/features/overlays/OverlayEditorDialogGetToken.test.tsx` | Pins the stable `getToken` identity across re-render and silent renewal (issue 1889). Decision 3 rests on this staying green. |
| `apps/management-web/src/features/overlays/OverlayEditorDialogResolvePreview.test.tsx` | Pins the debounced placeholder resolve, including two phase-6 blockers. Adding a second mode must not disturb the `currentData`/`settled` handling. |
| `apps/shared/src/ui/composites/OverlayEditorCharacterisation.test.tsx`, `OverlayEditorKeyboard.test.tsx`, `OverlayEditorBackdrop.test.tsx`, `OverlayLabelParity.test.tsx`, `OverlayLabelCharacterisation.test.tsx` | `OverlayEditor` is not modified (FR-019). All five stay green untouched. |
| `apps/management-web/src/features/layouts/*.test.tsx` | Layouts is not touched. |

**Why they survive:** all three dialog suites wrap the component in
`<Provider store={store}>` (verified), so mounting `useGetOverlayQuery` is safe;
and with no `editTarget` it takes `skipToken`, so it issues no request and
changes no assertion.

### Tests that change, and must change visibly

Distinguished from the guards on purpose — a modified test needs a stated
reason.

| File | Change | Reason |
|---|---|---|
| `OverlaysPage.test.tsx:403` + `:440` (the shape table) | `ACTION_LABELS` gains `Edit draft`; the five draft-holding shapes gain it in their expectation | New action. **The trap:** the filter is `ACTION_LABELS.includes(textContent)`, so omitting the label from `ACTION_LABELS` makes the new button invisible to the assertion and the table passes while covering nothing. Adding it to both places is the whole point. |
| `OverlaysPage.test.tsx:317-377` (recovery block) | Two tests gain an assertion that the editor opens after branching | US2 only. The existing branch-payload assertions stay as they are. |
| `OverlaysPage.test.tsx:30-41` (the module mock) | Adds `useEditDraftOverlayRevisionMutation` and `useGetOverlayQuery` | The mock spreads `...actual`, so an unmocked new hook would reach the real RTK Query and fire a request from a component test. |
| `apps/management-web/src/App.test.tsx:110-126` | **Probably none — verify, do not pre-emptively edit.** | It renders the real `OverlaysPage` over an *empty* chain list, so no `Edit draft` button is reachable and the dialog mounts with no `editTarget` — `skipToken`, no request. The real mutation hook is inert until triggered, and the real store is present. Touch it only if it actually goes red. |

---

## Contradictions with the issue's own text, and with the brief

Recorded because both were written in good faith on a programme whose premise
has been wrong four times.

1. **"There is no Edit button on any overlay row" — wrong.** There is one,
   labelled `Edit (new draft)` (`OverlaysPage.tsx:194`). It branches and stops.
   The issue's evidence table is about `useEditDraftOverlayRevisionMutation`,
   which *is* uncalled; the prose generalised from the orphan mutation to the
   whole row.

2. **"`useBranchDraftOverlayRevisionMutation` … is, as far as I can tell, also
   unused by any UI" (brief) — wrong.** `OverlaysPage.tsx:53`, with a mocked
   `branchMock` asserted in three tests. Only the *edit* mutation is orphaned.

3. **The biggest one: copying `LayoutsPage` does not fix the reported
   scenario.** Layouts' Edit is gated on `live !== undefined || fullyArchived`.
   A freshly-created overlay is `{D}` — no live revision, not fully archived —
   so a faithful copy of the layouts pattern renders no Edit on the exact row
   the operator is complaining about. The spec's P1 is therefore the action
   *neither* page has.

4. **"`OverlayEditorDialog.tsx:148` historically surfaced only
   `errors.label.text` — spec 151 may have widened this" (brief).** It has not.
   PR #2367 touches `OverlayEditor.tsx`, `OverlayGeometryFields.tsx`,
   `normalizedPercent.ts` and `e2e/overlays.spec.ts` — **not**
   `OverlayEditorDialog.tsx`. On `develop` line 148 still surfaces only
   `errors.label.text`. Geometry errors remain unsurfaced by the dialog, which
   is latent rather than live: `OverlayEditor` clamps and quantizes, so the
   canvas cannot produce a geometry value the schema rejects. Not this spec's
   scope; noted for whoever files it.

5. **`LayoutsPage` has the same hole this issue reports, and it is not fixed
   here.** Branch a layout draft, cancel the dialog, and that draft is
   unreachable — layouts offers Publish and Discard on a draft row and no way
   back into the designer. Out of scope. Worth its own issue.

6. **A latent 500 on the branch path, unrelated but adjacent.**
   `Overlay.BranchDraft` throws `InvalidOperationException` when the chain has
   no published revision and is not fully archived. The row never offers the
   button in that state, so it is unreachable through the UI today — but it is a
   throw where every sibling failure is a `Result`. Not touched here.

7. **"A wall caps at 4 tiles, not 250" (brief) — noted and irrelevant to this
   spec.** No tile count, wall or grid appears anywhere in this change. Recorded
   so the next reader does not go looking for where it was applied. (#2363
   governs the correction.)
