# Spec 305: The editor that had to fit a dialog

**Issue:** #2350 · **Branch:** `feat/2350-overlay-editor-route` (cut from `develop` at `4c57cd73`)
**ADRs:** ADR-0074 (two apps; the console owns this surface), ADR-0146 (the console's
treatment, which lists "View Transitions between routes" — *deferred*, see §Out of scope),
ADR-0113 (If-Match from the chain's re-read version — unchanged), ADR-0151 (`unavailable`, not
`disabled` — unchanged), ADR-0166 (outcomes stay inline — governs where a save's outcome
shows), ADR-0139/ADR-0144 (two-colour phase 4a). Constitution §IV (N/A, below), §Testing.
**No new ADR.** The route-vs-modal choice was made by the product owner on the issue; this
spec implements it. Nothing here picks a library, a pattern or a boundary that is not already
in the repo (`createBrowserRouter` 7.18 + `ShellLayout` + `useParams`, as
`cameras/:cameraIdentifier` and `walls/:wallIdentifier` already do).

**Phase 4a colour: two colours, split per artefact** (plan.md §Phase 4a). The editing
behaviour is CHARACTERISATION; the container, the URL and the navigation are RED.

**Latency budget (§IV): N/A.** `apps/management-web` is not on the event→overlay path; no leg
is touched.

---

## The premise, checked (against `4c57cd73`)

- `OverlayEditor` (`apps/shared/src/ui/composites/OverlayEditor.tsx`) **already takes**
  `canvasWidthPx` / `canvasHeightPx` props; 800×450 is only their *default* (`:257-258`), and
  the "fit inside a Dialog" reasoning is a doc comment (`:24-28`). Nothing inside the
  component needs to change to get a bigger canvas — the caller simply never passes one.
- The label's type scales with the canvas, not the viewport (`overlayLabelStyle.ts:59`,
  `cqw` against the canvas's `@container`), and geometry is stored normalised. **A bigger
  canvas is WYSIWYG-safe**: the same stored label renders proportionally identical at any
  canvas width. `e2e/overlay-editor-preview-scale.spec.ts` already measures relative to the
  canvas, so it is width-independent.
- The finding is worse than the issue states: the shared `Dialog` primitive is
  `max-w-md` (448 px, `Dialog.tsx:33`), so today's 800 px canvas **overflows its own modal**.
- `OverlayEditorDialog` does **two jobs**, not one: create (`New overlay`, no identifier
  exists yet) and edit-a-draft (`Edit draft`, and `Edit (new draft)` after a branch). Both
  host the same canvas, so both move — a route for edit alone would leave the precision
  canvas in an overflowing modal on create.
- `OverlaysPage` mounts the dialog twice (`:366-373`) and seeds edit mode from the list
  (`elementsOf(draft|baseline)`). A URL carries no such seed, so on a route the page must
  read the draft itself — the dialog **already** issues `useGetOverlayQuery(overlayIdentifier)`
  for its If-Match version (`OverlayEditorDialog.tsx:118-123`), so the read exists; only its
  use as the seed source is new.
- A chain can carry two open drafts (`OverlaysPage.tsx:220-223`, "recorded as observed").
  The URL therefore names the revision, or a deep link would be ambiguous.

## Scope decision (the critical question)

**This spec moves the existing editor into a routed page and removes the modal-imposed size
cap. It builds none of the affordances the issue lists as future needs.** The toolbar,
inspector, label list, zoom/fit, live frame, tile-aspect canvas and ADR-0146 visual treatment
belong to #2340, #2341, #2345–#2349 and later work; the page lays its existing controls out
exactly as the dialog did (canvas, then the existing controls stacked below), only wider.

Moving behaviour-identically **is** possible, with one unavoidable, declared change: the
edit page seeds from the server read instead of from list state (a URL has no list state).
The read and the If-Match version come from the same `GET /overlays/{id}`, so the seed is
the draft revision's stored elements — the same values the list held.

## User stories

### US1 (P1) — Edit a draft on its own page

As an operator, when I choose **Edit draft** (or **Edit (new draft)**, after its branch
succeeds), I land on `/overlays/{overlayIdentifier}/revisions/{revisionNumber}/edit` — a page
inside the console shell, not a modal — with every editing control that existed in the
dialog, a canvas that uses the page's width, and the URL surviving a reload.

### US2 (P1) — Create an overlay on its own page

As an operator, **New overlay** takes me to `/overlays/new`, where I name and lay out the
overlay exactly as in the dialog; **Save as draft** returns me to the list.

US1 and US2 ship together in one PR: deleting the dialog is only possible when neither mode
needs it, and leaving one mode in an overflowing modal is the defect this issue names.

## Acceptance scenarios

```gherkin
Feature: The overlay editor is a routed page

  Background:
    Given I am signed in to the management console as an operator

  # --- navigation (RED) ---
  Scenario: Edit draft navigates rather than opening a modal
    Given overlay "Line 3" has an open draft revision 2
    When I choose "Edit draft" on its row
    Then the URL is "/overlays/{id}/revisions/2/edit"
    And no element with role "dialog" is present
    And the console navigation is visible with "Overlays" marked current

  Scenario: Edit (new draft) branches, then navigates to the branched revision
    Given overlay "Line 3" has only published revision 1
    When I choose "Edit (new draft)" and the branch answers revision 2
    Then the URL is "/overlays/{id}/revisions/2/edit"

  Scenario: A refused branch does not navigate
    When I choose "Edit (new draft)" and the branch is refused
    Then I stay on "/overlays" and the row's existing fault notice shows

  Scenario: New overlay navigates to the create page
    When I choose "New overlay"
    Then the URL is "/overlays/new" and the Name field has focus

  # --- deep link / reload (RED) ---
  Scenario: The edit page survives a reload
    Given I am on "/overlays/{id}/revisions/2/edit"
    When I reload the page
    Then the editor shows revision 2's label text and geometry as stored

  Scenario: Save returns to the list
    Given I am on the edit page and have moved the label
    When I choose "Save draft" and the PATCH succeeds
    Then the URL is "/overlays"

  Scenario: Cancel returns to the list and sends nothing
    When I choose "Cancel" on the edit page
    Then the URL is "/overlays" and no PATCH was sent

  # --- the canvas (RED) ---
  Scenario: The canvas uses the page's width
    Given a 1920x1080 viewport
    When I open the edit page
    Then the canvas is wider than 800 px and its width:height ratio is 16:9
    And the label's on-canvas position is the stored normalised geometry times the canvas size

  # --- bad request (RED) ---
  Scenario: The named revision is not a draft
    Given revision 1 of "Line 3" is Published
    When I open "/overlays/{id}/revisions/1/edit"
    Then I see "This revision is no longer a draft." with a link back to Overlays
    And no editor and no Save control is rendered

  Scenario: The overlay does not exist
    When I open "/overlays/{unknown-id}/revisions/1/edit" and the GET answers 404
    Then I see "This overlay does not exist." with a link back to Overlays

  Scenario: The revision segment is not a positive integer
    When I open "/overlays/{id}/revisions/abc/edit"
    Then I see the not-found notice and no GET for the chain is sent

  Scenario: The chain read fails
    When the GET answers 5xx
    Then I see a retry banner, and Retry re-issues the read

  # --- conflict (CHARACTERISATION — existing behaviour at the new host) ---
  Scenario: A stale save still offers Reload, never Retry
    Given another operator saved the same draft after my page loaded
    When I choose "Save draft" and the PATCH answers 409 OVERLAY_STALE
    Then I stay on the edit page with the existing conflict notice and Reload
    And my in-progress edits are not reseeded by the conflict's refetch

  Scenario: A name clash on create keeps me on the create page
    When I save a new overlay whose name is taken
    Then I stay on "/overlays/new" with "That overlay name is already taken."

  # --- auth (unchanged; stated so it is not assumed) ---
  Scenario: Signed-out deep link
    Given I am signed out
    When I open "/overlays/{id}/revisions/2/edit"
    Then the console's existing OIDC sign-in redirect applies, as for every other route
```

Auth is not new: the route sits under the same `ShellLayout` as every console route, and the
PATCH/POST scopes are unchanged. A deep link after OIDC sign-in returning to the origin root
rather than the deep link is existing console-wide behaviour (`router.tsx:14-18`), not this
spec's to change.

## Functional requirements

- **FR-001** Routes `overlays/new` and `overlays/:overlayIdentifier/revisions/:revisionNumber/edit`
  are registered as children of `ShellLayout` in `router.tsx`, each with `errorElement: <SurfaceCrash />`.
- **FR-002** `New overlay`, `Edit draft` and a successful `Edit (new draft)` navigate; none
  opens a dialog. `OverlayEditorDialog` is deleted.
- **FR-003** The edit page seeds the form **once**, from the first successful read of the
  chain, using the named revision's `elements` (whole set — spec 150 FR-018 / spec 300
  FR-019 unchanged) and the chain's `name`. Later refetches (Retry, the conflict's tag
  invalidation) update the If-Match version only and **never reseed** the form.
- **FR-004** Save success and Cancel both navigate to `/overlays`. Browser Back behaves as
  navigation, discarding unsaved edits — the same outcome as Esc/Cancel on the dialog today.
- **FR-005** Every editing behaviour of the dialog is preserved: the Text element found by
  kind, the shapes-only notice, the debounced placeholder resolve, the save gate
  (`saveBlocked`, `aria-disabled`, form-submit guard), `ChainRecoveryNotice` wiring and
  focus-to-Save on recovery, error texts keyed on problem code, stable `getToken`.
- **FR-006** The canvas is 16:9, as wide as its container, measured with `ResizeObserver`;
  when no width can be measured (jsdom, first frame) it falls back to `OverlayEditor`'s own
  800×450 default. `OverlayEditor` itself is not changed except its stale doc comment.
- **FR-007** Not-a-draft, unknown overlay (404), malformed `revisionNumber`, and a failed
  read each render the notices in the scenarios above, with no Save control.
- **FR-008** The Overlays nav entry stays current on both new routes.

## Independent end-to-end test procedure

1. Boot the stack (`dotnet run --project src/AppHost`), sign in to management-web as an operator.
2. Overlays → **New overlay** → URL is `/overlays/new`; no modal; canvas spans the content width.
3. Name it, drag the label, **Save as draft** → back on `/overlays`, row shows the draft.
4. **Edit draft** → URL `/overlays/{id}/revisions/1/edit`. Reload → the label is where you left it.
5. Move it, **Save draft** → back on the list. Re-open → new position persisted.
6. Paste the URL with `/revisions/abc/edit` and with an unknown GUID → the notices, no editor.
7. Two tabs on the same draft; save in one, save in the other → conflict notice + Reload; edits intact.

## Out of scope (owned elsewhere)

Toolbar, inspector, label list (#2345), snapping/guides/align (#2346), undo placement
(#2347, its buttons stay where `OverlayEditor` renders them), z-order (#2348), primitive
selection (#2349), live camera frame (#2340), placeholder editing UI (#2341), zoom/fit,
a canvas at the tile's real aspect (not 16:9 — needs a tile to be chosen, which this spec has
no input for), ADR-0146 depth/materials/View Transitions, and an unsaved-changes prompt on
navigation (new behaviour; would be its own issue).

## Assumptions (marked)

- **[A1]** URL shape `/overlays/:id/revisions/:n/edit`, not the issue's illustrative
  `/overlays/:id/edit` — because a chain can hold two drafts and because `Edit (new draft)`
  must land on the revision the branch returned, not whichever draft a lookup picks.
- **[A2]** Save and Cancel go to `/overlays` (not `navigate(-1)`), so a deep-linked page has a
  defined exit.
