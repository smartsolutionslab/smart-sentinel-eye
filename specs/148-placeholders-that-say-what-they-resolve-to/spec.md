# Spec 148 — Placeholders that say what they resolve to, and a decision that was already made

**Issue:** #2341 — *Overlay text takes `{{variable}}` placeholders with no autocomplete, no
validation and no resolution preview*
**Branch:** `feat/2341-placeholders-that-say-what-they-resolve-to`
**Status:** Phase 1 complete — awaiting gate
**Lane:** autonomous (ADR-0144)
**Engineers:** `backend-engineer` (US1 server half), then `frontend-engineer` (US1 client
half, US2–US4)
**Phase 4a colour:** **two colours.** CHARACTERISATION (green) on the resolution loop and on
the label surface; RED on everything new. Reasoned out in `plan.md` §"Phase 4a".
**ADRs:** **0115** (an overlay is a fab-neutral template; a placeholder resolves in the
*viewer's* fab — the decision that settles this spec's hardest question), **0114** (fab
inferred for single-fab operators), 0145 (a kiosk's fab comes from its wall), 0070 (minimal
APIs), 0143 (`GET` is retried by default — which is why this is a `GET`), 0142 (no
idempotency key: nothing is created), 0125 (Postgres connection budget), 0047/0089
(`Result<T, Error>` + `ApiError`), 0075 (RTK Query), 0077 (Radix — and why no combobox is
introduced), 0036 (smallest change; no speculative generality; read before write), 0109
(`[P]` disjoint-file parallelism), 0037 (phases and gates), 0028 (GitFlow), 0086 (no
`Co-Authored-By`), constitution §II (no primitives on a domain model), §IV (the latency
budget) and §Testing (two obligations, not one rule).

**Spec number 148.** Derived from refs, not the working tree (spec 137's method).
`ls specs/` tops out at `146-one-label-renderer`; `git log --all --name-only --pretty=format:
-- specs/` adds `147-a-real-frame-behind-the-canvas` on an unmerged ref (PR #2357, #2340).
146 and 147 are taken. **148.**

---

## Phase 1 — every claim in the issue, checked against the tree

Three of the issue's claims hold exactly as written. One does not, and it is the one the
issue asks for a decision about.

### The input is a plain text box. Confirmed, and it is not even the shared primitive.

`apps/shared/src/ui/composites/OverlayEditor.tsx:94-102` is a raw `<input type="text">` with
inline styles — not `apps/shared/src/ui/primitives/Input.tsx`. No `list`, no `aria-describedby`,
no validation beyond `maxLength={256}`. The editor has **no** unit test file of its own; its
only coverage is indirect, via `OverlayEditorDialog.test.tsx` and spec 146's
`OverlayLabelParity.test.tsx`.

### Resolution is server-side, built, and measured. Confirmed.

`PlaceholderParser` + `Resolver` + `GetOverlaySnapshotQueryHandler`, exercised by
`NFR_VariableResolutionLatencyTests` (median under an 800 ms regression ceiling; the
constitution's own budget for the leg is 200 ms) and `ResolvedTextReachesItsFabTests` (five
facts over the real SignalR hub).

### `AelHelpPanel.tsx` is the precedent — but it is a narrower precedent than the issue implies.

`apps/management-web/src/features/rules/AelHelpPanel.tsx` is 66 lines of native
`<details>`/`<summary>`. **No Radix, no state, no props, no fetch, no autocomplete, nothing
clickable-to-insert** — three module-level frozen literal tables rendered as `<dl>`s, dropped
statically into `RuleDialog.tsx:133`. It sets a precedent for *an inline reference panel beside
the field it documents*. It sets none for typeahead, for a floating surface, or for reading a
catalogue.

That matters, because the repo has a **recorded decision against building a combobox**
(`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:288-294`, spec 055):

> A field beside the native lists rather than a combobox replacing them: the selects already
> carry role and value announcement, arrow-key movement, Escape and start-of-name type-ahead.
> Replacing them would mean re-implementing all of it, and losing any of it is invisible to
> anyone testing with a mouse.

And the tree agrees: no `cmdk`, no `downshift`, no `@headlessui`; `@radix-ui/react-popover` is
declared in `apps/shared/package.json` and **imported by zero files**; no `role="listbox"`,
`aria-autocomplete` or `aria-activedescendant` exists anywhere in `apps/`.

**So "autocomplete on `{{`" as a popup typeahead would be the first of its kind in this
codebase, against a recorded decision, on the one input the whole e2e wall suite depends on.**
US2 below delivers the same operator outcome by the house pattern instead, and says so.

---

### The issue's point 4 is wrong. The wall's behaviour is specified, three times over.

> *"A decision about what the wall should do with an unresolvable placeholder … Today's
> behaviour is the accident of nobody choosing."*

It is not an accident, and nobody has to choose. It was chosen in spec 005 and it is recorded
in the requirement, in the acceptance scenarios, and in the code's own doc comments.

**In the requirement** — `specs/005-system-variables/spec.md:208-211`:

> **FR-011** A placeholder resolves to the variable's current value if the variable exists, is
> not archived, and is not `Unset`. In every other case (missing, archived, unset), the literal
> `{{name}}` placeholder is kept in the rendered text.

**In three acceptance scenarios** — US3 scenario 2 (unset), US3 scenario 3 (*"identical to the
unset case from the kiosk's perspective"*), US4 scenario 2 (archived: *"no rendering crash, no
stale value"*).

**In the code that implements it** — `PlaceholderParser.cs:46-51` says it in prose and
`:57-62` in one line:

```csharp
return PlaceholderRegex().Replace(labelText, match =>
{
    string name = match.Groups["name"].Value;
    string? value = resolveOne(name);
    return value ?? match.Value;   // match.Value is the whole "{{name}}"
});
```

**And in six tests across four levels** — `PlaceholderParserTests:46-52` and `:54-61`,
`ResolverTests:58-64`, `GetOverlaySnapshotQueryHandlerTests:54-73` (mixed archived + unknown →
`"{{shift}} - {{unknown}}"`) and `:104-121` (*"Identical to a name that exists nowhere, per the
contract"*), `TwoPlaceholdersInOneLabelTests:141`.

**Consequence for this spec, and it is a simplification rather than a loss.** There is nothing
to decide and nothing to file. The preview must render the literal `{{name}}` because that is
what the wall renders, and a preview that showed anything else would be a second, wrong
implementation of the very thing #2339 just finished folding. What is genuinely missing is not
a *decision* — it is an *explanation*: the editor shows the operator braces and never says why.
That is what US1's advisory rows add.

---

### What the tree says that the issue does not, and every item below changes the design

**1. `GET /system-variables/snapshot` cannot preview a draft. It is keyed on an overlay GUID.**

`SystemVariableEndpoints.cs:334-340` takes `overlayIdentifier` (required `Guid`) and `fabId`
(optional). The label text is **not an input** — `GetOverlaySnapshotQueryHandler.cs:16` reads it
from `reverseIndex.LookupLabelText(...)`, and the reverse index is populated only from
`OverlayRevisionPublishedV1`. A draft has never been published, so it is not in the index, so
the endpoint answers **404 `OVERLAY_NOT_IN_REVERSE_INDEX`**.

There is no other endpoint that resolves arbitrary text. Every `MapGet`/`MapPost`/`MapPut`
under `src/**/*Endpoints.cs` was enumerated; `POST /rules/{name}/dry-run` is AEL evaluation and
never touches `PlaceholderParser`. **The only supported way to resolve your own text today is
to publish an overlay carrying it and then read it back** — which is literally what the
integration tests do.

**This is the finding that decides §Decision 1.** "Ask the snapshot endpoint" is not an option
that exists.

**2. The list endpoint's `value` is not the rendered value.** `VariableDto.Value` comes from
`GetVariableQueryHandler.cs:49` → `VariableValue.ToWireString()`, which formats a number
`"G17"`. The wall renders with `VariableValue.Render()`, which formats with plain
`ToString(InvariantCulture)` — shortest round-trippable. For many doubles those are **different
strings**. A client-side substitution reading `value` off the list would paint
`23.399999999999999` where the wall paints `23.4`.

**3. A boolean's rendered value is not its value at all.** `Render` returns
`booleanLabels.TruthyLabel` / `FalsyLabel`, falling back to `BooleanLabels.Default` when the
variable carries none (`Resolver.cs:17`). A client-side substitution would paint `true` where
the wall paints `Running`.

**4. Multi-fab resolution has a tiebreak, and nobody would guess it.**
`GetOverlaySnapshotQueryHandler.FindInAnyFabAsync:77-90` — when the caller holds several fabs
and names none, **the first fab by ordinal fab-name that defines the variable wins, per
placeholder.** Not per request, per placeholder. Two names in one label can resolve in two
different fabs.

**5. Whitespace inside braces is not a placeholder at all.** The grammar is
`\{\{(?<name>[A-Za-z][A-Za-z0-9_]{0,63})\}\}` — ASCII-only, case-sensitive, no spaces, no
escape mechanism. `{{ temperature }}` never resolves and never will, and today the editor gives
no hint whatsoever. The issue does not mention this case; it is at least as likely as
`{{temperatuer}}` and it is *permanently* unresolvable rather than fixable by defining the
variable. US4.

**6. There is no edit path.** `OverlayEditorDialog.tsx` mounts only
`useCreateOverlayDraftMutation`; `overlaysApi.editDraftOverlayRevision`
(`overlays.api.ts:117`) exists and is wired to no UI. So the issue's *"a variable that exists at
authoring time and is later renamed or archived degrades the same silent way"* is true and
**cannot presently be repaired through this dialog at all**. Out of scope here; recorded so it
is not rediscovered as an omission.

**7. The e2e wall suite types into this exact input with `.fill()`.**
`e2e/support/seed-bound-overlay-wall.setup.ts:59` and `e2e/support/seed-live-video-wall.setup.ts:66`:

```ts
await page.getByTestId('overlay-editor-text').fill(`{{${wall.variableName}}}`);
await page.getByRole('button', { name: /save as draft/i }).click();
```

`.fill()` sets the value and fires **one** input event — no per-key events. These two setups
gate every wall spec in the suite. Any floating surface that opens on `{{` and swallows the
following click breaks all of them. This is a hard constraint, and it is the second reason US2
is not a popup.

**8. The authoring operator already holds the scope.** `management-web` carries
`sse.variables.read` and `sse.variables.write` in its `defaultClientScopes`
(`src/AppHost/Realms/smart-sentinel-eye-realm.json:167-180`). No realm change, no new scope, no
auth gap.

---

## The five decisions

### 1. Where does the preview's resolution come from? — **Server-side, through a new read endpoint.**

The issue frames this as *ask the snapshot endpoint* vs *substitute client-side*. Finding 1
removes the first option: the snapshot endpoint cannot be handed text. So the real choice is
**a new server endpoint that resolves arbitrary text** against **a TypeScript reimplementation
of the resolver**.

**Client-side is rejected, and the #2339 precedent is only the third-strongest reason.**

To reproduce the wall's answer, a TS implementation must replicate six independent rules:

| # | Rule | Where it lives | What a naive TS copy gets wrong |
|---|---|---|---|
| 1 | `\{\{[A-Za-z][A-Za-z0-9_]{0,63}\}\}`, ASCII, case-sensitive | `PlaceholderParser.cs:17` | `IgnoreCase`; `\w` (Unicode in JS `u` mode) |
| 2 | Ordinal name matching | `Resolver.cs:16` | JS object keys are fine; a `toLowerCase()` normalise is not |
| 3 | missing / Archived / Unset → keep the literal | `GetOverlaySnapshotQueryHandler.cs:47-61` | renders `undefined` or empty |
| 4 | Number: shortest round-trip, **not** the `G17` on the wire | `VariableValue.cs:94` vs `:77` | paints `23.399999999999999` |
| 5 | Boolean: `TruthyLabel`/`FalsyLabel`, default when null | `VariableValue.cs:95`, `Resolver.cs:17` | paints `true` |
| 6 | Multi-fab: first fab by **ordinal fab name**, per placeholder | `GetOverlaySnapshotQueryHandler.cs:83` | picks the first row the array happened to hold |

Rules 4, 5 and 6 are the decisive ones, because **no frontend developer would ever think to
implement them**, and each produces a preview that is confidently, quietly wrong — the exact
failure mode the issue is filed about, reproduced inside the fix.

And the #2339 lesson applies here *worse* than it did there. There, the two copies were both
TypeScript in one process, so `OverlayLabelParity.test.tsx` could run them side by side and
diff them. **A C# resolver and a TS resolver cannot be compared by any test this repo can
run.** The drift would be undetectable by construction.

**Chosen: `GET /system-variables/resolve?text=<text>&fabId=<optional>`**, a new read on the
existing `/system-variables` group, reusing `PlaceholderParser` and `IResolver` unchanged and
sharing the *snapshot-building loop itself* with `GetOverlaySnapshotQueryHandler` (see `plan.md`
§The extraction). `IVariableRepository` gains one method,
`ExistsIncludingArchivedAsync` — `GetByNameAsync` excludes archived rows by contract (FR-005: a
released name is free for re-use), so it cannot itself distinguish `Archived` from `Unknown`,
which this endpoint's own `placeholders` array must. The preview is then the same code the wall
runs, and cannot drift because there is nothing to drift from.

`GET`, not `POST`: it is a read, it creates nothing, it needs no `Idempotency-Key` (ADR-0142),
and `GET` is retried by the standard resilience handler while `POST` is not (ADR-0143) — so
`GET` is both correct and the one that survives a flaky hop. The text is ≤256 characters and
fits a query string comfortably. *Noted, not a blocker:* label text will therefore appear in
request logs and traces. Label text is rendered on a wall in a shared control room; it is not
a secret.

### 2. What does the preview show for an unset or unknown variable? — **The literal `{{name}}`, and a row saying why.**

Settled by FR-011, not by this spec (see §"The issue's point 4 is wrong"). The preview shows
exactly what the wall shows, because it *is* what the wall shows.

What is new is the explanation. The endpoint returns, per referenced name, which of four
outcomes applied — `Resolved`, `Unknown`, `Unset`, `Archived` — and the editor renders one
advisory row each. **The server can distinguish all four; the resolved text cannot.** That
asymmetry is the whole value of the endpoint over reading `resolvedText` alone, and it is why
the DTO carries more than a string.

**Specifying the wall's behaviour does not belong here and is not its own issue** — it is
already specified, implemented and tested. Filing it would be filing a duplicate of a closed
decision.

### 3. Is validation advisory or blocking? — **Advisory. I am recommending against blocking, and against a confirm-to-publish dialog, which is blocking with extra clicks.**

The operator-cost argument ("they might be about to define it") is real but soft. The
architectural argument is not soft, and it decides it:

**ADR-0115 makes an overlay a fab-neutral template whose placeholders resolve in the viewer's
fab.** A name absent from the *author's* fabs may be present in the *viewer's* — that is not an
edge case, it is the stated purpose of the decision:

> An overlay saying `Line 1: {{oeeLine1}}` is the same design in every fab.

So the editor can only ever answer *"no variable named X is defined in the fabs **you** hold"*.
**Blocking publish on that answer would make a fab-neutral template fail validation against one
fab's catalogue** — it would enforce, at the UI, the precise coupling ADR-0115 was written to
reject. The two stand in direct contradiction, and the ADR wins.

The wording of every advisory must therefore be scoped to what is actually known. Not *"this
variable does not exist"*. **FR-009 fixes the exact strings**, because the honest phrasing is
the entire difference between a useful warning and a false accusation.

The soft argument survives as a second reason: authoring the overlay before wiring the data is
a legitimate order of work, and blocking would forbid it.

### 4. Fab scoping — **the operator's own fabs, with the server's existing per-placeholder ordinal tiebreak. The tension dissolves the moment the server resolves it.**

#2340 had to invent an answer (never persist the camera choice) because nothing in the tree had
one. Here the tree already has one, written down in ADR-0115 and implemented in
`FindInAnyFabAsync`: **resolution happens in the viewer's fab, and a multi-fab caller who names
none gets the first fab by ordinal name, per placeholder — arbitrary but stable.**

Because the new endpoint reads `query.Fabs` from the caller exactly as the snapshot endpoint
does, the editor inherits that rule for free. **No fab picker. No new concept. No design
work.** A client-side implementation, by contrast, would have had to reproduce rule 6 above and
would silently have got it wrong.

Two consequences are carried honestly rather than hidden:

- **The preview is one fab's rendering of a fab-neutral template**, and must say so. The
  response carries, per resolved name, **which fab it resolved in**, and the editor prints it.
  The handler already knows; exposing it costs a field.
- **No fab selector in the UI.** ADR-0114 records that every operator in the current deployment
  is single-fab; a picker for a population of zero is speculative generality (ADR-0036). The
  *endpoint* takes optional `fabId` anyway, for consistency with the two sibling reads in its
  own group — that is one parameter passed through, not a feature. A multi-fab operator gets
  the stable ordinal answer plus a visible fab name telling them which one it was, which is
  strictly more than the wall gives them today.

### 5. Resolved text inside the sized box — **yes, in scope, as US3. It does not fix #2353, and the spec says so twice so that nobody reads it as closing it.**

An author sizes the box around `{{temperature}}` (15 characters) and the wall paints `23.4`
(4). Rendering the resolved string inside the `<Rnd>` box costs one expression — the box
already exists, the geometry already exists, `overlayLabelSurfaceStyle` takes only
`fontSizePx` and never touches text (`overlayLabelStyle.ts`). The author then sizes the plate
around the string that will actually appear.

**It fixes which string is being sized. It does not fix how big the box is.** #2353 is the
separate, orthogonal defect: the box is tile-relative and the type is viewport-relative, so the
editor's plate is 400px where a 5×5 tile's is 192px. **Both must be true for the author's
judgement to transfer**, and only one of them is fixed here. #2353 stays blocked on #2337 and
this spec does not touch it — no `cqw`, no `container-type`, no change to
`overlayLabelSurfaceStyle`.

Kept out of P1 deliberately: it changes what `overlay-editor-preview` renders, which is the
node spec 146's parity tests and PR #2357 both sit on. As its own story it can be dropped
whole if the rebase fights, without losing US1.

---

## User Scenarios & Testing

### User Story 1 — the author sees what the wall will say, and why it will say it (Priority: **P1**)

An operator types `Line 1: {{temperatuer}} °C` into the overlay editor. Below the field, a
panel shows the text as it will render — `Line 1: {{temperatuer}} °C` — and one row per
reference: **`temperatuer` — no variable of this name is defined in your fab (munich). It will
render as `{{temperatuer}}`.** The operator fixes the typo without leaving the dialog, and the
panel changes to **`temperature` — `23.4` (munich)**.

**Why this priority:** it is the whole of the issue's points 2 and 3, and it is the only story
here that requires a server change. Everything else is an improvement to a panel that must
exist first.

**Independent Test:** boot the Aspire stack, define `temperature = 23.4` in the operator's fab,
open **Overlays → New overlay**, type text carrying a good and a bad reference, and read the
panel. Also testable below the UI: `GET /system-variables/resolve?text=...` returns the
resolved string and the per-name outcomes.

**Acceptance Scenarios:**

1. **(happy)** **Given** `temperature` is a `Number` variable set to `23.4` in the caller's
   fab, **When** `GET /system-variables/resolve?text=Line%201%3A%20%7B%7Btemperature%7D%7D`
   is called, **Then** the response is `200` with `resolvedText` = `"Line 1: 23.4"` and one
   placeholder entry `{ name: "temperature", outcome: "Resolved", fab: "munich",
   renderedValue: "23.4" }`.
2. **(the number format, and it is not the one on the wire)** **Given** the same variable,
   **When** the resolve endpoint and `GET /system-variables/temperature` are both called,
   **Then** `renderedValue` is `"23.4"` while `VariableDto.value` is the `G17` wire string —
   the two are **not** required to be equal, and the test asserts `renderedValue` against
   `VariableValue.Render`'s output, never against the DTO's. *(This scenario exists to pin
   finding 2. If it ever goes green by both sides agreeing, someone has changed a format.)*
3. **(boolean labels)** **Given** `lineRunning` is a `Boolean` set to `true` with
   `truthyLabel: "RUNNING"`, **When** `{{lineRunning}}` is resolved, **Then** `renderedValue`
   is `"RUNNING"` — not `"true"`.
4. **(the three unresolvable outcomes, distinguished)** **Given** one label referencing an
   unknown name, a `Defined`-but-`Unset` name and an `Archived` name, **When** it is resolved,
   **Then** `resolvedText` keeps all three literal `{{name}}` tokens **unchanged** (FR-011),
   **and** the placeholder entries carry `outcome` `Unknown`, `Unset` and `Archived`
   respectively. The resolved text alone cannot tell them apart; the entries must.
5. **(no placeholders at all)** **Given** `text` = `"PRODUCTION LINE 1"`, **When** it is
   resolved, **Then** `200`, `resolvedText` equals the input byte-for-byte, and `placeholders`
   is empty.
6. **(bad request — empty)** **Given** `text` is absent or empty, **When** the endpoint is
   called, **Then** `400` carrying `VARIABLE_INVALID_INPUT`, matching the shape
   `GetSnapshot` already returns for `Guid.Empty`.
7. **(bad request — too long)** **Given** `text` longer than 256 characters (the `Label` value
   object's own bound, `overlays.schema.ts:7`), **When** the endpoint is called, **Then**
   `400`. The server does not resolve text no overlay could ever carry.
8. **(auth — no token)** **Given** no bearer token, **Then** `401`.
9. **(auth — wrong scope)** **Given** a token without `sse.variables.read`, **Then** `403`.
   Pinned the way `VariableReadScopeIntegrationTests` pins the sibling reads.
10. **(auth — a fab the caller lacks)** **Given** `fabId=dresden` from a caller holding only
    `munich`, **Then** `403 RESOURCE_FAB_NOT_AUTHORIZED`, via the same `FabResolution` path
    the other reads use. **Given** a caller holding no fabs at all, **Then** `403`.
11. **(the fab that answered is named)** **Given** a caller holding `dresden` and `munich`,
    both defining `oee` with different values, **When** `{{oee}}` is resolved with no `fabId`,
    **Then** the answer is `dresden`'s and the entry's `fab` field says `"dresden"` — the
    first by ordinal fab name (`FindInAnyFabAsync`). Arbitrary, stable, and now visible.
12. **(the UI — a resolved reference)** **Given** the editor with `temperature` set, **When**
    the operator types `{{temperature}}`, **Then** after the debounce the panel shows the
    resolved text and a row naming the value and the fab.
13. **(the UI — an unresolved reference is advisory, never blocking)** **Given** the operator
    types `{{temperatuer}}`, **Then** the panel shows the `Unknown` row **and** *Save as
    draft* stays enabled, the form submits, and the draft is created with the text exactly as
    typed. **Decision 3, asserted rather than described.**
14. **(the UI — the query is skipped when there is nothing to resolve)** **Given** text with
    no `{{`, **Then** no request is issued at all — the same `text.includes('{{')` guard the
    kiosk already applies (`CellPage.tsx:449`).
15. **(the UI — the endpoint is down)** **Given** the resolve request fails, **Then** the panel
    shows a single non-blocking notice, *Save as draft* stays enabled, and nothing about the
    form's validity changes. A preview that cannot be fetched must never become a publish
    gate — that would reintroduce decision 3 through the back door.

---

### User Story 2 — the author picks a name instead of spelling it (Priority: **P2**)

Beside the text field, a filterable list of the variables defined in the operator's fabs. Each
row shows name, type, current value and fab; clicking one inserts `{{name}}` at the caret.

**Why this priority:** it is the issue's point 1, and it removes the typo at its source rather
than reporting it. P2 because US1 already catches the typo, and because this story is worth
nothing without a panel to catch what it misses.

**Not a popup typeahead, and this is a design decision rather than a shortcut.** Three
independent reasons, any one of which is sufficient:

- The repo has a **recorded decision against combobox-shaped controls** (spec 055,
  `LayoutEditorDialog.tsx:288-294`) and no listbox, `aria-activedescendant` or Radix Popover
  usage exists to copy.
- **The e2e wall suite drives this exact input with `.fill()`** and then clicks *Save as
  draft* (finding 7). A surface that opens on `{{` and intercepts that click breaks every wall
  spec in the repo.
- The cited precedent, `AelHelpPanel`, is a static inline panel — the house pattern is *a field
  beside the control*, `useDebouncedValue` between field and query, and an `aria-live="polite"`
  status line (`LayoutEditorDialog.tsx:92, 305-314`). This story is that pattern with an insert
  button.

**Independent Test:** open the dialog with three variables defined; filter; click one; assert
the text field's value and caret.

**Acceptance Scenarios:**

1. **(happy)** **Given** `temperature`, `humidity` and `lineRunning` are defined, **When** the
   operator clicks `temperature`, **Then** `{{temperature}}` is inserted at the caret in
   `overlay-editor-text` and the field keeps focus.
2. **(insert is not append)** **Given** the field holds `Line 1:  °C` with the caret after
   `"Line 1: "`, **When** a name is inserted, **Then** the value is `Line 1: {{temperature}} °C`
   — inserted at the caret, not at the end.
3. **(filter)** **Given** the filter reads `temp`, **Then** only matching rows are listed, and
   an `aria-live="polite"` line states the count. The filter is client-side over the already-
   fetched array — `listVariables` is **unpaged** (`ListVariablesQueryHandler.cs:36` returns
   every matching row), so a server round-trip per keystroke would buy nothing.
4. **(archived names are not offered)** **Given** `oldVariable` is `Archived`, **Then** it does
   not appear in the pick list — inserting it would author a reference that renders literally
   by FR-011. It still gets an `Archived` advisory row in US1 if the operator types it, because
   existing overlays carry such references already.
5. **(the seed path is untouched — the conflict case)** **Given** `page.getByTestId(
   'overlay-editor-text').fill('{{someVariable}}')` followed by a click on *Save as draft*,
   **Then** the draft is created with that exact text, with nothing intercepting the click.
   `seed-bound-overlay-wall.setup.ts` and `seed-live-video-wall.setup.ts` run unmodified.
6. **(empty catalogue)** **Given** the operator's fab defines no variables, **Then** the panel
   states so plainly and the text field is unaffected.
7. **(auth)** N/A beyond US1 — the same `sse.variables.read` on an endpoint already consumed by
   `SystemVariablesPage`. No new scope, no new client, no realm change.

---

### User Story 3 — the box is sized around the string that will appear (Priority: **P3**)

The editor's canvas renders the **resolved** string inside the draggable box, so the plate the
operator sizes is the plate the wall will need. The text field below continues to hold the raw
text with its placeholders — the author edits raw and sees resolved, which is the same split a
formula bar and a cell have.

**Why this priority:** it is the issue's point 3 and it needs US1's endpoint to exist. P3
because it is the story most exposed to PR #2357's rebase, and it is severable: dropping it
costs the sizing insight and nothing else.

**Independent Test:** author `{{temperature}}` with `temperature = 23.4`; the canvas box shows
`23.4`; the input still shows `{{temperature}}`.

**Acceptance Scenarios:**

1. **(happy)** **Given** `temperature` resolves to `23.4`, **When** the canvas is read, **Then**
   `overlay-editor-preview` renders `23.4` while `overlay-editor-text` holds `{{temperature}}`.
2. **(unresolvable — the literal, consistently)** **Given** `{{temperatuer}}`, **Then** the
   canvas renders `{{temperatuer}}` — identical to the wall, per FR-011 and decision 2.
3. **(before the first response)** **Given** the resolve request is in flight or has failed,
   **Then** the canvas renders the **raw** text. It never renders empty and never flickers to a
   placeholder of its own; the raw text is the honest fallback because it is what the wall
   would show if nothing resolved.
4. **(the surface does not move — the characterisation case)** **Given** any label, **When** the
   canvas node's style is read, **Then** every property `overlayLabelSurfaceStyle` contributes
   is byte-identical to `origin/develop`, and `OverlayLabelParity.test.tsx` and
   `OverlayLabelCharacterisation.test.tsx` pass **unmodified**. This story changes *what text is
   in the box*, never *what the box looks like*.
5. **(what is stored is the raw text)** **Given** the operator publishes, **Then** the persisted
   label text is the raw string with placeholders intact. The resolved string is never written
   anywhere — it is a rendering, and #2341 exists because an overlay is a template.
6. **(#2353 is not fixed, and is observed unfixed)** **Given** the same label on a 5×5 wall,
   **Then** the editor's plate is still ~400px and the tile's still ~192px. Recorded as a
   residual in the phase-5 procedure, not as a pass.

---

### User Story 4 — text that looks like a placeholder but is not says so (Priority: **P3**)

`{{ temperature }}`, `{{1temp}}` and `{{camera-1}}` are not placeholders. They do not match the
grammar, so they are never substituted, and — unlike a typo — **defining the variable does not
help.** The editor calls them out.

**Why this priority:** it is a failure the issue does not mention and that is strictly worse
than the one it does, because it is unfixable by the obvious remedy. P3 because it reuses US1's
panel and response entirely.

**How it is detected without a second copy of the grammar.** The client matches a deliberately
**loose** shape — `\{\{[^{}]*\}\}` — and flags any match whose inner text is **not** among the
names the server returned. The strict grammar therefore stays only on the server; the client
owns a "brace-shaped" heuristic, which is not a resolver and cannot drift into one.

**Independent Test:** type `{{ temperature }}` and read the panel.

**Acceptance Scenarios:**

1. **(whitespace)** **Given** `{{ temperature }}`, **Then** a row reads that it is not a
   placeholder and will render literally, and **no** `Unknown` row appears for it — the server
   never saw a name.
2. **(bad first character)** **Given** `{{1temp}}`, **Then** the same row.
3. **(illegal character)** **Given** `{{camera-1}}`, **Then** the same row.
4. **(a valid name is never flagged)** **Given** `{{temperature}}` with the variable defined,
   **Then** no malformed row appears — the inner text matches a returned name.
5. **(a valid-but-unknown name is not flagged as malformed)** **Given** `{{temperatuer}}`,
   **Then** it gets US1's `Unknown` row and **not** a malformed row. The server returns the
   name for an unknown-but-well-formed reference, which is exactly what keeps these two
   categories apart.
6. **(the false positive is bounded)** **Given** exotic nesting such as `{{{temp}}}`, **Then**
   at worst an extra advisory row appears. Because every advisory is non-blocking (decision 3),
   a false positive costs a note and never a refused publish. Stated as a known limit rather
   than defended as exact.

---

## Independent end-to-end test procedure

Run once by the verifier in phase 5. Requires the Aspire stack for steps 2-8.

1. `dotnet run --project src/AppHost` (one stack only — a second boot gives `FailedToStart`).
2. Sign in to `management-web` as a single-fab operator. **Variables** → define
   `temperature` (`Number`) = `23.4`, `lineRunning` (`Boolean`, truthy `RUNNING`) = `true`,
   `shift` (`String`, leave **Unset**), `oldOne` (`String`) then **Archive** it.
3. **Overlays → New overlay.** Type
   `L1 {{temperature}} {{lineRunning}} {{shift}} {{oldOne}} {{temperatuer}} {{ spaced }}`.
4. **Expected (US1, US4):** the panel shows resolved text
   `L1 23.4 RUNNING {{shift}} {{oldOne}} {{temperatuer}} {{ spaced }}` and six rows —
   `temperature → 23.4 (fab)`, `lineRunning → RUNNING (fab)`, `shift` unset, `oldOne`
   archived, `temperatuer` unknown, `{{ spaced }}` not a placeholder.
   **`23.4`, not `23.399999999999999`** — read it character by character; that is finding 2.
   **`RUNNING`, not `true`** — that is finding 3.
5. **Expected (US3):** the canvas box contains `L1 23.4 RUNNING {{shift}} …` while the text
   field still contains the raw string.
6. **Expected (US2):** clicking `temperature` in the pick list inserts `{{temperature}}` at the
   caret. `oldOne` is **absent** from the list.
7. **Expected (decision 3):** *Save as draft* is enabled throughout. Save it. Publish it, bind
   it to a 5×5 layout (`e2e/support/seed-bound-overlay-wall.setup.ts` is the existing path),
   open the kiosk wall at 1920px.
8. **Expected:** the wall's label reads the **same string** the editor's panel predicted, for
   all six references. This is the one step that proves the preview is not a second
   implementation — compare the two strings, do not merely confirm both look plausible.
9. **Expected, and recorded as a residual rather than a pass:** the label still overruns its
   plate on the tile and not in the editor (#2353, unchanged and out of scope).
10. Re-run `pnpm e2e` for the wall suite. **Expected:** the two seed setups pass unmodified.

---

## Latency budget impact

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No |
| Presentation buffer | ≤ 200 ms | No |
| **Event → overlay state** | **≤ 200 ms** | **Code path refactored, behaviour and budget unchanged.** |
| Overlay composite + render | ≤ 50 ms | No |

**The editor is not on the path.** Authoring happens minutes to months before an event
arrives; nothing this spec adds runs on a wall, in a kiosk, or in the push fan-out.

**But the leg's code is touched, and that is stated rather than waved past.** US1 extracts the
snapshot-building loop out of `GetOverlaySnapshotQueryHandler` so the new handler shares it
(`plan.md` §The extraction). That loop is on leg 4. The obligations on the engineer:

- **Behaviour-preserving.** `GetOverlaySnapshotQueryHandlerTests` passes **unmodified** — all
  of it, including the archived/unset case at `:54-73` and the two fab cases at `:77-121`.
- **Allocation-shape preserving.** One dictionary per snapshot, as today. No caching, no
  memoisation, no new repository round-trip per name, no `IAsyncEnumerable`. The loop already
  does one `GetByNameAsync` per (fab, name) and must continue to do exactly that count.
- **`NFR_VariableResolutionLatencyTests` re-run and its printed median quoted in the PR.** Its
  assertion is a loose 800 ms order-of-magnitude ceiling, so a regression inside the
  constitution's real 200 ms budget would pass it silently. The figure is the evidence, not the
  green tick. **Run it twice** — the first run after machine churn reads like a regression.

**Load on the SystemVariables service, since the brief asks for a rate.** The preview calls a
**new** endpoint, never `GET /snapshot`, so it adds nothing to the path the kiosk polls. Its
rate:

- `useDebouncedValue(text, DEBOUNCE_MS)` — 250 ms, the repo's single constant
  (`apps/shared/src/hooks/useDebouncedValue.ts`). The input is driven from raw state and the
  *query* from the settled value, per that hook's own warning.
- **Skipped entirely unless the settled text contains `{{`** (US1 scenario 14).
- **No `pollingInterval`, ever.** The systemVariables slice has none today and must not gain
  one. An authoring dialog has no reason to watch for value changes.
- RTK Query caches on `(text, fabId)`, so backspacing to a string already asked about is a
  cache hit.

Worst case is one operator holding a key down: ~4 requests/second, each doing at most ~10
`GetByNameAsync` calls (256 characters cannot hold more distinct names than that), each a
primary-key read. Against ADR-0125's connection budget that is one pooled connection held for
single-digit milliseconds. For scale: `CameraViewer` polls stream health every 5000 ms **per
tile**, which on a 250-camera wall is 50 requests/second sustained, forever. The editor is one
browser tab, open while someone is typing.

**§VII, honestly.** ADR-0117 rule 1 binds implemented legs to a dashboard, and constitution §IV
records `Dashboard: no` for all six — a repo-wide pre-existing condition this spec neither
creates nor can discharge. Noted rather than passed over. **No cell of §IV's table changes**:
no leg is built here, none moves state, and the leg whose code is refactored stays
*recorded, not yet readable*.

---

## Out of scope, stated so it is not rediscovered as an omission

- **#2353** — the box is tile-relative while the type is viewport-relative. Blocked on #2337.
  No `cqw`, no `container-type`, no edit to `overlayLabelSurfaceStyle`. US3 changes which
  string is in the box and nothing about the box.
- **Design tokens** (#2342, gated on #2332) — no token is added, no CSS custom property, no
  entry in `ui/tokens/colors.css`.
- **Editing a published overlay's label** — `editDraftOverlayRevision` exists in
  `overlays.api.ts:117` and is wired to no UI (finding 6). So an overlay that breaks because a
  variable was later renamed still cannot be repaired through this dialog. A real gap, filed
  separately (see plan.md §Follow-ups), not fixed here.
- **Warning on *existing* published overlays whose references have since broken.** That is a
  list-page concern and needs the reverse index queried the other way round. Not this dialog.
- **Changing the wall's behaviour for an unresolvable placeholder.** Specified in FR-011,
  implemented, tested six ways. Nothing to decide (decision 2).
- **A fab picker in the editor** — ADR-0114 says the population is zero (decision 4).
- **Any change to `PlaceholderParser`, `Resolver` or `VariableValue.Render`.** These three are
  the resolution grammar and the wall's output. The new endpoint calls them; it does not edit
  them. **The reviewer's boundary check: a diff touching any of those three files is a defect.**
- **A combobox, a Radix Popover, or any floating surface on `overlay-editor-text`** — spec 055
  and the e2e seeds (US2's three reasons).
- **Marten, event sourcing, or a new bounded context.** One endpoint on an existing group.
