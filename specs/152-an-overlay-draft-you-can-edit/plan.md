# Plan 152 — An overlay draft you can edit

Implements `spec.md`. Frontend only.

**Engineer:** `frontend-engineer`. Every changed file is under `apps/` or
`e2e/`. No C#, no Aspire resource, no migration, no infra.
**Phase 4a colour:** **red** (new behaviour).

---

## Bounded context and layers

**Context:** OverlayDesigner. **Layers touched: none of them.**

This is the unusual case where the plan's "bounded context + layers" section
records what is *not* changing, and that is the load-bearing statement.

| Layer | State | Action |
|---|---|---|
| `OverlayDesigner/Domain` | `Overlay.EditDraft(number, label, clock)` exists (`Overlay.cs:94`); `Revision.EditLabel` enforces the invariant | none |
| `OverlayDesigner/Application` | `EditDraftRevisionCommand` + handler + `EditDraftRevisionError` (four variants incl. `NotADraft`, `OverlayRevisionStale`) | none |
| `OverlayDesigner/Infrastructure` | persistence + EF concurrency token | none |
| `OverlayDesigner/Api` | `MapPatch(...)` at `OverlayEndpoints.cs:108`, `RequireAuthorization(Scope.Sse.Overlays.Write)`, `EditDraftRequest(LabelRequest Label)` | none |
| `Shared.Contracts` | no integration event is raised by an in-place draft edit, and none should be — a draft is not on any kiosk | none |
| `apps/shared/src/api` | `editDraftOverlayRevision`, `getOverlay`, `branchDraftOverlayRevision` all defined and exported | none |
| `apps/management-web` | the gap | **all of it** |

**Boundary rules:** unaffected. No cross-context project reference is added or
removed, so `NetArchTest` has nothing new to judge. The frontend continues to
reach OverlayDesigner through the API gateway (ADR-0106) via
`gatewayBaseQuery('overlay-designer/overlays')`.

**Entities, value objects, invariants:** none introduced. The invariants this
change relies on already live in the domain and are already tested there —
`Label`'s trim/length/normalised/font rules (spec 004 FR-005/FR-008), mirrored
client-side by `overlayLabelSchema`, and the Draft-only rule enforced by
`Revision.EditLabel` and surfaced as `OVERLAY_REVISION_NOT_DRAFT`.

**Messaging (domain → integration event):** none. Publish and Archive raise
integration events because they change what kiosks show; editing a draft
changes nothing a kiosk can see. Adding an event here would be speculative
generality (ADR-0036) and would put draft churn on the bus.

---

## Frontend design

Two files carry the change. They are **not** disjoint — both are in the same
feature folder and US1 and US2 both touch both — so nothing inside this spec is
`[P]` against anything else inside it (ADR-0109). See `tasks.md`.

### `apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`

One component, two modes (spec Decision 3).

**New exported type** — mirrors `LayoutEditTarget` and carries what the page
already knows, so the dialog needs no lookup to render its first frame:

```
OverlayEditTarget = { overlayIdentifier, revisionNumber, name, label }
```

`label` is the six `OverlayLabel` fields lifted off an `OverlayRevision`
(`OverlayRevision extends OverlayLabel`, `overlays.api.ts:18`). Lift them with a
small local helper rather than spreading the revision — a spread would carry
`state`, `createdAt`, `revisionIdentifier` and the rest into the form value and
then into the PATCH body.

**Props:** `{ open, onOpenChange, editTarget? }`. `const isEdit = editTarget !== undefined;`

**Hook order is fixed and unconditional.** The three existing hook groups stay
exactly where they are. Two are added:

1. `useEditDraftOverlayRevisionMutation()` beside the create mutation.
2. `useGetOverlayQuery(editTarget?.overlayIdentifier ?? skipToken)` — the
   version re-read (FR-011). `skipToken` from
   `@reduxjs/toolkit/query/react`, imported the same way
   `LayoutEditorDialog.tsx:8` imports it.

Mode-selected state, as `LayoutEditorDialog.tsx:66` does it:
`const { isLoading, error, reset: resetMutationState } = isEdit ? editState : createState;`
The existing close-resets-the-banner effect keeps working unchanged.

**Seeding.** `DEFAULT_INPUT` is untouched and stays the create seed. A
`defaultValues` memo returns `DEFAULT_INPUT` when `editTarget` is undefined and
`{ name: editTarget.name, label: editTarget.label }` otherwise, plus a
`useEffect(() => reset(defaultValues), [defaultValues, reset])` so a second open
against a different target re-seeds. `zodResolver(createOverlayDraftSchema)`
is unchanged — the seeded name keeps it valid even though the field is hidden
(spec Decision 2, property 2).

**Watch out:** `labelText` currently falls back to `DEFAULT_INPUT.label.text`
(line 78). In edit mode that fallback is wrong-but-harmless (it only applies
before the form initialises); make it fall back to the *seed's* text so a
placeholder in a saved label resolves on open rather than after the first
keystroke.

**Submit** branches on `editTarget`:

- edit — `editDraftOverlayRevision({ overlayIdentifier, revisionNumber,
  version: currentChain.version, label: value.label })`. Only `label`. On
  success, `reset(defaultValues)` and close.
- create — unchanged.

**Save is disabled until the version is known (FR-013).** `disabled={isLoading || (isEdit && currentChain === undefined)}`.
Do **not** copy `LayoutEditorDialog.tsx:183`'s `if (currentChain === undefined) return;`
silent no-op. If the chain query errored, render an alert with a retry
(`refetchChain`) rather than leaving a dead button unexplained.

**Error copy** — three branches on `problemCode`/`isStaleConflict`, not on the
status (ADR-0119, and `problemDetail.ts:63-81` explains why the status is never
consulted):

| Condition | Copy | Reload offered |
|---|---|---|
| `isStaleConflict(error)` | server detail, else `CONFLICT_FALLBACK` | yes |
| `problemCode === 'OVERLAY_REVISION_NOT_DRAFT'` | server detail, else *"This revision is no longer a draft. Reload to see its current state."* | yes |
| `problemCode === 'OVERLAY_NAME_TAKEN'` (create only) | unchanged | no |
| anything else | unchanged *"Could not save the overlay. Try again."* | no |

Reload calls `refetchChain()` and sends no write — the same shape as
`LayoutEditorDialog.tsx:333`.

**Chrome:** title `Edit overlay draft` / `New overlay`; description naming the
revision and overlay in edit mode; `{!isEdit && <FormField label="Name" …>}`;
submit label `Save draft` / `Save as draft`.

**Untouched inside this file:** the `getToken` ref block (lines 36-52) and the
resolve-preview block (lines 71-95). Both are pinned by their own suites and
both carry recorded bug fixes. Do not refactor them while passing through.

### `apps/management-web/src/features/overlays/OverlaysPage.tsx`

**US1.** Add `editTarget` state (`useState<OverlayEditTarget>()`), an
**Edit draft** button rendered when `draft !== undefined`, and a second
`<OverlayEditorDialog>` beside the existing one — the two-render pattern from
`LayoutsPage.tsx:333-340`:

```
<OverlayEditorDialog open={dialogOpen} onOpenChange={setDialogOpen} />
<OverlayEditorDialog open={editTarget !== undefined} onOpenChange={…} editTarget={editTarget} />
```

Both stay mounted; the edit one is inert while `editTarget` is undefined because
its chain query takes `skipToken`.

The button sets `editTarget` from `chain` + `draft` and **sends nothing**
(FR-002). It joins the existing `disabled` expression.

**US2.** Restore `newest` to the `chainView` destructure — removing it was
deliberate and its comment (`OverlaysPage.tsx:134-136`) names this spec's change
as the reason it would come back. **Update that comment; do not leave it
asserting the opposite of what the file now does.** Add an async `onEdit(chain,
baseline)` mirroring `LayoutsPage.tsx:76-84`: await the branch, return on
`'error' in result`, then `setEditTarget({ …, revisionNumber: result.data,
label: labelOf(baseline) })`, with `baseline = live ?? newest`.

### `e2e/overlays.spec.ts`

One new Playwright test: create → edit → assert the row's summary changed.
Uses `signInAsOperator`, `FIRST_WRITE_TEST_TIMEOUT_MS` / `FIRST_WRITE_TIMEOUT_MS`
exactly as the existing create test does.

**Why an e2e is not optional here.** Every component test in this feature mocks
the API module. A mocked-hook test cannot distinguish "the mutation is wired" from
"the mutation is exported" — which is the precise failure mode that let this
mutation sit uncalled across four specs. The e2e is the only artefact that
proves the PATCH leaves the browser, crosses the gateway, carries the token and
the `If-Match`, and is answered 200.

**Conflict note:** PR #2367 (spec 151, open) also edits `e2e/overlays.spec.ts`
and nothing else this spec touches. Appending a test at the end of the file is a
trivial rebase; whichever lands second rebases.

---

## Constitution and ADR alignment

- **§II value objects / primitive boundary** — no domain model changes; the
  `PrimitiveBoundaryTests` build gate is unaffected. `OverlayLabel` on the wire
  carries primitives, which is what `Shared.Contracts`/DTO shapes are exempted
  for.
- **§IV latency budget** — N/A, stated and reasoned in `spec.md`. No leg is
  built, changed, or claimed measured. §VII's dashboard obligation (ADR-0117)
  does not attach.
- **§Testing** — new behaviour, so red first (ADR-0139). The verbatim failure of
  the phase-4a run is quoted in the PR body. Nothing here is a refactor, so no
  characterisation lane applies.
- **ADR-0113** — the `If-Match` version comes from a server re-read. The plan
  forbids `version + 1` explicitly because it is the obvious wrong
  implementation and it *works* on a single-operator machine.
- **ADR-0142 / ADR-0143** — no `Idempotency-Key` (this is neither a create nor a
  rotation) and no `RetryEveryMethod()` (a human clicks Save once; the default
  one-attempt rule for `PATCH` is the behaviour we want).
- **ADR-0036 Karpathy** — smallest possible change; no new abstraction; no
  drive-by refactor of the two pinned blocks in the dialog; no new error
  handling beyond the two trust-boundary refusals the server actually returns.
- **ADR-0084 code metrics** — `OverlayEditorDialog.tsx` is 169 lines today and
  will land near 240. `OverlaysPage.tsx` is 339 and will land near 380. **The
  300-LOC limit is a SonarAnalyzer rule on C#, not on TSX** — `OverlaysPage.tsx`
  and `LayoutsPage.tsx` already exceed it — so this is not a gate. Recorded so
  nobody spends the slice extracting components to chase a limit that does not
  apply here.

---

## Risks

| Risk | Mitigation |
|---|---|
| `version + 1` instead of a re-read — correct on one machine, a lost update on two | FR-012 forbids it; a test asserts the PATCH carries the *queried* version, not the list's |
| The new button is added but omitted from `ACTION_LABELS`, so the exhaustive shape table silently stops covering it | Called out in `spec.md` *Tests that change*; a task asserts the table's expectation for `{D}` fails before the button exists |
| A test suite is edited to go green instead of the code | The four guard suites are named in `spec.md`; editing one is a phase-6 blocker, not an adjustment (ADR-0144) |
| The new hooks reach real RTK Query from a component test, because `OverlaysPage.test.tsx`'s mock spreads `...actual` | Mock both new hooks in that file's `vi.mock` factory |
| A drive-by refactor of the `getToken` ref or the resolve-preview block | Both named as untouched; both have dedicated suites that must pass unmodified |
| Scope creep into `LayoutsPage`'s identical hole, or into geometry-error surfacing | Both recorded in `spec.md` *Contradictions* as separate issues, not as work |
