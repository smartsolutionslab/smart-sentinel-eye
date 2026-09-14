# Tasks 152 — An overlay draft you can edit

Implements `plan.md`. **Phase 4a colour: red** — every test in T001-T005 must be
observed **failing** before any of T006-T010 is written, and the verbatim output
goes in the PR body (ADR-0139, ADR-0144).

`[P]` = may run in parallel; the marked tasks own disjoint files (ADR-0109).

**Foundational and blocking:** `T008` defines `OverlayEditTarget` in
`OverlayEditorDialog.tsx`, which `OverlaysPage.tsx` imports. Every page-side task
depends on it. Nothing else in this spec fans out — US1 and US2 both touch the
same two source files, so they are sequential by construction, not by choice.

---

## Phase 4a — tests first, observed red

Written by `test-writer`, run, and the failure output returned verbatim. The
engineer receives that output as its brief and **may not edit these tests to
pass**.

| ID | P | Story | Task |
|---|---|---|---|
| **T001** | | US1 | In `OverlayEditorDialog.test.tsx`, a **new** `describe('OverlayEditorDialog — edit')` block (the create blocks are guards and stay untouched). Render with an `editTarget`. Assert: the text field is seeded from the target's label; **no** Name field is rendered; the title reads `Edit overlay draft`; the description names the revision number and the overlay. Mock `useGetOverlayQuery` to return a chain at a known version and `useEditDraftOverlayRevisionMutation`. |
| **T002** | | US1 | Same file, same new block: submitting calls `editDraftOverlayRevision` **exactly once** with `{ overlayIdentifier, revisionNumber, version: <the queried version>, label }`; the body carries **only** `label` (no `name`); the dialog closes on success. **The version assertion is the point** — use a queried version that is *not* the list's and *not* `list + 1`, so `version + 1` fails here (FR-012). |
| **T003** | | US1 | Same file: (a) Save is disabled while the chain query has not resolved, and while it has errored, with an alert offering a retry (FR-013); (b) a 409 `OVERLAY_REVISION_STALE` shows the detail plus a **Reload** that refetches and sends no write, and never the words "try again"; (c) a 409 `OVERLAY_REVISION_NOT_DRAFT` says the revision is no longer a draft and offers Reload; (d) clearing the text blocks submit with a validation message and calls no mutation. |
| **T004** | `[P]` | US1 | In `OverlaysPage.test.tsx`: add `useEditDraftOverlayRevisionMutation` + `useGetOverlayQuery` to the `vi.mock` factory (it spreads `...actual`, so an unmocked hook reaches real RTK Query). Then: a `{D}` chain renders **Edit draft**; clicking it opens the editor on that draft and **sends no request**; on a `{D,D}` chain it targets the **newest** draft. |
| **T005** | `[P]` | US1 | Same file, the exhaustive shape table (`:403`, `:440`): add `'Edit draft'` to `ACTION_LABELS` **and** to the expectation of all five draft-holding shapes — `{D}`, `{P,D}`, `{A,D}`, `{D,D}`, `{P,D,D}`. Adding it to only the expectations makes the table fail; adding it to only `ACTION_LABELS` makes the new button **invisible to the assertion**, which is the quiet way to pass and is forbidden. No shape may lose an action it offers today. |
| **T006** | `[P]` | US2 | Same file, the recovery block (`:317-377`): after a successful branch, the editor opens on the returned revision number, seeded from `live ?? newest`'s label. The two existing branch-payload assertions stay exactly as they are. Also: a **failed** branch opens nothing. |
| **T007** | `[P]` | US1 | In `e2e/overlays.spec.ts`, append one Playwright test: sign in, create an overlay with a unique name, click **Edit draft** on its row, change the text, **Save draft**, assert the row's summary shows the new text and the badge still reads `v1 · Draft`. Reuse `signInAsOperator`, `FIRST_WRITE_TEST_TIMEOUT_MS`, `FIRST_WRITE_TIMEOUT_MS`. Appends only — the file is also edited by open PR #2367. |

**Gate:** T001-T007 all red, for the stated reason (missing button / missing
mode), not for a setup error. A test red because a mock is wrong is not evidence.

---

## Phase 4b — implementation

Written by `frontend-engineer` against the verbatim red output.

| ID | P | Story | Task | Depends on |
|---|---|---|---|---|
| **T008** | | US1 | **Foundational.** `OverlayEditorDialog.tsx`: export `OverlayEditTarget = { overlayIdentifier, revisionNumber, name, label }`; add optional `editTarget` prop and `isEdit`; add `useEditDraftOverlayRevisionMutation` and `useGetOverlayQuery(editTarget?.overlayIdentifier ?? skipToken)`; mode-select `{ isLoading, error, reset }`. `DEFAULT_INPUT` and `overlays.schema.ts` unchanged. | T001-T003 red |
| **T009** | | US1 | Same file: `defaultValues` memo (create → `DEFAULT_INPUT`; edit → `{ name: target.name, label: target.label }`) + re-seed effect; hide the Name field in edit mode; title/description/submit-label per mode; point `labelText`'s fallback at the seed rather than `DEFAULT_INPUT.label.text`. | T008 |
| **T010** | | US1 | Same file: edit branch of `onSubmit` sending **only** `label` with `currentChain.version`; Save disabled until the version is read; alert + retry when the chain read fails. Do **not** copy `LayoutEditorDialog.tsx:183`'s silent `return`. | T009 |
| **T011** | | US1 | Same file: the three error branches on `problemCode`/`isStaleConflict` and the Reload control wired to `refetchChain`. Create-mode copy unchanged. | T010 |
| **T012** | | US1 | `OverlaysPage.tsx`: `editTarget` state, the **Edit draft** button on `draft !== undefined` (joining the existing `disabled` expression, sending nothing), and the second `<OverlayEditorDialog … editTarget>` render beside the existing one. | T008 |
| **T013** | | US2 | `OverlaysPage.tsx`: restore `newest` to the `chainView` destructure, add async `onEdit(chain, baseline)` mirroring `LayoutsPage.tsx:76-84`, wire it to the existing **Edit (new draft)** button with `baseline = live ?? newest`, and open the dialog only on success. **Rewrite the comment at `:130-136`** — it currently states the opposite of what the file will do. | T012 |

**Not tasks, and a task proposing one is wrong:** any edit under `src/`;
any edit to `overlays.api.ts`, `overlays.schema.ts`, or `OverlayEditor.tsx`;
renaming the existing **Edit (new draft)** button; converting anything to design
tokens (#2342); touching `LayoutsPage` or `LayoutEditorDialog`.

---

## Phase 4c — the guards

| ID | P | Story | Task |
|---|---|---|---|
| **T014** | | — | Run the four guard suites and confirm they pass **unmodified**: `OverlayEditorDialog.test.tsx`'s create + conflict + frame-capture blocks, `OverlayEditorDialogGetToken.test.tsx`, `OverlayEditorDialogResolvePreview.test.tsx`, and the five `apps/shared/.../OverlayEditor*`/`OverlayLabel*` suites. A guard needing an edit is a **phase-6 blocker**, not an adjustment — stop and report. |
| **T015** | `[P]` | — | `apps/management-web/src/App.test.tsx` — run it. Expected to pass untouched (empty chain list, no `editTarget`, `skipToken`). Edit only if it actually goes red, and say why in the PR. |
| **T016** | `[P]` | — | `npm run lint` + `npm run typecheck` (and `typecheck:e2e`) clean. If `typecheck:e2e` fails on a missing `@types/node` in the workspace root, that is a pre-existing `develop` failure — stash and re-run to confirm before touching config. |

---

## Phase 5 — verify

| ID | Story | Task |
|---|---|---|
| **T017** | US1 | Run `spec.md`'s *Independent end-to-end test procedure* against the booted Aspire stack. Confirm step 9 in the Aspire dashboard: **exactly one** `PATCH …/revisions/1` carrying `If-Match`, answered 200. Two would mean the ADR-0143 default was overridden. Write the verification note on the PR. **No latency figure is cited — this change is not on the §IV path**, and the note says so rather than omitting it. |

---

## Delivery

**One PR**, `feat/2364-an-overlay-draft-you-can-edit` → `develop`.

US1 and US2 both modify `OverlayEditorDialog.tsx` and `OverlaysPage.tsx`, so
splitting them means either a stacked PR (whose child must be retargeted before
the parent merges — CLAUDE.md's unrecoverable ordering) or two branches
conflicting in the same two files. Neither buys anything: US2 is roughly twenty
lines on top of US1's plumbing.

**If US2 is dropped** — legitimate, it is P2 — the PR body must record that a
`{P,D}` row then carries both **Edit draft** and **Edit (new draft)**, the
second of which still branches without opening the editor. That is a knowingly
accepted intermediate state, and it should be written down rather than
discovered by the next reader.

**Board:** #2364 is already on Project #13 (status In Progress), which satisfies
the Phase-3 gate. No per-task issues — the repo stopped creating those after
spec 028, and `tasks.md` is the artefact this work is tracked against.

**Commits:** Conventional Commits, **no `Co-Authored-By` footer** — ADR-0086,
which governs over any session-level attribution reminder. The PR body still
carries the Claude Code line.
