# Plan 148 — Placeholders that say what they resolve to

**Spec:** `specs/148-placeholders-that-say-what-they-resolve-to/spec.md`
**Issue:** #2341 **Branch:** `feat/2341-placeholders-that-say-what-they-resolve-to`
**Status:** Phase 2 complete — awaiting gate

---

## Bounded contexts and layers

Two contexts, no new one, **no cross-context project reference**. The editor lives in
OverlayDesigner's UI and calls SystemVariables over HTTP through the gateway — the same
arrangement the kiosk already uses for `getOverlaySnapshot`
(`gatewayBaseQuery('system-variables/system-variables')`). NetArchTest's boundary rule is
untouched because no `.csproj` reference is added.

### SystemVariables — the only backend change

| Layer | File | Kind |
|---|---|---|
| Application / Resolution | `VariableSnapshotBuilder.cs`, `IVariableSnapshotBuilder.cs` | **new** — the extracted loop |
| Application / Resolution | `PlaceholderResolution.cs` | **new** — one name's outcome |
| Application / Queries | `ResolveOverlayTextQuery.cs`, `ResolveOverlayTextErrors.cs` | **new** |
| Application / Queries / Handlers | `ResolveOverlayTextQueryHandler.cs` | **new** |
| Application / Queries / Handlers | `GetOverlaySnapshotQueryHandler.cs` | **edited** — loop moves out |
| Application / DTOs | `ResolvedTextPreviewDto.cs` | **new** |
| Infrastructure | `SystemVariablesInfrastructureModule.cs` | **edited** — one registration |
| Api | `SystemVariableEndpoints.cs` | **edited** — one `MapGet` + one handler method |

**Untouched, and the reviewer's boundary check:** `PlaceholderParser.cs`, `Resolver.cs`,
`VariableValue.cs`, `VariableName.cs`, `IReverseIndex`, every EventHandler, every Command,
every migration. A diff touching any of them is a defect.

### Frontend — `apps/shared` and `apps/management-web`

| File | Kind |
|---|---|
| `apps/shared/src/api/systemVariables.api.ts` | **edited** — one endpoint + hook |
| `apps/shared/src/api/systemVariables.schema.ts` | **edited** — one Zod schema |
| `apps/shared/src/ui/composites/PlaceholderPreviewPanel.tsx` | **new** — US1 + US4 panel |
| `apps/shared/src/ui/composites/VariablePickList.tsx` | **new** — US2 |
| `apps/shared/src/ui/composites/placeholderAdvisories.ts` | **new** — pure mapping fn |
| `apps/shared/src/ui/composites/OverlayEditor.tsx` | **edited** — mounts both; US3 span |

**Why new files rather than growth in `OverlayEditor.tsx`.** It is 118 lines on `develop` and
**222 with PR #2357** (backdrop controls + camera picker). Three more concerns inline would put
it past 300 — the limit ADR-0084 sets for C# and that this repo's TypeScript has no analyzer
for, which is a reason to hold the line by hand rather than to ignore it. Separate files also
make the #2357 rebase a JSX-sibling conflict rather than a tangled one (§PR #2357 below).

`placeholderAdvisories.ts` is a **pure function**, not a hook: given the response and the raw
text it returns the rows to render. That is what makes US4's loose-brace heuristic unit-testable
with no DOM, no store and no fetch.

---

## Entities, value objects, invariants

**No new domain type. No aggregate change. No migration.** This spec adds a read, and a read
that reuses the domain unchanged. Recorded explicitly because "spec adds an endpoint" usually
implies a table.

Constitution §II (no primitives on a domain model) is **not** engaged: everything new is an
Application-layer DTO or query record, the category §II exempts alongside `Shared.Contracts`.
`ResolveOverlayTextQuery` carries `IReadOnlyList<FabIdentifier>` — the value object, matching
`GetOverlaySnapshotQuery` — plus the caller's `string Text`, which is wire input at a trust
boundary and stays a string exactly as `ListVariablesQuery`'s filters do.

### The one new Application type worth naming

```csharp
public sealed record PlaceholderResolution(
    string Name,
    PlaceholderOutcome Outcome,
    FabIdentifier? Fab,
    VariableSnapshotEntry? Entry);
```

`Outcome` is a four-case enum — `Resolved`, `Unknown`, `Unset`, `Archived` — and **four is the
complete set for this input**, which needs saying because `BuildSnapshotAsync` today has *five*
exits. Its first is `catch (ArgumentException)` from `VariableName.From`, unreachable here: the
placeholder grammar (`[A-Za-z][A-Za-z0-9_]{0,63}`, ASCII) is strictly narrower than
`VariableName`'s (`char.IsLetter`, Unicode-aware), so a name that came out of `ExtractNames` can
never fail `VariableName.From`. **Keep the guard — do not delete it and do not add a fifth
outcome for it.** If it ever throws, the grammar has changed and the build should say so.

**Invariants the builder must hold, all four inherited and none new:**

1. A name missing from every fab the caller holds → `Unknown`.
2. `State == Archived` → `Archived`, regardless of value.
3. `Value is VariableValue.Unset` → `Unset`.
4. Otherwise `Resolved`, and `Entry` is non-null with a non-`Unset` value — the precondition
   `VariableValue.Render` states in its own doc comment.

Outcomes 1–3 all render the literal `{{name}}` (FR-011). **The outcome distinguishes them; the
resolved text cannot.** That asymmetry is the endpoint's reason to exist.

---

## The extraction, and why the refactor comes first and alone

`GetOverlaySnapshotQueryHandler.BuildSnapshotAsync` (`:30-66`) plus `FindInAnyFabAsync`
(`:77-90`) **is** the resolution policy: the fab search order, the three skip rules, the
per-placeholder tiebreak. Writing a second copy for the preview would put the spec's own
decision-1 argument into the codebase as a defect. So it moves to
`IVariableSnapshotBuilder`, and both handlers call it.

```csharp
Task<IReadOnlyList<PlaceholderResolution>> BuildAsync(
    IReadOnlyList<FabIdentifier> fabs, string labelText, CancellationToken cancellationToken);
```

- `GetOverlaySnapshotQueryHandler` projects the `Resolved` entries into the
  `IReadOnlyDictionary<string, VariableSnapshotEntry>` that `IResolver.Resolve` already takes,
  and is otherwise unchanged. **Its externally observable behaviour does not move.**
- `ResolveOverlayTextQueryHandler` uses the same list twice: once projected for `Resolve`, once
  mapped to the DTO's per-name entries.

**One loop, two callers, zero policy duplication** — the shape spec 146 chose for the label
style, applied to the resolution rules.

**There is a third copy, and it is deliberately left alone.**
`VariableValueChangedDomainEventHandler.cs:100-142` repeats the same filtering for the push
fan-out. Folding it in would be correct and is **not done here**: that is the hottest code on
constitution §IV leg 4, it is reached from a message handler rather than a query, and changing
it would put a second behaviour-preserving refactor on the push path inside a PR whose headline
is new behaviour. ADR-0036, smallest change. Named so a reviewer does not read it as an
oversight, and filed as a follow-up below.

**Ordering is load-bearing.** The extraction is its own commit, landing green under the
existing tests, *before* the new handler exists. Under ADR-0087 rebase-merge each commit lands
individually on `develop`, so a commit that only compiles with its successor breaks `git
bisect` forever — and more to the point, an extraction proved green in the same commit that
adds a caller is an extraction nobody proved.

---

## The endpoint

```
GET /system-variables/resolve?text=<urlencoded>&fabId=<optional>
```

On the existing `MapGroup("/system-variables")`, `.RequireAuthorization(Scope.Sse.Variables.Read)`.

**Why `GET`.** It creates nothing, so ADR-0142 does not apply — no `Idempotency-Key`, no
`IdempotencyStore`, no `IdempotencyKeyTable` migration. And ADR-0143 retries only
RFC 9110-idempotent methods: as a `GET` this is retried by the standard resilience handler for
free, where a `POST` would need `RetryEveryMethod()` and a written justification for a call that
does not need one. 256 characters fit a query string with room to spare.

**Shape, deliberately mirroring `GetSnapshot` at `SystemVariableEndpoints.cs:58-70`:**

| | |
|---|---|
| `text` | `string`, required; empty or absent → `400 VARIABLE_INVALID_INPUT`; > 256 → `400` |
| `fabId` | `string`, optional, `""` default — resolved by `FabResolution.ResolveForReadAsync`, exactly as the three sibling reads |
| `200` | `ResolvedTextPreviewDto` |
| `400` | invalid text; or fab resolution yielding no usable fab |
| `401` / `403` | unauthenticated; missing scope, unheld fab, or no fab membership |

**No `404`.** Resolving text that references nothing is a `200` with an empty
`placeholders` list. There is no resource to be absent — that is the difference from
`GetSnapshot`, whose `404` means "this overlay was never published".

```csharp
public sealed record ResolvedTextPreviewDto(
    string ResolvedText,
    IReadOnlyList<PlaceholderResolutionDto> Placeholders);

public sealed record PlaceholderResolutionDto(
    string Name,
    string Outcome,          // "Resolved" | "Unknown" | "Unset" | "Archived"
    string? Fab,             // the fab it was found in; null when Unknown
    string? RenderedValue);  // null unless Resolved
```

`Outcome` is a **string** on the wire, matching `VariableDto.State` and `.Type` in the same
group rather than introducing a numeric enum the frontend would have to decode.

`Fab` is on the wire for the reason `VariableDto.Fab` is: a multi-fab operator otherwise cannot
tell which plant answered, and here the answer was chosen by an ordinal tiebreak they have no
other way to observe (ADR-0115, spec §Decision 4).

`RenderedValue` is `VariableValue.Render(...)`'s output — **not** `ToWireString()`. The two
differ for numbers (`G17` vs shortest round-trip) and for every boolean. This is the single
field the whole server-side decision turns on; the engineer must read `VariableValue.cs:74` and
`:91` side by side before writing it, and spec US1 scenario 2 exists to pin it.

---

## Frontend wiring

**RTK Query** (ADR-0075), one endpoint on the existing `systemVariablesApi`:

```ts
resolveOverlayText: builder.query<ResolvedTextPreview, ResolveTextInput>({ ... })
```

`providesTags`: **none.** A preview is derived, transient and keyed by its own input; tagging it
would invalidate it on every unrelated variable write for no benefit. This is a deliberate
departure from the slice's other reads and the reason is written at the call site.

**In `OverlayEditorDialog.tsx`** — corrected during phase 4a; this originally said
`OverlayEditor.tsx` and that does not work:

```ts
const settledText = useDebouncedValue(label.text);            // DEBOUNCE_MS = 250
const shouldResolve = settledText.includes('{{');
const { data, isFetching, isError } = useResolveOverlayTextQuery(
  { text: settledText },
  { skip: !shouldResolve },
);
```

The results pass down into `OverlayEditor` as props (`resolvedPreview`, `isResolving`,
`resolveFailed`); `OverlayEditor` itself stays Redux-free.

**Why the query cannot live in `OverlayEditor`.** Three suites render it bare, with no
Redux `<Provider>` — `OverlayEditorCharacterisation.test.tsx`,
`OverlayEditorBackdrop.test.tsx` and `OverlayLabelParity.test.tsx` — because until this
spec the component had no RTK Query dependency at all. Mounting the hook there fails all
three with *"could not find react-redux context value"*, 19 previously-green tests. Two of
those files are the characterisation baseline for specs 146 and 147, so wrapping them in a
`<Provider>` to make the hook fit would edit the very assertions that prove those refactors
did not move behaviour — CLAUDE.md: *an assertion that has to be edited is evidence the
behaviour moved: block, don't adjust.*

`OverlayEditorDialog` already has a `<Provider>` in its own test (it consumes
`useCreateOverlayDraftMutation`), so the real application path is unaffected either way.
Lifting the query is also the better shape on its own terms: `OverlayEditor` lives in
`apps/shared`, consumed by two apps, and a shared presentational component should not
assume a Redux store exists.

Three constraints, each with its reason:

- **`value.text` drives the input; `settledText` drives the query.** `useDebouncedValue`'s own
  doc comment forbids the reverse — the field would drop characters.
- **`skip` on `!shouldResolve`** — the same `includes('{{')` guard the kiosk already applies at
  `CellPage.tsx:449`. A label with no placeholder issues no request, ever.
- **No `pollingInterval`.** The slice has none today (`CameraViewer`'s 5000 ms is the only one
  in the app and it is stream health) and must not gain one.

**US3's one expression.** The canvas span becomes the resolved text with the raw text as
fallback, mirroring the kiosk's own `snapshot?.resolvedText ?? publishedOverlay?.text`
(`CellPage.tsx:477`):

```tsx
<span data-testid="overlay-editor-preview">{previewText || ' '}</span>
```

where `previewText = data?.resolvedText ?? value.text`. **`overlayLabelSurfaceStyle(value)` is
not touched** — US3 changes what is inside the box, never what the box looks like, and spec
146's two guard suites must pass unmodified to prove it.

**US2's pick list** reuses `useListVariablesQuery` (already consumed by `SystemVariablesPage`)
and filters client-side — the endpoint is **unpaged** (`ListVariablesQueryHandler.cs:36` returns
every matching row), so a request per keystroke would buy nothing. Insert-at-caret via
`selectionStart`/`selectionEnd` on the existing input ref, then `setSelectionRange` and
`focus()`. Archived rows excluded (they are excluded server-side by default anyway).

**Accessibility and the shape of the control**, following
`LayoutEditorDialog.tsx:288-294, 305-314` — a plain filter field beside a list, an
`aria-live="polite"` line stating loading / no-match / count, and the advisory panel wired to
the text input by `aria-describedby`. **No `role="listbox"`, no `aria-activedescendant`, no
Radix Popover.** Spec 055's recorded decision plus the e2e `.fill()` constraint; both are in
spec §US2.

---

## Messaging

**None.** No domain event, no integration event, no `Shared.Contracts` change, no Wolverine
handler, no queue, no outbox row. Reads only.

The existing chain is untouched and is recorded here so the absence is visible rather than
assumed: `VariableValueChangedV1` → `VariableValueChangedDomainEventHandler` →
`ResolvedOverlayTextChangedV1` → `SignalRLayoutLifecycleBroadcaster` → the wall. This spec adds
a query beside it; it does not join it.

---

## Boundary rules

- **No cross-context project reference.** OverlayDesigner and SystemVariables communicate over
  HTTP through the gateway. NetArchTest's rule is unaffected because no `.csproj` changes.
- **Domain stays pure.** Nothing new in `src/SystemVariables/Domain/`.
- **Guards** are `Ensure.That(x).IsNotNull()` (ADR-0105), never
  `ArgumentNullException.ThrowIfNull`.
- **Errors** are `Result<T, ResolveOverlayTextError>` with an `ApiError` base and a
  `ResolveOverlayTextFailures` static factory returning the **base** type — ADR-0047's
  invariance rule, copied from `GetOverlaySnapshotFailures`.
- **Handler deconstruction** (CLAUDE.md): `ResolveOverlayTextQueryHandler` reads two or more
  fields of its query, so it destructures into locals as the first statement after the guard.
  `HandlerDeconstructionTests` fails the build on a local named after a different field.
- **`CancellationToken` last, mandatory, no `ConfigureAwait`** (ADR-0049).
- **`Option<T>` over nullable parameters in Domain and Application** (ADR-0141) — the builder's
  internal per-name lookup already returns `Option<Variable>` and stays that way.
- **Collections** declared with an explicit type and a collection expression —
  `List<PlaceholderResolution> resolutions = [];`.
- **No leading underscore on private fields**; primary constructors throughout.

---

## Phase 4a — two colours, and which artefact gets which

The programme has used this split twice (specs 146 and 147) and it applies again, this time
across the stack rather than across two components.

### CHARACTERISATION — observed **green** before the change, unmodified after

| Suite | What it pins |
|---|---|
| `GetOverlaySnapshotQueryHandlerTests` | the extraction is behaviour-preserving — including `:54-73` (archived + unset → `"{{shift}} - {{unknown}}"`) and `:77-121` (the two fab cases) |
| `PlaceholderParserTests`, `ResolverTests` | the grammar and the literal-passthrough, neither of which this spec may change |
| `TwoPlaceholdersInOneLabelTests` | the multi-name loop end to end over HTTP |
| `OverlayLabelParity.test.tsx`, `OverlayLabelCharacterisation.test.tsx` | US3 moves the *text*, not the *surface* |
| `e2e/support/seed-bound-overlay-wall.setup.ts`, `seed-live-video-wall.setup.ts` | `.fill()` on `overlay-editor-text` still produces the same draft, and the *Save as draft* click is not intercepted |

**An assertion in any of these that has to be edited is evidence the behaviour moved: block,
do not adjust.** The two e2e setups are the ones most likely to be "fixed" by a well-meaning
selector change — they gate every wall spec in the repo, and they are the only mechanism that
would catch a floating surface swallowing a click.

### RED — written first, observed failing, the failure quoted in the PR

Everything new: the builder's four outcomes, the endpoint and its handler, the DTO,
`placeholderAdvisories`, the panel, the pick list, and US3's canvas text.

**Ambiguity resolves to red** (ADR-0144), and one case needs saying out loud.
`ResolveOverlayTextQueryHandler` is a *new* handler built on an extraction whose whole proof is
that nothing moved — so it is tempting to treat its tests as characterisation too. It is not:
the endpoint did not exist, so no test of it can have been green before. **Red.**

The extraction's own commit is the green one; the handler's commit is the red one; they are
separate commits for exactly this reason.

---

## Parallelism (ADR-0109)

**T-block A (backend) blocks T-block B (the frontend's US1 half)** — B consumes A's DTO.
Everything after that fans out on disjoint files:

- **A** — `src/SystemVariables/**` + `tests/SystemVariables.Application.Tests/**` +
  `tests/Integration.Tests/SystemVariables/**`. `backend-engineer`.
- **B** — `systemVariables.api.ts`, `systemVariables.schema.ts`,
  `placeholderAdvisories.ts`, `PlaceholderPreviewPanel.tsx`. `frontend-engineer`.
- **C** `[P]` — `VariablePickList.tsx` (US2). Disjoint from B's files; touches
  `OverlayEditor.tsx` only in the mount step.
- **D** `[P]` — US4's advisory rows, wholly inside `placeholderAdvisories.ts` + its test.
  Disjoint from C.

**The one contention file is `OverlayEditor.tsx`** — B, C and US3 all mount something in it.
`tasks.md` serialises those three touches into a single task each, in a fixed order, so two
parallel workers never hold it.

---

## PR #2357 (#2340) — what depends on it, and what happens if it does not land

**Nothing depends on it.** Every story here works against #2357's default checkerboard exactly
as it works against a captured frame. US3 is *more useful* with a real frame behind the canvas —
the operator sees the real string over the real picture — but does not require one, and the
absence costs a nicety, not a scenario.

**The overlap is one file, and it is certain.** Both branches edit
`apps/shared/src/ui/composites/OverlayEditor.tsx`. #2357 takes it from 118 to 222 lines and
restructures the `return` block, adding `<BackdropControls>` and a conditional `<FrameGrabber>`
as siblings after the controls grid. This spec adds `<PlaceholderPreviewPanel>` and
`<VariablePickList>` as siblings in the same region and changes the
`overlay-editor-preview` span.

**Cut from `develop`, as instructed; do not stack.** Whichever merges second rebases. The
conflict is mechanical — two sets of siblings inside one `<div>` — and the resolution is to
keep both. Concretely, for whoever rebases:

- Conflict 1: the JSX sibling block after `<div style={{ display: 'grid', gap: 12 … }}>`.
  Keep #2357's `<BackdropControls>` / `<FrameGrabber>` **and** this spec's panel and pick list.
- Conflict 2: the `<span data-testid="overlay-editor-preview">` line, if #2357 touched it.
  This spec's version wins; #2357 does not change the text, only what is behind it.
- Conflict 3: the props interface and the `useState` block near the top.
  Keep both — `getToken` (#2357) and this spec's debounce/query are independent.

**#2357 also adds `OverlayEditorCharacterisation.test.tsx`**, which pins the checkerboard
gradient byte-for-byte precisely so a mangled rebase fails loudly. After the rebase that file is
the first thing to run: if it is green, the conflict was resolved correctly.

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | `RenderedValue` is written from `ToWireString()` instead of `Render()` — the preview shows `23.399999999999999` and `true`. **The most likely single defect in this spec.** | US1 scenarios 2 and 3, asserted against `Render`'s output and never against `VariableDto.value`. Called out in the task text, not only here. |
| 2 | The extraction quietly changes the snapshot handler's repository call count (e.g. a fab-parallel lookup, a cache) and the leg regresses inside its own loose ceiling. | `GetOverlaySnapshotQueryHandlerTests` unmodified; `NFR_VariableResolutionLatencyTests` re-run **twice** and its printed median quoted in the PR. Its 800 ms assertion is 4× the budget and would not catch this on its own — the figure is the evidence. |
| 3 | Someone makes the advisory blocking "because it is obviously a mistake". | US1 scenario 13 asserts *Save as draft* stays enabled and the draft is created with the text as typed. Spec §Decision 3 carries the ADR-0115 argument; a reviewer who is asked to loosen it should read it first. |
| 4 | A floating surface on the text input breaks every wall e2e spec. | Spec §US2's three reasons; US2 scenario 5; the two seed setups are characterisation artefacts. |
| 5 | US4's loose regex false-positives on nested braces. | Bounded by design: every advisory is non-blocking, so the cost is one extra row. US4 scenario 6 states the limit rather than defending exactness. |
| 6 | The #2357 rebase silently drops a sibling. | Conflicts 1-3 written out above; #2357's `OverlayEditorCharacterisation.test.tsx` is the first suite to run afterwards. |
| 7 | `OverlayEditor.tsx` grows past 300 lines. | Three new files; the editor gains mounts, not logic. |
| 8 | The preview is treated as authoritative for another fab's wall. | The `fab` field is rendered beside every resolved value; spec §Decision 4 states that a fab-neutral template has no single correct preview. |

---

## Follow-ups to file (not fixed here)

1. **A third copy of the resolution filter.** `VariableValueChangedDomainEventHandler.cs:100-142`
   repeats the skip rules and the fab search for the push fan-out. After this spec it is the
   *only* copy outside `VariableSnapshotBuilder`. Folding it is a behaviour-preserving refactor
   on constitution §IV leg 4's hottest path and wants its own characterisation gate. Carry
   §"The extraction" above into the issue.
2. **An overlay's label cannot be edited after creation.** `OverlayEditorDialog` mounts only
   `useCreateOverlayDraftMutation`; `overlaysApi.editDraftOverlayRevision`
   (`overlays.api.ts:117`) is wired to no UI. So the issue's own *"renamed or archived later"*
   scenario has no remedy in the product at all — this spec lets an author see the problem at
   authoring time and gives them nothing to do about it afterwards. That is the larger half of
   #2341's second paragraph and it is a separate feature.
3. **Nothing warns about already-published overlays whose references have since broken.**
   `IReverseIndex` holds name → overlays and could answer it directly; it is a list-page
   concern, not a dialog one.
