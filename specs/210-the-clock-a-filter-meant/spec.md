# Spec 210 — The clock a filter meant

**Issue:** #2431 — *The audit page's date filters and its table use different clocks: a local-time input is sent with no offset and resolved server-side in UTC*
**Branch:** `2431-audit-date-clock-mismatch`
**Base:** `origin/develop` @ `3263084c`
**Phase-4a colour:** **RED** (behaviour-changing). The `since`/`until` a search is issued with changes value — today a bare wall clock, after the fix an instant. A test that arrives green is a phase-4 failure (ADR-0139, constitution §Testing).
**ADRs:** **ADR-0075** (Redux Toolkit + RTK Query — where the request argument is built and where it is *not* rewritten), **ADR-0074** (two apps; this is `management-web` only), ADR-0077 (Radix + Tailwind primitives — `Input`/`FormField`), ADR-0079 (RHF + Zod — *not* used by this filter bar; see §Blessed), ADR-0106 (the YARP gateway the request travels through), ADR-0108 (Playwright e2e — and why the e2e level cannot hold this guard), ADR-0109 (`[P]` markers), ADR-0139 (new behaviour starts red), ADR-0144 (autonomous lane), ADR-0037 (phases).
**Constitution:** §Testing (red for new behaviour), §VII (observability — the audit trail is the record, and a filter that silently excludes rows corrupts a reader's view of it).

**No new ADR is required.** Nothing here decides architecture. The wire contract already says what `since`/`until` are — `DateTimeOffset?`, an instant — and the client has simply been sending something that is not one. This makes the producer honour a contract the consumer already declared.

---

## Problem

Every line number, type and predicate below was **re-read in the working tree at HEAD `3263084c`**, not copied from the issue. The line numbers the issue quotes are still exact.

### The client sends a bare wall clock

`apps/management-web/src/features/audit/AuditPage.tsx`:

```tsx
:15    since: string;            // FilterDraft
:16    until: string;
...
:34    if (draft.since !== '') query.since = draft.since;     // toQuery()
:35    if (draft.until !== '') query.until = draft.until;
...
:136   <Input id="audit-since" type="datetime-local" value={draft.since} onChange={setField('since')} />
:139   <Input id="audit-until" type="datetime-local" value={draft.until} onChange={setField('until')} />
```

`Input` (`apps/shared/src/ui/primitives/Input.tsx`) spreads `...rest` onto a bare `<input>`, so `type="datetime-local"` reaches the DOM unchanged. An `<input type="datetime-local">` reports a **local wall clock with no UTC offset** — `2026-09-17T08:00` — and `toQuery` copies that string into `SearchAuditInput.since` verbatim. No conversion happens anywhere between the input and the wire:

- `toQuery` (`:28-37`) copies, it does not transform.
- `onSubmit` (`:62-66`) calls `setApplied(toQuery(draft))` and nothing else.
- `useSearchAuditQuery(applied)` (`:59`) hands the object straight to RTK Query.
- `searchAudit`'s `query` (`apps/shared/src/api/audit.api.ts:71`) is `(input) => ({ url: '', method: 'GET', params: definedParams({ ...input }) })`.
- `definedParams` (`:55-63`) only **drops** `undefined` and `''`; every surviving value is passed through unchanged.

**`grep -rn "toISOString()" apps/` returns zero hits across the whole workspace.** There is no conversion layer anywhere in either app, so the absence is not "converted somewhere else" — it is simply not done.

### The server resolves the missing offset in its own zone

`src/AuditObservability/Api/AuditEndpoints.cs:67-68` (`Search`) and `:104-105` (`GetTimeline`):

```csharp
[FromQuery] DateTimeOffset? since,
[FromQuery] DateTimeOffset? until,
```

A `DateTimeOffset` bound from a string carrying no offset takes one from the process's own time zone. **No `TZ` is set anywhere** — `grep -rn "TZ=" deploy/ src/AppHost/` matches only two binary `.mp4` fixtures, nothing configuring a zone — so the services run on the Linux container default, UTC.

This conclusion does not depend on which `DateTimeStyles` minimal-API binding uses, and that is worth saying because it would otherwise be an unverified SDK claim. Both candidate behaviours — "assume the value is local, then normalise" and "assume the value is already UTC" — land on the **same** instant when the process's zone *is* UTC. `2026-09-17T08:00` becomes `2026-09-17T08:00:00Z` either way. The defect needs only that the server's zone differ from the operator's, which is true for every operator outside UTC.

### The table reads a different clock

`AuditPage.tsx:79` and `:198`:

```tsx
cell: (row) => new Date(row.occurredAt).toLocaleString()
```

`occurredAt` is an ISO instant from the API (`AuditRow.occurredAt: string`, `audit.api.ts:6`); `toLocaleString()` renders it in the **browser's** zone. So the two halves of one page disagree: the column is browser-local, the filter is server-UTC.

### The failure, restated with the numbers checked

Measured (`node`, three zones, same input string `2026-09-17T08:00`):

| Browser zone | What the table shows for instant `06:00Z` | What the filter sends today | What the server reads it as |
|---|---|---|---|
| `Europe/Berlin` (UTC+2, summer) | `08:00` | `2026-09-17T08:00` | `08:00Z` = **10:00 Berlin** |
| `America/New_York` (UTC−4) | `08:00` | `2026-09-17T08:00` | `08:00Z` = **04:00 New York** |
| `UTC` | `08:00` | `2026-09-17T08:00` | `08:00Z` — correct by accident |

A Munich operator typing the time they can read in the *When* column loses the **two hours after it**. A New York operator gets the mirror-image defect: four hours of rows they did not ask for. Nothing on screen indicates either. **The direction of the error flips with the sign of the offset**, which is why "just tell people to add two hours" is not a workaround.

---

## Verifying the fix direction, and the one thing that breaks it

The issue proposes `new Date(draft.since).toISOString()`. It is correct, and the two edge cases that could have sunk it do not.

**The bare form parses as local time by specification, not by browser convention.** ECMA-262 §21.4.3.2 has required, since ES2016, that a date-*time* form without an offset be interpreted as local time (a date-*only* form is UTC — that asymmetry is the historic trap, and it does not apply here because `datetime-local` never yields a date-only value). Measured across three zones and both `datetime-local` value shapes:

```
              YYYY-MM-DDTHH:mm        YYYY-MM-DDTHH:mm:ss
UTC           2026-09-17T08:00:00.000Z   2026-09-17T08:00:30.000Z
Europe/Berlin 2026-09-17T06:00:00.000Z   2026-09-17T06:00:30.000Z
New_York      2026-09-17T12:00:00.000Z   2026-09-17T12:00:30.000Z
```

Both shapes parse; the seconds-bearing form (which a `step` attribute can produce, though this page sets none) is handled identically.

**The empty case is already guarded, and the invalid case is not.** `toQuery` only reads `draft.since` inside `if (draft.since !== '')`, so clearing a filter never reaches the conversion — `new Date('')` is `Invalid Date` and would throw. But `new Date('nope').toISOString()` throws `RangeError: Invalid time value`, which in a render path blanks the page. Neither a real browser's `datetime-local` nor jsdom will produce an invalid non-empty value — both sanitise an incomplete or invalid entry straight back to `''` on assignment, measured against the pinned jsdom version. But `toQuery` and `toInstant` are pure functions over `FilterDraft`, not over the DOM element that currently produces it — any future non-DOM writer of that state (a URL-restore feature, a saved-filter feature) could still hand them an invalid string. The conversion therefore returns `undefined` for an unparseable value, which `definedParams` already drops — the filter is simply not applied, which is exactly what the field being blank means. This is boundary validation at the one place the untrusted shape enters, not drive-by error handling.

### Where the conversion belongs, and where it must not go

**In `AuditPage.tsx`, not in `audit.api.ts`.** The shared client's `SearchAuditInput.since` is documented as a query param that maps to a `DateTimeOffset?` — an instant. Putting a `new Date(...)` normaliser inside `searchAudit`'s `query` or inside `definedParams` would make the shared client silently rewrite **any** caller's value, and would push a concern that belongs to one input widget into a module that three endpoints share. The wall-clock string exists for exactly as long as it takes to leave the page that produced it; that is where it is converted.

There is no existing hook to hang it on: RTK Query's `query` function is the only request-argument mapper in this API and it is deliberately generic. So the shape is a small module-local function in `AuditPage.tsx`, called from `toQuery`.

---

## Is this a pattern or an instance?

**An instance. One file, two fields.**

- `grep -rn 'datetime-local' apps/` → **two hits, both `AuditPage.tsx:136,139`.** No other date-range filter exists in either app.
- `ResourceTimelineInput` (`audit.api.ts:44-52`) also declares `since`/`until`, and `GetTimeline` binds them as `DateTimeOffset?` — the same server-side shape. But `grep -rn "useGetResourceTimelineQuery" apps/` returns **only its own export line** (`audit.api.ts:91`). Nothing consumes it, so it has no client that can get the zone wrong. It is a latent twin, not a second defect.

Scope stays at `AuditPage.tsx`. No shared module changes.

---

## Blessed — checked, and deliberately left alone

| Thing | Why it stays |
|---|---|
| The filter bar uses `useState`, not React Hook Form (ADR-0079) | A pre-existing deviation predating this issue. ADR-0079 is about forms with validation; this is a six-field filter bar with none. Converting it is a refactor, and CLAUDE.md forbids mixing a refactor into a bug fix. |
| `toLocaleString()` in the *When* column (`:79`, `:198`) | Browser-local is the right rendering, and after the fix it is the *same* clock the filter speaks. Changing it would reintroduce the mismatch from the other side. |
| `ResourceTimelineInput.since` / `until` | No caller. Fixing an unused path is speculative generality. |
| The server binding (`AuditEndpoints.cs`) | It correctly declares an instant. Making it reject offset-less input would be a contract change affecting every API consumer, and would turn a silent wrong answer into a 400 for the *server's own* OpenAPI-documented type. The client is the party in the wrong. |
| `definedParams` (`audit.api.ts:55-63`) | Already drops `undefined`, which is what makes the invalid-value fallback a one-liner rather than a branch. |

---

## Locked technical choices

| Concern | Choice |
|---|---|
| App | `apps/management-web` only. No backend, no database, no contract, no migration. |
| Conversion | `new Date(wallClock).toISOString()`, guarded by `Number.isNaN(parsed.getTime())`. No date library is added — none is in the workspace today and one field does not justify one. |
| Location | Module-local function in `apps/management-web/src/features/audit/AuditPage.tsx`, called from `toQuery`. |
| Test framework | Vitest + Testing Library + `userEvent`, matching `AuditPage.test.tsx`'s existing `vi.mock` of `useSearchAuditQuery` and its `expect(searchMock).toHaveBeenLastCalledWith(expect.objectContaining({...}))` assertion. |
| TypeScript | `exactOptionalPropertyTypes` is `false` (`tsconfig.base.json`), so assigning `string \| undefined` to `since?: string` compiles. Verified, because with it `true` the fix would need a different shape. |

## Latency-budget impact

**N/A.** The audit search is an operator-initiated `GET` through the gateway. It is not on the `event arrival → overlay rendered` path and touches none of constitution §IV's six legs.

## Assumptions

1. **Operators read the audit page in their own local zone and expect the filter to mean the same clock the table prints.** Stated because the alternative — everyone works in UTC and wants UTC filters — would make the current behaviour correct. The table's `toLocaleString()` already committed this app to browser-local display, so the filter follows the display, not the other way round.
2. **The bare-form-is-local rule is specification, not browser lore.** Measured on Node 22 (the version CI uses) across three zones; ECMA-262 has mandated it since ES2016 and every browser this repo supports (Chromium, per ADR-0108) implements it. Unavoidable residual guess: the measurement was on V8 via Node, not in each browser. The risk is negligible and the alternative — shipping an offset-computing hand-rolled parser — is strictly worse.
3. **The services run in UTC.** Verified by the absence of any `TZ` setting, not by reading a container's clock. If a deployment ever set `TZ`, the defect would get *worse*, not better; the fix is immune either way because after it the client sends an explicit `Z`.

---

## User stories

### US1 (P1) — A filter means the clock the operator can read

**As** an operator in a non-UTC fab,
**I want** the *Since* and *Until* filters to mean the same wall clock the *When* column prints,
**so that** typing the time I can see on a row actually catches that row.

Independently shippable and independently observable: with US1 alone the query is correct, and the correctness can be seen by typing a time and watching which rows survive. US2 changes no behaviour.

### US2 (P2) — The field says which clock it speaks

**As** an operator who has been bitten by this once,
**I want** the filter fields to name the zone they are interpreted in,
**so that** I do not have to trust that it was fixed.

**Scope decision, recorded explicitly:** the issue calls this "belt-and-braces" and it is. **US1 alone closes the correctness gap; US2 is a UX-clarity addition and is included, as a separate P2 story with its own task and its own commit.** It is included rather than deferred because it costs one string per field, has no runtime behaviour, and directly answers the issue's own observation that *"nothing on screen indicat\[es\] the filter was reinterpreted"*. It is kept **separate** so that if it is cut — for review churn, or because a designer wants different wording — P1 ships untouched. If this spec must be trimmed, cut US2, not US1.

---

## Acceptance scenarios

### US1 — the happy path (the filed defect)

```gherkin
Given the management app is running in a browser whose zone is UTC+2
  And the audit trail holds a row whose occurredAt is 2026-09-17T06:00:00Z
  And the When column therefore shows 08:00
 When the operator types 2026-09-17T07:00 into Since
  And clicks Search
 Then the search is issued with since = "2026-09-17T05:00:00.000Z"
  And the 06:00Z row is inside the filtered range
```

Today the search is issued with `since = "2026-09-17T07:00"`, the server reads `07:00Z` = 09:00 Berlin, and the row is excluded.

### US1 — the mirror case (a negative offset)

```gherkin
Given the browser's zone is UTC-4
 When the operator types 2026-09-17T08:00 into Since
 Then the search is issued with since = "2026-09-17T12:00:00.000Z"
```

Stated separately because the UTC+2 case alone cannot distinguish "converted correctly" from "shifted by a constant".

### US1 — both bounds

```gherkin
Given the browser's zone is any zone
 When the operator sets Since to 2026-09-17T08:00 and Until to 2026-09-17T18:00
  And clicks Search
 Then both since and until are issued as ISO instants ending in Z
  And neither retains the bare 2026-09-17T08:00 form
```

### US1 — zone-independent invariant (the one CI can hold)

```gherkin
Given the test process runs in any time zone, UTC included
 When the operator applies a Since filter
 Then the issued since is exactly new Date(<the typed value>).toISOString()
  And it therefore ends in "Z" and carries milliseconds
```

**This is the assertion that must be red in CI.** CI's frontend job runs on `ubuntu-latest` (`ci.yml:136`), i.e. **UTC**, where a zone-*dependent* assertion would pass against the unfixed code and prove nothing. The bare string `2026-09-17T08:00` never equals an ISO instant in *any* zone, so this form fails red everywhere. See plan.md §*The trap this test must not fall into*.

### US1 — clearing a filter (bad-request-adjacent)

```gherkin
Given the operator has applied a Since filter
 When they click Clear
 Then the search is issued with no since key at all
  And no RangeError is raised
```

`new Date('').toISOString()` throws; the existing `!== ''` guard must survive the edit.

### US1 — an unparseable value

```gherkin
Given the Since field somehow holds a value that is not a datetime
 When the operator clicks Search
 Then the search is issued without a since filter
  And the page still renders
```

Unreachable through `datetime-local` in a real browser or in jsdom — both sanitise an invalid entry back to `''`; reachable only through some future non-DOM writer of `FilterDraft`, which the test drives directly by bypassing the DOM sanitiser.

### US1 — the other filters are untouched

```gherkin
Given the operator types an event kind and an actor
 When they click Search
 Then eventKind and actorUsername are issued verbatim, unconverted
```

Guards against a conversion applied to the wrong field, which `toQuery`'s uniform shape makes easy to do.

### US1 — auth

```gherkin
Given the operator does not hold sse.audit.read
 When any search is issued, converted or not
 Then the gateway and AuditEndpoints refuse it exactly as before
```

**No authorization surface changes.** No scope, no fab guard, no endpoint, no claim is touched — this spec does not reach the server at all. The scenario is recorded to make that explicit rather than to add a test.

### US2 — the field names its clock

```gherkin
Given the operator opens the audit page
 Then the Since field's label names the zone it is interpreted in
  And the Until field's label does the same
```

---

## Independent end-to-end test procedure

Runnable by a human, no test framework, and it fails today:

1. Boot the stack (`dotnet run --project src/AppHost`) and sign in to `management-web` as an operator.
2. Open **Audit**. Note the *When* value of the newest row — call it `T`, shown in your browser's local zone.
3. Open devtools → Network, filter on `audit`.
4. Type a *Since* one minute **before** `T`, click **Search**.
5. **Before the fix, in any non-UTC browser zone:** the request URL carries `since=2026-09-17T08%3A00` — no `Z`, no offset — and, west of UTC, the row you were aiming at is gone from the table. (East of UTC you get extra rows instead; either way the URL is the decisive artefact.)
6. **After the fix:** the request URL carries `since=2026-09-17T06%3A00%3A00.000Z`, and the row is present.
7. Set your OS zone to UTC and repeat: the URL carries `Z` in both eras, and the row behaves identically. **This step is what shows why the automated guard cannot be zone-dependent.**

The URL is checked rather than only the row set, because a row set can coincidentally match.

---

## Out of scope

- Any backend change. `AuditEndpoints.cs` is not edited.
- `ResourceTimelineInput` and `getResourceTimeline` — no caller (§*Is this a pattern or an instance?*).
- Converting the filter bar to React Hook Form + Zod (ADR-0079).
- A date/time library.
- An explicit zone *picker* (filter in a fab's zone rather than the browser's). That is a product decision, would need an ADR, and is not what #2431 reports.
- A Playwright e2e for the conversion. ADR-0108's suite runs Chromium in the CI container — **UTC** — where the bug is invisible. An e2e here would be a test that cannot fail. `e2e/audit.spec.ts` stays as it is.

---

## File contention

Three files, all inside `apps/management-web/src/features/audit/`, plus this spec directory. Nothing shared, nothing backend, no `Shared.Contracts`, no migration, no AppHost. Fully `[P]`-safe against any concurrently-running backend spec (ADR-0109).

| File | Change |
|---|---|
| `apps/management-web/src/features/audit/AuditPage.tsx` | US1: the conversion. US2: two label strings. |
| `apps/management-web/src/features/audit/AuditPage.test.tsx` | New cases, existing ones untouched. |
| `specs/210-the-clock-a-filter-meant/*` | These artifacts. |
