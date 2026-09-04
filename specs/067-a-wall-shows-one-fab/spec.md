# Feature Specification: A wall shows one fab

**Branch**: `fix/2069-a-wall-shows-one-fab` · **Issue**: #2069
**Created**: 2026-09-04 · **Status**: Phase 1 complete, awaiting review
**ADRs**: **0145 (new, in this branch)**, 0114, 0115, 0112, 0129, 0109
**Specs it continues**: 014 (FR-015), 017 (FR-018, FR-019), 063

---

## Summary

A kiosk session held by a multi-fab principal has no idea which plant its wall
belongs to, so it shows whichever plant answered last — or, on the opening
label, whichever plant's name sorts first. `Layout.Fab` has been the answer on
the wire since spec 017; nothing on the kiosk reads it.

Three surfaces, one cause. **The fix is to read a field that is already there**
on two of them, and to add one field to a wire shape on the third.

---

## The premise, and the decision under it

**Multi-fab is real in production.** One deployment can serve several fabs and
an operator can legitimately hold two fab groups — the seeded
`op-multi@smart-sentinel-eye.test` (realm `groups: ["/fabs/munich",
"/fabs/dresden"]`) is a test-time instance of a production shape, not a
dev-stack artefact. **This is a production defect.**

**ADR-0145 (this branch) decides**: for a kiosk, the fab is *derived* from the
wall — `Layout.Fab` — and is never chosen. ADR-0114 explicitly deferred this
question (`0114:101-103`), which is why it earns an ADR rather than arriving as
an implementation detail. **The deferral for writes stays deferred.**

**This spec does not depend on the current wording of ADR-007 or constitution
§VI.** Those texts contradict the premise above and are filed separately as
#2080. Nothing here amends them, and nothing here reads them.

---

## The defect, located

Every citation below was read on this branch at `d0faa47`.

### (a) The kiosk never learns its wall's fab

`LayoutDto` has carried `string Fab` since spec 017
(`src/LayoutComposition/Application/DTOs/LayoutDto.cs:16-29`), and
`GetLayoutQueryHandler` populates it (`:39` — `Fab: layout.Fab.Value`).
`GET /layouts/{id}` — which `CellPage` already calls — returns it today.

The TypeScript `Layout` interface simply does not declare it
(`apps/shared/src/api/layouts.api.ts:35-43`). **This is a client-side field
read, not a server change.**

### (b) A pushed frame does not say whose it is

```csharp
// ResolvedOverlayTextChangedHubMessage.cs:8
public sealed record ResolvedOverlayTextChangedHubMessage(Guid Overlay, string ResolvedText, long Version);
// OverlayHighlightChangedHubMessage.cs:9
public sealed record OverlayHighlightChangedHubMessage(Guid Overlay, int DurationMs);
```

The broadcaster **has** the fab and uses it to pick the group —
`SignalRLayoutLifecycleBroadcaster.cs:102-118` for resolved text and `:120-133`
for the highlight, both calling `LayoutLifecycleHub.FabGroup(notification.Fab)`
— then drops it from the payload. `LayoutLifecycleHub.OnConnectedAsync:45-53`
joins **one group per fab in the token**, so a multi-fab connection receives
both plants' frames on one socket. Overlays are fab-neutral templates
(ADR-0115), so the same overlay GUID is legitimately pushed by every fab.

The client filters on `boundOverlays.has(message.overlay)` and a per-overlay
version high-water mark, and on nothing else (`CellPage.tsx:140-157`).

**Ordering matters.** The version guard at `CellPage.tsx:142-144` writes
`overlayTextVersionsRef` on every accepted frame. A foreign frame that advanced
that mark would *suppress a later legitimate one* — turning a wrong label into
a frozen one. The fab filter must run **before** it.
`onOverlayHighlightChanged` (`:155-157`) has **no version guard at all**; the
fab filter applies to it too, and this spec does not add a version guard to it
(out of scope, below).

### (c) The opening label is resolved in the wrong fab — deterministically

The half the issue does not mention, and the one that fires every time.

```csharp
// GetOverlaySnapshotQueryHandler.cs:74-87
foreach (FabIdentifier fab in fabs.OrderBy(candidate => candidate.Value, StringComparer.Ordinal))
```

`"dresden" < "munich"`, so a multi-fab operator opening a **Munich** wall gets
its opening label resolved from **Dresden** — before any push, on every load,
with no race involved.

The endpoint **already accepts `?fabId=`** and routes it through
`FabResolution.ResolveForReadAsync`
(`src/SystemVariables/Api/SystemVariableEndpoints.cs:305-334`). The client
sends none (`apps/shared/src/api/systemVariables.api.ts:142-147`). **The fix is
to pass it.** Nothing is invented server-side.

**Two comments assert the false premise, not one.** The reconnaissance named
`:67-73`; `:40-44` says the same thing:

- `:40-44` — *"First by fab name where the caller holds several — arbitrary but
  stable, and a kiosk holds exactly one."*
- `:67-73` — *"A kiosk holds exactly one fab so the loop resolves on its first
  iteration."*

`op-multi` falsifies both. Both are corrected in this feature.

---

## User Scenarios & Testing

### User Story 1 — the opening label lands in the wall's plant (P1)

A multi-fab operator opens a Munich wall. Every tile's label resolves from
Munich's variables. No push is involved and nothing has to happen on the plant
floor for the difference to be visible.

**Independently shippable.** Two TypeScript edits and a comment correction. It
is observable end to end on its own, and it introduces the `Layout.fab` field
US2's filter needs — which is why it is P1 despite US2 carrying the larger
consequence. Both ship in this feature.

#### Acceptance scenarios (Gherkin)

**Happy path — the opening label**

```gherkin
Given a variable "oeeline1" exists in fab "munich" with value "82.5"
  And a variable "oeeline1" exists in fab "dresden" with value "41.0"
  And a published overlay's label text is "OEE {{oeeline1}}"
  And a published layout in fab "munich" binds a tile to that overlay
 When an operator holding both "munich" and "dresden" opens that wall
 Then the snapshot request names fabId "munich"
  And the tile's opening label reads "OEE 82.5"
```

**Conflict — the ordering that produced the defect**

```gherkin
Given the arrangement above
  And "dresden" sorts before "munich" ordinally
 When the snapshot is requested without a fabId
 Then it resolves from "dresden" and reads "OEE 41.0"
```

That second scenario is the **control**. It describes today's behaviour, it
stays true of the endpoint for a caller who names no fab, and it is what proves
the endpoint was always capable and only the caller was silent.

**Single-fab operator — unchanged**

```gherkin
Given an operator holding only "munich"
 When they open a "munich" wall
 Then the label reads "OEE 82.5"
  And it read "OEE 82.5" before this change as well
```

**Auth — naming a fab the caller does not hold**

```gherkin
Given an operator holding only "munich"
 When a snapshot is requested with fabId "dresden"
 Then the request is refused with 403
  And no resolved text is returned
```

`FabResolution.ResolveForReadAsync` already does this through
`IFabAuthorizationGuard`. A regression guard, not new work.

**Bad request — the endpoint's existing shape**

```gherkin
Given a request to GET /system-variables/snapshot
 When overlayIdentifier is an empty Guid
 Then the request is refused with 400 and title "VARIABLE_INVALID_INPUT"
```

---

### User Story 2 — a wall refuses another plant's frame (P2)

A multi-fab operator watches a Munich wall while a Dresden operator changes a
figure. The wall does not move. Munich's own next change still lands.

P2 because it builds on the `Layout.fab` field US1 introduces. It carries the
greater consequence — this is the leak the phase-6 security review of #2012
found, and it became reachable only once that fix started delivering frames.

#### Acceptance scenarios (Gherkin)

**Happy path — the frame is self-describing**

```gherkin
Given a multi-fab operator holds a kiosk hub connection
  And a variable "oeeline1" exists in both "munich" and "dresden"
  And a published overlay references it
 When an operator sets "oeeline1" in "dresden"
 Then a ResolvedOverlayTextChanged frame reaches that connection
  And its payload carries fab "dresden"
```

The frame still **arrives**. The connection legitimately holds both groups, and
a console wants both. What changes is that the frame now says whose it is.

**Conflict — the wall drops it**

```gherkin
Given a kiosk displaying a layout whose fab is "munich"
  And a tile on it binds overlay X
 When a ResolvedOverlayTextChanged frame for overlay X arrives carrying fab "dresden"
 Then the tile's label is unchanged
  And the per-overlay version high-water mark is not advanced
 When a later frame for overlay X arrives carrying fab "munich" and a LOWER
      version than the dresden frame carried
 Then the tile's label updates to the munich text
```

The second half is the whole point of the ordering constraint. Munich and
Dresden share one counter today (see *Out of scope*), so the "lower version"
case is not contrived.

**Conflict — the highlight, which has no version guard**

```gherkin
Given a kiosk displaying a layout whose fab is "munich"
  And a tile on it binds overlay X
 When an OverlayHighlightChanged frame for overlay X arrives carrying fab "dresden"
 Then no tile lights
 When the same frame arrives carrying fab "munich"
 Then every tile bound to overlay X lights for its duration
```

**Regression control — a matching frame still applies**

```gherkin
Given a kiosk displaying a layout whose fab is "munich"
 When a ResolvedOverlayTextChanged frame arrives carrying fab "munich"
      and a version higher than the last applied
 Then the tile's label updates without a re-fetch
```

Not optional. "Nothing applied" passes the two drop scenarios trivially, and
the snapshot cache-key change US1 makes (plan.md, Risk 1) can break exactly
this while every fab assertion stays green.

**Fail-closed — a frame with no fab**

```gherkin
Given a kiosk displaying a layout whose fab is "munich"
 When a frame arrives with no fab field at all
 Then it is dropped
```

Same direction the server already takes for an event carrying no fab. It also
means the client half **must not ship ahead of the server half**: deployed
alone it would drop every frame and freeze every wall.

**Auth — the hub still refuses a caller without the scope**

```gherkin
Given a bearer token carrying neither "sse.layouts.read" nor "sse.management"
 When a client attempts to connect to /hubs/layouts
 Then the connection is refused
  And no frame of any kind reaches it
```

---

## Independent end-to-end test procedure

Runnable by a person with no knowledge of the fix. It distinguishes a working
system from today's on **both** stories.

1. Boot the full stack in run mode: `dotnet run --project src/AppHost`.
2. In management-web as `admin` (munich): define variable `demo1` with value
   `MUNICH`, publish an overlay whose text is `Line {{demo1}}`, and publish a
   one-tile munich layout binding a camera and that overlay.
3. As `op-multi@smart-sentinel-eye.test` / `Operator1234`, define `demo1` in
   **dresden** with value `DRESDEN`. The console will require the fab to be
   named — that is ADR-0114 working, not a defect.
4. Open kiosk-web, sign in as `op-multi@smart-sentinel-eye.test` (the kiosk
   uses the ordinary Keycloak form flow — `e2e/support/kiosk-session.ts`), and
   open the munich wall.
   - **Today:** the label reads `Line DRESDEN`. *(US1 — deterministic.)*
   - **Fixed:** it reads `Line MUNICH`.
5. Leave the wall open. In another browser, as `op-multi`, set **dresden**'s
   `demo1` to `DRESDEN-2`.
   - **Today:** the wall changes to `Line DRESDEN-2`. *(US2.)*
   - **Fixed:** the wall does not move.
6. Now set **munich**'s `demo1` to `MUNICH-2`.
   - **Fixed:** the wall reads `Line MUNICH-2`. If it does not, the fab filter
     is running after the version guard, or the snapshot cache key moved
     without the push upsert following it.

Step 6 is the step that fails if the fix is built in the wrong order. Do not
skip it.

---

## Requirements

- **FR-001**: The kiosk MUST derive the fab it is displaying from the layout it
  is displaying (`Layout.Fab`), and MUST NOT infer it from the token, ask for
  it, or hold it as session state. (ADR-0145)
- **FR-002**: `GET /layouts/{id}`'s TypeScript response type MUST declare the
  `fab` field the server already returns.
- **FR-003**: The kiosk MUST request an overlay snapshot for the displayed
  wall's fab, by naming it as `fabId`.
- **FR-004**: `ResolvedOverlayTextChangedHubMessage` and
  `OverlayHighlightChangedHubMessage` MUST each carry the fab the broadcast is
  addressed to.
- **FR-005**: The kiosk MUST discard a resolved-text or highlight frame whose
  fab differs from the displayed layout's fab, **before** any other per-frame
  state is read or written — in particular before the per-overlay version
  high-water mark.
- **FR-006**: A frame carrying no fab MUST be discarded (fail closed).
- **FR-007**: A frame whose fab matches MUST continue to apply exactly as it
  does today, including the in-place snapshot cache upsert with no re-fetch.
- **FR-008**: The two comments in `GetOverlaySnapshotQueryHandler` asserting
  that a kiosk holds exactly one fab (`:40-44`, `:67-73`) MUST be corrected.
  They state a premise `op-multi` falsifies.
- **FR-009**: No server-side behaviour of `GET /system-variables/snapshot`
  changes. Its `fabId` handling, its guard, and its no-`fabId` ordering are
  untouched; only the caller stops being silent.
- **FR-010**: Management-web's behaviour MUST NOT change. It is a console, and
  ADR-0115's resolve-in-the-caller's-fabs behaviour is correct there.

### Known caveat — recorded, not solved

Spec 017 **FR-018** exempts pre-existing layouts from retro-validation, so a
legacy chain could carry tiles whose cameras belong to another fab.
`Layout.Fab` remains the single authoritative answer to *"whose wall is this"*,
so deriving from it is correct regardless — but *"no layout has cross-fab
tiles"* is **not** a guarantee the data enforces retroactively. This spec does
not widen to fix it, and does not claim it.

### Out of scope — each with its reason

- **The version counter is keyed on overlay, not `(fab, overlay)`**
  (`IReverseIndex.NextVersionFor`, `InMemoryReverseIndex.cs:72`). Munich and
  Dresden share one monotonic counter, so their frames interleave in one
  sequence. This is **related hardening, not a fix**: a correctly-versioned
  frame still arrives at the wrong wall, and the fab filter is what stops it.
  **Recommend as its own issue.** It is also why the "lower version" half of
  US2's conflict scenario is realistic rather than contrived.
- **`PublishedLayoutDto` has no `Fab`** (`LayoutDto.cs:55-62`), so a multi-fab
  operator's kiosk picker lists both plants' walls with nothing distinguishing
  two same-named ones — names are unique only *within* a fab (spec 017 FR-019),
  so a genuine collision is possible. **Cosmetic, and a separate slice**: it
  changes what a picker shows, not what a wall shows.
- **A version guard for `OverlayHighlightChanged`.** It has none today. Adding
  one is scope creep; the fab filter applies to the frame either way.
- **ADR-007 and constitution §VI**, whose text contradicts multi-fab. **#2080.**
  Not touched, not read, not depended on.
- **An e2e test.** Driving a second Keycloak account through the browser is
  exactly what `e2e/cameras.spec.ts:39-44` declines to do, and says why: it
  would be testing Keycloak's login form. The multi-fab half is covered over
  HTTP by integration tests and in the DOM by component tests — the same
  division `CameraFabResolutionIntegrationTests` already uses.
- **A cross-fab wall** (tiles from two plants on one screen). ADR-0145 decides
  against it for the kiosk.

---

## Locked technology choices

Nothing new is introduced. The feature uses what is already in the stack:

| Concern | Choice | ADR |
|---|---|---|
| Real-time push | SignalR behind the replaceable-transport seam | 0076 |
| Frontend state | Redux Toolkit + RTK Query | 0075 |
| Fab resolution | `FabResolution` + `FabClaims` + `IFabAuthorizationGuard` | 0114 |
| Overlay semantics | Fab-neutral template; placeholders resolve in the viewer's fab | 0115 |
| Kiosk's fab | **Derived from `Layout.Fab`** | **0145** |
| Test framework | xUnit + Shouldly; Aspire fixture for integration; Vitest for components | 0052, 0103 |
| Commits | Conventional Commits, **no `Co-Authored-By`** | 0030, 0086 |

---

## Latency budget impact (constitution §IV) — **not N/A**

**Leg affected: `Event → overlay state` (≤ 200 ms).**

- The fab filter is a string comparison in an existing hub callback, placed
  ahead of an existing `Map` lookup. No I/O, no fetch, no allocation of
  consequence. For a *dropped* frame it strictly removes work — a dispatch and
  a cache write that used to happen no longer do.
- The hub payload grows by one short string per frame. Named for honesty, not
  because it is measurable against a 200 ms budget.
- US1's `?fabId=` rides a query the tile already issues on mount. The cold-load
  snapshot fetch is **not** one of §IV's six legs; it is page load.

**No re-measurement is claimed and none is required.** §IV records this leg as
*"recorded, not yet readable"*; this change does not move that cell, and the
verification note must say so rather than implying a figure. §VII's dashboard
obligation is unchanged.

---

## Assumptions

1. **`Layout.Fab` is trustworthy as the wall's fab.** Stated in ADR-0145 with
   its FR-018 caveat. Everything here rests on it.
2. **A multi-fab kiosk session is a real production shape**, per the
   maintainer's decision of 2026-09-04. Were it not, (a) and (b) would still be
   latent and (c) would still fire for any multi-fab console user.
3. **Adding a positional field to the two hub records breaks no C# test.**
   Checked: `grep -rn "HubMessage" tests/` finds no construction of either
   record.

## Guesses marked

- **The snapshot integration test gets a new file** rather than joining
  `VariableFabResolutionIntegrationTests`, whose `InitializeAsync` resets only
  SystemVariables and which has no overlay or reverse-index arrangement. A
  guess about placement, not about behaviour; plan.md names the file and the
  arrangement it duplicates.
- **`getOverlaySnapshot`'s RTK Query argument becomes an object.** There is no
  other way to carry a second parameter into a query, but it moves the cache
  key — plan.md treats that as the feature's principal risk.
