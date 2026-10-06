# Plan 305: The editor that had to fit a dialog

Spec: [spec.md](spec.md) · Issue #2350 · Frontend only (`apps/management-web`; one comment in `apps/shared`).

## Context and layers

No bounded context, entity, value object, contract, message or migration is touched. The
backend endpoints used are the ones the dialog already calls: `GET /overlays/{id}`,
`POST /overlays` (create draft), `PATCH .../revisions/{n}` (edit draft), `POST .../drafts`
(branch, still issued from the list). Boundary rules: unchanged — the feature folder imports
only `@smart-sentinel-eye/shared/*`.

## Shape of the change

```
features/overlays/
  OverlayDraftForm.tsx      NEW  — the dialog's body, verbatim: useForm, Controller+OverlayEditor,
                                   resolve query, chain query, saveBlocked, ChainRecoveryNotice,
                                   Cancel/Save. Props: { editTarget?, onDone(), onCancel(),
                                   canvasWidthPx?, canvasHeightPx? }. No Dialog import.
  OverlayEditorDialog.tsx   step 1: thin <Dialog> around OverlayDraftForm (title/description/
                                   reset-on-close stay here). Step 4: DELETED.
  OverlayCreatePage.tsx     NEW  — /overlays/new: header (back link, "New overlay", description),
                                   <OverlayDraftForm onDone/onCancel = navigate('/overlays')>.
  OverlayEditPage.tsx       NEW  — /overlays/:overlayIdentifier/revisions/:revisionNumber/edit:
                                   parse params → GET chain → resolve revision → seed once →
                                   <OverlayDraftForm editTarget=seed …>; else a notice.
  useCanvasFit.ts           NEW  — ResizeObserver on a container ref → { widthPx, heightPx } at
                                   16:9, or `undefined` when unmeasurable (OverlayEditor default).
  OverlaysPage.tsx          both <OverlayEditorDialog> mounts and `dialogOpen`/`editTarget`
                                   state removed; buttons call navigate(...).
app/router.tsx              two child routes under ShellLayout.
apps/shared/.../OverlayEditor.tsx   doc comment on canvasWidthPx/HeightPx only (no "Dialog").
e2e/overlays.spec.ts        `getByRole('dialog')` visibility/close assertions → URL assertions.
```

### Why extract a form rather than move the dialog's code into the page

~3,000 lines across eight `OverlayEditorDialog*.test.tsx` suites pin the editing behaviour
(save gate, chain recovery/retention/announcement, resolve preview, getToken stability,
reseed regression). Extracting first, with the dialog as a thin wrapper, lets those suites
run **unmodified** across the extraction — the characterisation proof. Only then are they
re-hosted onto `OverlayDraftForm` (render-harness lines only; assertion lines untouched,
checked by diff), and only then is the dialog deleted. Precedent for a feature-local form
composed by its page: `WallForm` in `WallsPage`.

### Edit page — seeding invariants

1. `revisionNumber` param must match `^[1-9]\d*$`, else not-found notice, and the query is
   `skipToken` (no request).
2. `useGetOverlayQuery(overlayIdentifier, { refetchOnMountOrArgChange: true })`, reading
   `currentData` (never `data` — `OverlayEditorDialog.tsx:102-111`'s trap applies unchanged:
   react-router reuses the page instance across a param change).
3. Revision not in `chain.revisions` → not-found; present but `state !== 'Draft'` →
   "This revision is no longer a draft." notice. 404 (read `error.status === 404` on the
   `FetchBaseQueryError`; `problemDetail.ts` has no not-found helper, and adding one is not
   needed for one call site) → "This overlay does not exist.";
   other error → `RetryBanner` → `refetch`.
4. **Seed once**: the form is rendered with `key={`${overlayIdentifier}/${revisionNumber}`}`
   and its `editTarget` is captured from the first `currentData` that resolves (held in a
   ref/state keyed by the same string), so a refetch never changes `defaultValues` — which
   is what `OverlayDraftForm`'s `useEffect(() => reset(defaultValues))` would otherwise turn
   into a reseed that wipes in-progress edits. The form's own chain query keeps supplying
   the If-Match version exactly as today (ADR-0113). (The page's query and the form's query
   share one RTK cache entry — one request, not two.)
5. After the form seeds, a later read that shows the revision left Draft (e.g. a conflict
   refetch) does **not** swap the editor for the notice; the form's existing
   `OVERLAY_REVISION_NOT_DRAFT` handling covers that case, as it does today.

### Canvas fit

`useCanvasFit(ref)`: observe the content-box width of the canvas's wrapper; `widthPx =
Math.floor(width)`, `heightPx = Math.round(widthPx * 9 / 16)`; `undefined` when
`ResizeObserver` is absent or width is 0, in which case the page passes nothing and
`OverlayEditor` keeps 800×450. The wrapper is the page's content column (full width of
`ShellLayout`'s outlet minus padding). No max-height clamp: the page scrolls, as every
console page does. A resize mid-drag is accepted (react-rnd reads the new size on the next
frame; geometry is normalised on stop).

### Navigation wiring in `OverlaysPage`

- `New overlay` → `navigate('/overlays/new')`.
- `Edit draft` → `navigate(`/overlays/${id}/revisions/${draft.revisionNumber}/edit`)`.
- `Edit (new draft)` → existing `branchDraft`; on success navigate to `…/revisions/${result.data}/edit`; on error unchanged (no navigation, page fault notice).
- `elementsOf` stays (still used? only by edit seeding → moves to `OverlayEditPage`; delete from the page if unused).
- The existing focus/`unavailable` behaviour of every row button is unchanged.

### Create page

Same form, no `editTarget`; `autoFocus` on Name already exists. Title "New overlay" and the
dialog's description move into the page header verbatim.

## Phase 4a — two colours

| Artefact | Colour | Proof |
|---|---|---|
| `OverlayDraftForm` extraction (T002) | **CHARACTERISATION** | all 8 `OverlayEditorDialog*.test.tsx` + `OverlaysPage.test.tsx` + shared `OverlayEditor*.test.tsx` captured green on `4c57cd73`, then green **unmodified** after T002. |
| Re-hosting the dialog suites (T003) | **CHARACTERISATION** | green against the form; `git diff` on those files shows only imports and render-helper lines; any edited `expect(` line blocks. Dialog-only cases (below) are not re-hosted. |
| Routes, pages, notices, canvas fit, navigation (T004–T009) | **RED** | new tests observed failing first, failure quoted in the PR. |
| `OverlaysPage` dialog-opening tests and e2e `getByRole('dialog')` steps | **RED (rewritten)** | these assertions describe the container, which is the behaviour that changes; rewritten to URL assertions and observed red against the pre-navigation page. |

**Dialog-only cases, retired with the dialog (not re-hosted, listed so the PR can name them):**
the close-resets-mutation-state effect and `open={false}` cases in
`OverlayEditorDialog.test.tsx`, and the `editTarget A → undefined → B` rerenders in
`OverlayEditorDialogChainRetention.test.tsx`. Their *behaviour* moves: unmount-on-navigate
replaces close-reset, and the A→B retention trap is re-pinned on `OverlayEditPage` with a
real `createMemoryRouter` navigating between two edit URLs on one mounted instance (pattern:
`CameraDetailPageNavigation.test.tsx`).

## Risks

- A re-hosted suite that passes only because the harness changed what it renders — guarded
  by the diff rule above and by keeping assertion lines byte-identical.
- e2e `overlays.spec.ts` stale-conflict test (`:427`) waits for the dialog to close; its
  wait becomes `toHaveURL(/\/overlays$/)`. Same claim, new container.
- Shard filters: the shard-filter rule is a .NET test note; confirm vitest is not sharded by
  file list before assuming new test files are picked up.
