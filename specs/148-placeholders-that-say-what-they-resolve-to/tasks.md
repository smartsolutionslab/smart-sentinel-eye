# Tasks 148 — Placeholders that say what they resolve to

**Spec:** `spec.md` **Plan:** `plan.md` **Issue:** #2341
**Branch:** `feat/2341-placeholders-that-say-what-they-resolve-to`
**Engineers:** `backend-engineer` (T001-T009), then `frontend-engineer` (T010-T021)
**Status:** Phase 3 complete — awaiting gate

**Phase 4a colour per task is in the `Colour` column.** GREEN = characterisation, captured
passing *before* the change and passing **unmodified** after; an assertion that has to be
edited is evidence the behaviour moved — block, do not adjust. RED = new behaviour, written
first, observed failing, the failure quoted verbatim in the PR (ADR-0139, ADR-0144).

**Commit discipline:** each commit builds on its own (ADR-0087 rebase-merge lands them
individually; a commit that compiles only with its successor breaks `git bisect` forever).
T002's extraction and T004's new handler are **separate commits** — that is the point of their
ordering, not a formality.

---

## Block A — SystemVariables backend (`backend-engineer`)

Files: `src/SystemVariables/**`, `tests/SystemVariables.Application.Tests/**`,
`tests/Integration.Tests/SystemVariables/**`. Disjoint from every other block.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T001** | | — | **GREEN** | **Capture the characterisation baseline.** Run `GetOverlaySnapshotQueryHandlerTests`, `PlaceholderParserTests`, `ResolverTests` and `TwoPlaceholdersInOneLabelTests` on the branch tip **before any edit**; record the verbatim passing output. These pin the resolution policy this spec may reuse but not change. No test is written here and none is modified. |
| **T002** | | US1 | **GREEN** | **Extract the snapshot-building loop.** New `src/SystemVariables/Application/Resolution/IVariableSnapshotBuilder.cs`, `VariableSnapshotBuilder.cs`, `PlaceholderResolution.cs` (+ `PlaceholderOutcome` enum: `Resolved`/`Unknown`/`Unset`/`Archived`). Move `BuildSnapshotAsync` and `FindInAnyFabAsync` out of `GetOverlaySnapshotQueryHandler` verbatim, returning `IReadOnlyList<PlaceholderResolution>`; the handler projects the `Resolved` entries into the dictionary `IResolver.Resolve` already takes and is otherwise unchanged. **Keep the `catch (ArgumentException) { continue; }` guard** — it is unreachable for `ExtractNames` output (the placeholder grammar is strictly narrower than `VariableName`'s) but it is the tripwire if the grammar ever moves; do **not** add a fifth outcome for it. Register in `SystemVariablesInfrastructureModule.cs` beside `AddSingleton<IResolver, Resolver>()` (line 60). **Do not touch `VariableValueChangedDomainEventHandler`** — plan.md §The extraction. **Gate: T001's four suites pass unmodified.** Its own commit. |
| **T003** | | US1 | **RED** | **Unit-test the four outcomes.** New `tests/SystemVariables.Application.Tests/Resolution/VariableSnapshotBuilderTests.cs`. One fact per outcome plus the two that only a list can express: a label mixing all four resolves each correctly, and a name held in two fabs resolves in the **first by ordinal fab name** carrying that fab on the result. Observed red (the builder's outcome mapping does not exist until T004's shape lands — if any of these is green on arrival, the mapping was written in T002 and T002's characterisation claim is void). |
| **T004** | | US1 | **RED** | **The query, errors and handler.** New `Application/Queries/ResolveOverlayTextQuery.cs` (`IReadOnlyList<FabIdentifier> Fabs, string Text`), `ResolveOverlayTextErrors.cs` (`ApiError` base + a `ResolveOverlayTextFailures` static factory returning the **base** type, ADR-0047's invariance rule — copy `GetOverlaySnapshotFailures`), and `Queries/Handlers/ResolveOverlayTextQueryHandler.cs`. The handler reads two fields, so it **destructures into locals as the first statement after `Ensure.That(query).IsNotNull()`** (CLAUDE.md; `HandlerDeconstructionTests` fails the build on a local named after a different field). It calls the T002 builder once and uses the result twice: projected for `IResolver.Resolve`, and mapped to the DTO. Its own commit. |
| **T005** | | US1 | **RED** | **The DTO, and the one field the whole design turns on.** New `Application/DTOs/ResolvedTextPreviewDto.cs` + `PlaceholderResolutionDto(string Name, string Outcome, string? Fab, string? RenderedValue)`. `Outcome` is a **string** on the wire, matching `VariableDto.State`/`.Type`. **`RenderedValue` comes from `VariableValue.Render(entry.BooleanLabels ?? BooleanLabels.Default)` — NOT `ToWireString()`.** Read `VariableValue.cs:74` and `:91` side by side first: `ToWireString` formats a number `G17` and a boolean `"true"`, `Render` formats shortest-round-trip and uses the truthy/falsy labels. Getting this wrong makes the preview quietly disagree with the wall, which is the defect #2341 was filed about. |
| **T006** | | US1 | **RED** | **Unit-test the handler.** New `tests/SystemVariables.Application.Tests/Queries/ResolveOverlayTextQueryHandlerTests.cs`. Covers spec scenarios 1-5 and 11: happy path; **`23.4` not `23.399999999999999`** for a `Number`; **`RUNNING` not `true`** for a `Boolean` with labels; all three unresolvable outcomes distinguished while `resolvedText` keeps every literal `{{name}}` unchanged; text with no placeholders returning it byte-for-byte with an empty list; and the ordinal fab tiebreak naming the fab that answered. |
| **T007** | | US1 | **RED** | **Map the endpoint.** `GET /system-variables/resolve` in `SystemVariableEndpoints.cs`, on the existing group, `.RequireAuthorization(Scope.Sse.Variables.Read)`, `.WithName("ResolveOverlayText")`. `text` required (empty/absent → `400 VARIABLE_INVALID_INPUT`, the shape `GetSnapshot` returns for `Guid.Empty`; > 256 chars → `400`); `fabId` optional, `""` default, through the **existing** `ResolveReadFabsAsync`/`FabResolution.ResolveForReadAsync` path — do not hand-roll fab handling. Declare `200`/`400`/`401`/`403`. **No `404`** — nothing can be absent; text referencing nothing is a `200` with an empty list. `EndpointScopeDeclarationTests` will check the scope is declared. |
| **T008** | | US1 | **RED** | **Integration-test the endpoint over the real stack.** New `tests/Integration.Tests/SystemVariables/ResolveOverlayTextTests.cs`, `[Collection(AspireCollection.Name)]`, **no `[Trait]`** — the CI integration job filters on `Category!=…` and every file in that directory carries none; adding one would silently remove the file from the only job that can run it. Covers spec scenarios 1-11: the happy path, the number and boolean formats against a real database, the three unresolvable outcomes, empty and over-long text, `401`, a token without `sse.variables.read` (mirror `VariableReadScopeIntegrationTests`), and `fabId` naming an unheld fab → `403 RESOURCE_FAB_NOT_AUTHORIZED`. |
| **T009** | | — | **GREEN** | **Re-measure leg 4.** Run `NFR_VariableResolutionLatencyTests` **twice** (the first run after machine churn reads exactly like a regression) and quote the printed median and worst in the PR. Its assertion is a loose 800 ms ceiling — 4× the constitution's 200 ms budget — so a real regression would pass it silently: **the figure is the evidence, not the green tick.** Confirm the extraction did not change the repository call count per snapshot. |

---

## Block B — the preview panel (`frontend-engineer`) — **blocked on T005**

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T010** | | — | **GREEN** | **Capture the frontend characterisation baseline.** Run `OverlayLabelParity.test.tsx`, `OverlayLabelCharacterisation.test.tsx` and `OverlayEditorDialog.test.tsx`, plus the two e2e setups `e2e/support/seed-bound-overlay-wall.setup.ts` and `seed-live-video-wall.setup.ts` (via one wall spec). Record verbatim. **These gate every wall spec in the repo** — they are the only mechanism that would catch a surface swallowing the *Save as draft* click. |
| **T011** | | US1 | **RED** | **The API client.** Add `resolveOverlayText` to `apps/shared/src/api/systemVariables.api.ts` + `useResolveOverlayTextQuery`, and the response schema to `systemVariables.schema.ts`. **`providesTags`: none** — a preview is derived and keyed by its own input; tagging it would invalidate on every unrelated variable write for nothing. Write that reason at the call site; it is a deliberate departure from the slice's other reads. Extend `systemVariables.api.test.ts`. |
| **T012** | | US1 | **RED** | **`placeholderAdvisories.ts` — a pure function, no hook, no DOM, no store.** Given the response and the raw text, return the rows to render. Unit-tested in isolation (`placeholderAdvisories.test.ts`): a `Resolved` row naming value and fab; `Unknown`, `Unset` and `Archived` rows whose wording is scoped to *what the editor can know* — **"no variable named X is defined in your fab(s)", never "this variable does not exist"** (spec §Decision 3: an overlay is a fab-neutral template, ADR-0115, and a name absent from the author's fabs may be present in the viewer's). |
| **T013** | | US1 | **RED** | **`PlaceholderPreviewPanel.tsx`.** Renders the resolved text and the T012 rows. `aria-live="polite"` for loading/empty/count, following `LayoutEditorDialog.tsx:305-314`. An error state that is one non-blocking notice: **the panel never affects form validity.** Tests cover spec scenarios 12, 13 and 15 — in particular that *Save as draft* stays enabled and the draft is created with the text exactly as typed when a reference does not resolve, and when the resolve request fails outright. |
| **T014** | | US1 | **RED** | **Mount it in `OverlayEditor.tsx`.** `useDebouncedValue(value.text)` (the repo's `DEBOUNCE_MS = 250`), `skip: !settledText.includes('{{')` — the same guard the kiosk applies at `CellPage.tsx:449`. **The input is driven from `value.text` and the query from the settled value, never the reverse** — `useDebouncedValue`'s own doc comment says the field would drop characters otherwise. **No `pollingInterval`.** Test spec scenario 14: text with no `{{` issues no request at all. **Contention file — see the ordering note below.** |

---

## Block C `[P]` — the pick list (`frontend-engineer`)

**Descoped from #2341's delivery — T015/T016 were never implemented, `VariablePickList.tsx` does not exist in the tree, and T020's US2 half is untested; US2 remains open for a later spec.**

Disjoint from Block B's files. Parallel with B after T010.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T015** | `[P]` | US2 | **RED** | **`VariablePickList.tsx`.** Reuses the existing `useListVariablesQuery`; filters **client-side** — `listVariables` is unpaged (`ListVariablesQueryHandler.cs:36` returns every matching row), so a request per keystroke buys nothing. Rows show name, type, current value, fab. Archived rows excluded (spec US2 scenario 4). `aria-live="polite"` count line. **No `role="listbox"`, no `aria-activedescendant`, no Radix Popover, no floating surface** — spec §US2's three reasons (spec 055's recorded decision at `LayoutEditorDialog.tsx:288-294`; the e2e `.fill()` constraint; `AelHelpPanel` is a static inline panel, not a typeahead). |
| **T016** | `[P]` | US2 | **RED** | **Insert at the caret, not at the end.** Via `selectionStart`/`selectionEnd` on the text input ref, then `setSelectionRange` and `focus()`. Tests: spec US2 scenarios 1, 2, 3, 6 — insertion mid-string with the caret preserved, the filter, and the empty-catalogue state. |

---

## Block D `[P]` — malformed references (`frontend-engineer`)

Wholly inside `placeholderAdvisories.ts` and its test. Disjoint from Block C.

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T017** | `[P]` | US4 | **RED** | **Flag brace-shaped text that is not a placeholder.** Match the **loose** shape `\{\{[^{}]*\}\}` in the raw text and flag any match whose inner text is **not** among the names the server returned. The strict grammar therefore stays only on the server and the client owns a heuristic, not a resolver — that distinction is the whole reason this is allowed at all. Tests: spec US4 scenarios 1-6 — `{{ temperature }}`, `{{1temp}}`, `{{camera-1}}` flagged; `{{temperature}}` never flagged; a valid-but-unknown `{{temperatuer}}` gets US1's `Unknown` row and **not** a malformed row; `{{{temp}}}` costs at most one extra row and never a refused publish. |

---

## Block E — the canvas, and the walk-through

| ID | P | Story | Colour | Task |
|---|---|---|---|---|
| **T018** | | US3 | **RED** | **The canvas shows the resolved string.** In `OverlayEditor.tsx`, `previewText = data?.resolvedText ?? value.text` into `<span data-testid="overlay-editor-preview">`, mirroring the kiosk's own `snapshot?.resolvedText ?? publishedOverlay?.text` (`CellPage.tsx:477`). Raw text is the fallback while in flight or on failure — never empty, never a placeholder of its own. **`overlayLabelSurfaceStyle(value)` is not touched.** Tests: spec US3 scenarios 1, 2, 3, 5. **Contention file.** |
| **T019** | | US3 | **GREEN** | **Prove the surface did not move.** `OverlayLabelParity.test.tsx` and `OverlayLabelCharacterisation.test.tsx` pass **unmodified** after T018 (spec US3 scenario 4). If either needs an edit, T018 changed the box's appearance and not only its contents — block. |
| **T020** | | US2 | **GREEN** | **Prove the wall suite still boots.** `seed-bound-overlay-wall.setup.ts` and `seed-live-video-wall.setup.ts` run **unmodified**: `.fill()` on `overlay-editor-text` (one input event, no per-key events) followed by a click on *Save as draft* still creates the draft with that exact text, with nothing intercepting the click (spec US2 scenario 5). Re-run one downstream wall spec end to end. |
| **T021** | | — | — | **Phase 5 walk-through.** Execute spec §"Independent end-to-end test procedure" steps 1-10 on a live stack and write the verification note. Step 8 is the one that matters: **compare the editor panel's predicted string against the wall's rendered string character by character** — that is the only step that proves the preview is not a second implementation. Step 9 records #2353 as an observed residual, not a pass. |

---

## Dependencies

```
T001 ──► T002 ──► T003 ──► T004 ──► T005 ──┬──► T006 ──► T007 ──► T008 ──► T009
                                            │
                                            └──► T010 ──┬──► T011 ──► T012 ──► T013 ──► T014 ─┐
                                                        │                                      │
                                                        ├──[P]──► T015 ──► T016 ───────────────┤
                                                        │                                      │
                                                        └──[P]──► T017 ─────────────────────────┤
                                                                                                │
                                                                              T018 ──► T019 ──► T020 ──► T021
```

- **T001 is foundational** and blocks everything: the characterisation baseline must be
  captured before the first edit, or there is nothing to compare against.
- **T005 blocks T010** and therefore the whole frontend — B cannot type the response it has
  not seen. This is the one place the stack serialises, and it is why the backend engineer
  goes first.
- **T002 → T003 → T004 → T005 are four separate commits.** T002's is the green one, T003-T005
  are red. Merging T002 into any of them voids its characterisation claim.

## The one contention file

**`apps/shared/src/ui/composites/OverlayEditor.tsx`** is touched by T014 (mount the panel),
T016 (mount the pick list and pass the input ref) and T018 (the preview span). **In that
order, one at a time, never by two workers at once.** Blocks C and D are `[P]` with respect to
B's *other* files; the mount steps are not.

If two agents are fanned out, C's parallel work is T015 only — T016's mount step waits for
T014 to land.

## The rebase, if PR #2357 merges first

`git fetch origin && git rebase origin/develop && git push --force-with-lease`. Three
conflicts, all in `OverlayEditor.tsx`, all resolved by **keeping both sides** — the sibling JSX
block, the preview span (this spec's version wins; #2357 changes only what is behind it), and
the props/`useState` header. Written out in `plan.md` §PR #2357.

**Then run `OverlayEditorCharacterisation.test.tsx` first.** #2357 added it to pin the
checkerboard gradient byte-for-byte precisely so a mangled rebase fails loudly. Green there
means the conflict was resolved correctly.

## Phase 3 gate (ADR-0037, as corrected in CLAUDE.md)

**No per-task issues.** Task issues stopped at spec 028 (`[TNNN]` runs to #1845); specs 029-047
carry a **feature-level** issue and track the work against `tasks.md`. The gate is that
**#2341 is on Project #13**, added by hand:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2341
```

`gh issue view 2341` already reports `projects: Smart Sentinel Eye (In Progress)` — **the gate
is satisfied; verify by `content.url` with `--limit 2000`, since `item-list` defaults to 30 and
the number filter returns zero.** Do not run `/speckit-taskstoissues`.
