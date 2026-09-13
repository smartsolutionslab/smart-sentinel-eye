# Spec 141 — A safe-looking branch says so

**Issue:** #2197 (part 2 of 3 from #2109) · **Branch:** `fix/2197-safe-looking-branches-say-so`
**Phase:** 1 (Specify) · **Date:** 2026-09-13 · **Tree read at:** `4a794fce`
**ADRs:** ADR-0037 (the phased workflow), ADR-0074/0075 (the two React apps and
their state), ADR-0079 (Zod is scoped to *form input* — the reason option 1 of
#2109 is stopped here), ADR-0106 (every REST call reaches a context through the
YARP gateway, which is why the collection URL below resolves at all), ADR-0109
(parallel work owns disjoint files), ADR-0112 §2 (one overlay may be bound to
several tiles), ADR-0117 (an implemented leg is subject to §VII), ADR-0139 +
constitution §Testing (new behaviour starts red), ADR-0144 (the lane may not
write an ADR and may not weaken a gate), ADR-0145 (the wall's fab is *derived*
from the layout it displays).
**Constitution:** §IV (the latency budget — `Event → overlay state`), §VII.

---

## What this is, and what it is not

**Hardening, and the difference is proven rather than assumed.** All three
sites are unreachable from today's server, re-verified against this tree:

- `src/LayoutComposition/Application/DTOs/LayoutDto.cs:72` declares
  `Guid? OverlayIdentifier` **positionally** inside `TileDto` (`:70`), and
  `LayoutComposition` sets no `DefaultIgnoreCondition` — the only one in `src/`
  is `HttpKeycloakAdminClient.cs:38`. So `"overlayIdentifier": null` is always
  emitted, never omitted.
- `LayoutDto.cs:25` declares `string Fab`, not `string?`.
- The placeholder delimiter is `{{…}}`, parsed server-side by
  `src/SystemVariables/Application/Resolution/PlaceholderParser.cs`.

What makes them reachable is a `DefaultIgnoreCondition` anywhere on the path, a
field made optional, a rename, or a second producer of the same shape. None is
exotic. The point of this spec is that the kiosk should **say so** when it
happens rather than take a safe-looking branch.

**Nothing here becomes a bug fix by being written up as one.** The PR body must
say hardening.

---

## Site 1 — settled: not a quiet 404, and not a quiet anything

#2197 says whether `useGetOverlayQuery('')` produces *"a quiet 404 or a thrown
`TypeError`"* is **not resolvable by reading** and should be settled by this
spec's red. It **is** resolvable by reading — not of the client, which is where
#2109 and #2197 both looked, but of the **route table and the realm**. Settled
here; the red then constructs what the reading proves.

The chain, each link verified:

| Link | Evidence |
|---|---|
| An omitted identifier is `undefined`, and `undefined !== null` is true | `CellPage.tsx:355` — `skip: overlayIdentifier === null` |
| So the query fires, with `''` | `CellPage.tsx:354` — `useGetOverlayQuery(overlayIdentifier ?? '')` |
| Which builds the path `/` | `overlays.api.ts:73` — ``query: (overlayIdentifier) => `/${overlayIdentifier}` `` |
| Joined onto the base as the **collection** URL | `gateway.ts:24/89` — base is `${origin}/overlay-designer/overlays`; RTK's `joinUrls` strips the leading slash and re-adds one, giving `…/overlays/` |
| The gateway forwards it | `src/ApiGateway/appsettings.json:59-60` — `Match: /overlay-designer/{**catch-all}` with `PathRemovePrefix`, so the service sees `GET /overlays/` |
| Which is a **mapped route** | `src/OverlayDesigner/Api/OverlayEndpoints.cs:59` — `group.MapGet("/", List)`. `GetOne` is `MapGet("/{overlayIdentifier:guid}")` (`:49`) and a `:guid` constraint cannot match an empty segment, so there is no 404 to be had here |
| And the kiosk is authorised for it | `:60` requires `Scope.Sse.Overlays.Read`; `src/AppHost/Realms/smart-sentinel-eye-realm.json:209-218` (client `kiosk-web`) and `:238-247` (client `kiosk-wall`) both carry `sse.overlays.read` — so not a 403 either |
| So the answer is **200, carrying the collection** | `OverlayEndpoints.cs:63` declares `.Produces<ListOverlaysResponse>(200)`; `OverlayEndpoints.Queries.cs:69` returns `Results.Ok(new ListOverlaysResponse(payload.Chains, payload.Published))` — JSON `{ "chains": […], "published": […] }` |
| Stored under cache key `''`, typed `Overlay`, it then meets | `CellPage.tsx:358` — `overlay?.revisions.find((r) => r.state === 'Published')` |
| `overlay` is a **truthy object**, so `?.` does not short-circuit; `overlay.revisions` is `undefined` | **`TypeError: Cannot read properties of undefined (reading 'find')`, thrown during render** |

**And the blast radius is larger than "one tile".** The throw is a render-phase
throw, so React unmounts the tree to the nearest boundary: `App.tsx:99`'s
`ErrorBoundary`, whose fallback is `KioskCrashRecovery` — which writes a crash
count to `sessionStorage` and **reloads the page** after a backoff
(`KioskCrashRecovery.tsx:45-56`). The reload refetches the same layout and
throws again. **An omitted `overlayIdentifier` puts the wall into a reload
loop**, showing "Recovering…" between reloads, with one
`[resilience] {subsystem: 'crash', transition: 'render-error'}` per cycle naming
a `TypeError` about `find`.

So the honest statement of site 1's residual harm, replacing both #2109's
*"drops all pushes for the life of the page"* and #2197's open question:

> Not silent, but **loud in the wrong place and about the wrong thing.** The
> kiosk reports a render crash in `CellPage` on a backoff loop; nothing anywhere
> names the layout field that is missing, and the crash-recovery path is
> designed to clear *corrupted client state*, which this is not — so it recovers
> into the same crash forever.

#2197's correction of #2109 survives intact and is adopted: `tilesToBoundOverlays`
(`:527-535`) *adds* `undefined` to the set rather than emptying it, so a mixed
layout keeps matching pushes for its real overlays, and *"drops all pushes"*
holds only when every tile omits the field — in which case dropping overlay
pushes is correct.

**One thing the same guard closes for free.** An overlay identifier that arrives
as the **empty string** — a different drift, equally unvalidated — also passes
`!== null`, produces the same `useGetOverlayQuery('')` request and the same
throw. #2084's sentinel catches both with one test; a `!== undefined` patch
would catch neither the second nor any future third.

---

## Site 2 — two guards, one field, and a reporter that switches itself off

`CellPage.tsx:379` — `fab === ''` inside the snapshot query's `skip`. The
sibling guards are `message.fab !== wallFab` at `:197` and `:233`.

If the layout's `fab` is ever absent, `data?.fab` is `undefined`, so:

1. `fab === ''` is **false**, the snapshot query is not skipped, and
   `fetchBaseQuery` strips the undefined `fabId` from the query string — which
   is exactly the cross-fab *"whichever sorts first"* request that
   `CellPage.tsx:69-77` exists to prevent and that #2069 closed.
2. `message.fab !== wallFab` is **true for every frame**, so every corrective
   push is dropped and the wrong text is never overwritten.

**And a third consequence that neither #2109 nor #2197 names, found here.**
#2084's own reporter goes permanently silent in exactly this case.
`countReportableSkew` opens with `if (wallFab === undefined) return null;`
(`CellPage.tsx:518`) — written for the window before the layout lands, where
nothing dropped is yet evidence of anything. A layout that carries **no** fab
leaves `wallFab` `undefined` for the life of the page, so the helper's first
line answers `null` to every frame forever. **The wall drops every push, and the
signal built to notice exactly that is switched off by the same absence.**

That is the argument for reporting site 2 at the *wall* rather than at the
frame: `countReportableSkew` is correct as written and is not changed — its
blind spot is not a frame-level fact and cannot be closed at frame level.

**The sentinel is #2084's, verbatim in shape.** `countReportableSkew`'s second
line is `if (typeof frameFab === 'string' && frameFab !== '') return null;`
(`:519`) — the same test #2197 quotes as `typeof fab !== 'string' || fab === ''`,
inverted because the helper returns early on the healthy case. Matching it is
most of the work, and nothing new is invented.

---

## Site 3 — only the delimiter sub-case, and what makes it detectable

`CellPage.tsx:368` — `publishedOverlay?.text?.includes('{{') ?? false`.

#2197's table is adopted unchanged. **Only the third row is in scope:**

| Change | Result | In scope |
|---|---|---|
| `text` **omitted** or **renamed** | `liveText` undefined (`:385`) → `renderOverlay` undefined (`:401-403`) → the tile shows **no label at all** | **No** — visible, not silent |
| the placeholder **delimiter** changes while `text` survives | `hasPlaceholder` false → the snapshot query is skipped (`:379`) → the tile renders the raw template | **Yes** — the only silent one |

**The delimiter is a contract the kiosk holds a third, unshared copy of.**
`PlaceholderParser.cs` parses `{{name}}` server-side and is what builds the
reverse index; `CellPage.tsx:368` re-states `{{` in TypeScript, with nothing
tying the two together. That is the same drift `REQUIRED_LAG_FIELDS`
(`wallAlignment.ts:70-76`) was hoisted to make impossible — except that here the
two readers are in different languages and different processes, so *declaring it
once* is not available.

**What is available is the disagreement itself, and it is exact.** A
`ResolvedOverlayTextChangedV1` is published only for overlays the server's own
index says reference a variable —
`VariableValueChangedDomainEventHandler.cs:50-62`: `reverseIndex.LookupOverlays(name)`,
skipping any overlay with no indexed label text. So:

> **A resolved-text push arriving for an overlay whose tile decided the label was
> static is, by construction, the server and the kiosk disagreeing about what a
> placeholder looks like.** There is no other way to produce it.

No delimiter list is guessed, no schema is added, and there are no false
positives from genuinely static labels: the server never pushes for one.

**Two ways to get this wrong, both closed by FR-005.** The tile must record
"static" only when it actually *knows* the text — not while the overlay query is
still loading (`publishedOverlay === undefined`), and not when `text` is absent
(the out-of-scope row above). Reporting on either would turn a not-yet into a
fault, which is the defect spec 095 FR-002 exists to prevent.

---

## Citations re-verified against `4a794fce`

`CellPage.tsx` is **562 lines**; every line number the two issues carry has
drifted. All still land in the construct they name.

| Cited as | Today | Construct |
|---|---|---|
| #2197 site 1: `:470-478` | **`:527-535`** | `tilesToBoundOverlays`, `tile.overlayIdentifier !== null` at `:530` |
| #2197 site 1: `:329-332` | **`:353-356`** | `useGetOverlayQuery(overlayIdentifier ?? '')` at `:354`, `skip:` at `:355` |
| (not cited) | **`:294-295`** | the render-side `cell.tile.overlayIdentifier !== null` pair — a **third and fourth** reader of the same sentinel |
| (not cited) | **`:358`** | `overlay?.revisions.find(...)` — where the collection response throws |
| #2197 site 2: `:353-356` | **`:377-380`** | `useGetOverlaySnapshotQuery`; `fab === ''` is on **`:379`** |
| #2197 site 2, sibling guard | **`:197`, `:233`** | `message.fab !== wallFab` |
| #2197 site 3: `:344` | **`:368`** | `publishedOverlay?.text?.includes('{{') ?? false` |
| #2084's sentinel | **`:513-525`** | `countReportableSkew`; the sentinel is `:519`, the blind spot `:518` |
| #2084's reports | **`:193-196`, `:229-232`** | `logResilienceEvent('hub', …)` |
| spec 095's declared-once list | `wallAlignment.ts:70-76` | read by `lagSampleFrom` `:98` and `missingLagFieldIn` `:148` |
| spec 095's latch | `CameraViewer.tsx:137-145` | `reportedMissingFieldsRef`, a `useRef<Set<string>>` |
| #2189's decade counter | `CameraViewer.tsx:420-426` | `countReportableFailure` |
| spec 095's quiet-case lesson | `CameraViewerAlignment.test.tsx:56-66` | the suite that once passed with the code under test deleted |

**`layouts.schema.ts:19` is not cited by this spec for anything.** #2197 is right
that it is the write-path form schema, consumed only by `gridDesignerModel.ts`.
It is not touched.

---

## User stories

### US1 (P1) — A kiosk that cannot trust a layout field says which one

**As** the person responsible for a 250-camera fab wall through a server
upgrade,
**I want** the kiosk to name the layout field it could not read,
**so that** a wall that has stopped applying pushes, or is querying across fabs,
or is showing an unresolved template, says which of those it is — instead of
looking healthy, or crash-looping about a `TypeError` in `find`.

**Everything here is P1 and is one story.** There is no P2. The three sites are
three fields of one DTO read by one component in one render pass; delivering two
of them leaves the third silent in the same file, and the issue's whole claim is
that a *category* of safe-looking branch should speak. Splitting would also mean
two engineers writing `CellPage.tsx` and `CellPage.test.tsx` at once, which
ADR-0109 forbids.

---

## Functional requirements

**FR-001 — A bound overlay is one the layout actually named, tested once.**
A single module-level helper in `CellPage.tsx` answers a tile's overlay
identifier when `typeof … === 'string'` and it is not `''`, and `null`
otherwise. **All four existing readers read it**: `tilesToBoundOverlays`
(`:527-535`), the `unavailable` and `highlighted` props (`:294-295`), and the
`Tile` query's `skip` (`:355`). This is spec 095's *declared once, read by both*
applied to a sentinel instead of a field list — four copies of `!== null` today
are four chances to fix three of them.

**The helper's parameter is deliberately wider than the DTO type**
(`string | null | undefined`), exactly as `countReportableSkew` takes
`frameFab: string | undefined` (`:515`) where `message.fab` is `string`.
**`LayoutTile.overlayIdentifier` stays `string | null`** in `layouts.api.ts:17`
and is not widened: widening it would push a `| undefined` through every consumer
in both apps to describe a server that does not exist, which is the
runtime-schema answer under another name.

**FR-002 — A tile whose overlay identifier is absent says so, once per mounted
page per session.** One
`logResilienceEvent('hub', 'tile-without-overlay-identifier', { layout, tiles })`
where `tiles` is how many tiles of the rendered revision are affected. Not once
per tile: one layout revision is one fact about one server response, and a 5×5
wall would otherwise put 25 identical lines on the console.

**FR-003 — Absent still means unbound; nothing gains a value.** The affected tile
still renders with no overlay, the overlay query is still skipped, the snapshot
query is still skipped, no identifier is invented and no default is supplied. The
only change is that the request for the collection URL — and therefore the
`TypeError` at `:358` and the reload loop behind it — no longer happens, and that
a line names the field.

**FR-004 — The fab sentinel matches #2084's, and a wall with no fab says so
once.** `:379`'s `fab === ''` becomes `typeof fab !== 'string' || fab === ''`.
One `logResilienceEvent('hub', 'layout-without-fab', { layout })` per mounted
page per session.

**`countReportableSkew` is not touched.** Its `wallFab === undefined` early
return (`:518`) is correct for the window it was written for, and this FR is what
covers the blind spot it leaves — stated here so a later reader does not "fix"
`:518` and start reporting a skew on every page load before the layout lands.

**FR-005 — A resolved-text push for a label the kiosk treats as static says so,
once per overlay per session.** In `onResolvedOverlayTextChanged`, after the
existing bound-overlay guard (`:166`) and after the existing fab guard (`:197`) —
so a foreign plant's frame never triggers it — one
`logResilienceEvent('hub', 'resolved-text-for-static-label', { overlay })`.

**A tile registers its verdict only when it knows the text.** The verdict is
recorded when `publishedOverlay !== undefined` **and**
`typeof publishedOverlay.text === 'string'`. While the overlay query is loading,
or when `text` is absent (the out-of-scope row of the table above), the tile
records nothing and the push is not reported. *Absent is not the same as
not-yet* — spec 095 FR-002, applied one layer up.

**FR-006 — Behaviour is unchanged; only the silence is.** Specifically:

- The snapshot query is still **skipped** for a label with no `{{`. This spec
  reports the disagreement; it does not resolve it by starting to query. That
  would add a round trip per static tile on every wall, and the reason for the
  skip (`:363-367`) is unchanged.
- The tile still renders the raw template. No text is substituted.
- The push still upserts the cache (`:210-220`) and still advances the version
  mark, exactly as today.
- `LiveUpdatesBadge` is untouched: a DTO skew is not a hub outage — the control
  #2084's suite already pins.
- `layouts.schema.ts`, `layouts.api.ts`, `overlays.api.ts`,
  `systemVariables.api.ts` and `resilienceLog.ts` are **not modified**.
- No DTO gets a runtime schema. No `safeParse`, no `.parse`, no Zod on any read
  path (see *§ The ADR question*).

**FR-007 — A latch, not the decade cadence, and here is why.** All three signals
are `useRef` latches — a boolean per page for FR-002 and FR-004, a `Set<string>`
keyed by overlay for FR-005 — declared at component scope, never inside an
effect, and never `useState` (a write would re-render a wall of live video to
record something nobody displays).

**Deliberately not `countReportableSkew`'s decade cadence**, and this FR is where
that choice is recorded, as spec 095 FR-003 recorded its own. #2084's cadence
exists because *frames arrive continuously and each one is a fresh drop*. None of
these three is a stream of drops: an omitted `overlayIdentifier` and an absent
`fab` are properties of one layout response, and a changed delimiter is a
property of the server's parser. A browser does not grow a stats field halfway
through a session (spec 095's phrasing); a server does not change its placeholder
syntax halfway through a wall's night. One line, then silence.

**FR-008 — Every new signal has a quiet case that proves its guard ran.**
Carried verbatim from 095's sharpest review finding: deleting `!applied &&` left
all 404 tests green while every tile permanently claimed a fault. For each of
FR-002, FR-004 and FR-005 there is a case on a **working** server asserting zero
lines of that transition **and** asserting positively that the guarded code path
executed — a call count, a rendered value, or a `skip` flag — never an absence
alone. The three are named in `tasks.md` and their constructions are below.

---

## Acceptance scenarios

### Site 1 — the defect

```gherkin
Given a published layout revision whose single tile omits overlayIdentifier
And   the overlay-designer service answers GET /overlays/ with
      { "chains": [], "published": [] }, as its route table requires
When  the kiosk renders that layout
Then  no overlay query is issued for that tile — the query is skipped
And   the page does not throw, so the ErrorBoundary and its reload backoff
      are not entered
And   exactly one [resilience] line is emitted, subsystem "hub", transition
      "tile-without-overlay-identifier", detail { layout, tiles: 1 }
And   the tile still renders its camera, with no overlay
```

### Site 1 — a mixed layout keeps working (#2197's correction, asserted)

```gherkin
Given a two-tile layout, one tile omitting overlayIdentifier and one bound
      to a real overlay
When  an OverlayHighlightChanged frame arrives for the real overlay
Then  that tile highlights
And   exactly one "tile-without-overlay-identifier" line is emitted, naming
      tiles: 1
```

### Site 1 — quiet (the shape today's server always sends)

```gherkin
Given a layout whose tiles carry "overlayIdentifier": null
When  the kiosk renders it
Then  no "tile-without-overlay-identifier" line is emitted
And   the overlay query for that tile was called with skip: true
      — the guard was evaluated, not merely absent
```

### Site 2 — the defect

```gherkin
Given a published layout whose fab is absent
When  the kiosk renders a tile bound to an overlay whose label is "OEE {{oeeline1}}"
Then  the snapshot query is skipped — no request is made without a fabId
And   exactly one [resilience] line is emitted, subsystem "hub", transition
      "layout-without-fab", detail { layout }
```

### Site 2 — the blind spot, asserted

```gherkin
Given a published layout whose fab is absent
When  a ResolvedOverlayTextChanged frame that itself carries no fab arrives
Then  the frame is still dropped, exactly as today
And   no "resolved-text-without-fab" line is emitted — countReportableSkew is
      unchanged and still silent while wallFab is undefined
And   the one "layout-without-fab" line is the wall's only claim, so the
      silence is no longer total
```

### Site 2 — quiet

```gherkin
Given a published layout whose fab is "munich"
When  the kiosk renders a tile bound to a placeholder label
Then  no "layout-without-fab" line is emitted
And   the snapshot query was called with skip: false and fabId: "munich"
      — the guard ran and let it through
```

### Site 3 — the defect

```gherkin
Given a munich wall with one tile bound to an overlay whose published text is
      "OEE [[oeeline1]]" — the delimiter has moved, the text has not
When  a ResolvedOverlayTextChanged frame for that overlay arrives carrying
      fab "munich" and resolvedText "OEE 99.9"
Then  the snapshot query was skipped, and the label still reads "OEE [[oeeline1]]"
And   exactly one [resilience] line is emitted, subsystem "hub", transition
      "resolved-text-for-static-label", detail { overlay }
When  a second such frame arrives at a higher version
Then  still exactly one line — the latch is per overlay per session
```

### Site 3 — quiet, and it proves the handler ran end to end

```gherkin
Given a munich wall with one tile bound to an overlay whose text is
      "OEE {{oeeline1}}"
When  a ResolvedOverlayTextChanged frame arrives carrying fab "munich" and
      resolvedText "OEE 99.9"
Then  the label reads "OEE 99.9" — the push applied, so the handler reached
      the new guard
And   no "resolved-text-for-static-label" line is emitted
```

### Site 3 — a not-yet is not a fault

```gherkin
Given a tile whose overlay query has not yet resolved
When  a ResolvedOverlayTextChanged frame for that overlay arrives
Then  no "resolved-text-for-static-label" line is emitted
And   the overlay query was issued — the tile rendered and the code ran
```

### Conflict — another plant's frame

```gherkin
Given a munich wall and a static-labelled overlay
When  a ResolvedOverlayTextChanged frame arrives carrying fab "dresden"
Then  no "resolved-text-for-static-label" line is emitted — the report sits
      behind the fab guard, so the filter working is never a fault
```

### Bad request / auth

**N/A, stated rather than omitted.** Nothing in this spec crosses a trust
boundary: no endpoint, no DTO, no token, no scope, no header. Every requirement
either removes a request (FR-003, FR-004) or writes to `console.info` through
`logResilienceEvent` (`resilienceLog.ts:14`). The one authorisation fact this
spec *reads* — that `kiosk-web` and `kiosk-wall` carry `sse.overlays.read` — is
evidence for the site-1 analysis, not something the spec changes.

---

## Independent end-to-end test procedure

Runnable by a person with the repo and a browser. Steps 1–4 need **no Aspire
stack**.

1. `cd D:/Github/smart-sentinel-eye && pnpm install`
2. `pnpm --filter @smart-sentinel-eye/kiosk-web test` — all cases pass, and the
   count is the pre-change count plus the new cases.
3. **Counterfactual for site 1 (proves the site-1 red is driven by the absent
   field).** In `CellPage.test.tsx`, in the site-1 defect case, change the tile's
   `overlayIdentifier` from omitted to `null` — one token. Re-run: that case
   fails at its `toHaveLength(1)` on the `[resilience]` line, because a `null`
   identifier is the healthy shape. Revert.
4. **Counterfactual for FR-008 (proves each quiet case is load-bearing).** Delete
   the `typeof … === 'string'` half of the site-1 sentinel so the guard fires on
   `null` as well. Re-run: **exactly** the site-1 quiet case fails, and it fails
   on its line count, not on a render assertion. Revert, and repeat for site 2 by
   deleting `fab === ''` from the new sentinel and for site 3 by deleting the
   `typeof publishedOverlay.text === 'string'` condition. Three deletions, three
   single failures.
5. **On a real stack (optional — and the only way to reach these branches without
   editing the server, which is the whole reason this is hardening).** Boot the
   AppHost, open the kiosk on a two-tile layout, and use Chrome DevTools **Local
   Overrides** to rewrite the response of `GET /overlay-designer/layouts/<id>`:
   - delete `"overlayIdentifier"` from one tile → expect one
     `[resilience] {subsystem: 'hub', transition: 'tile-without-overlay-identifier'}`,
     the wall still up, and **no** `Recovering…` screen. Before this change the
     same override produces a reload loop and a `crash/render-error` line naming
     a `TypeError` about `find` — worth capturing once, because it is the
     observation that settles site 1 against the running system rather than
     against the route table.
   - delete `"fab"` → expect one `layout-without-fab` line and no snapshot
     request on the Network tab carrying an empty or absent `fabId`.
   - For site 3, override `GET /overlay-designer/overlays/<id>` to rewrite `{{`
     → `[[` in the published revision's `text`, then change the referenced system
     variable's value in management-web → expect one
     `resolved-text-for-static-label` line, and the tile still showing the
     template.

Step 5 is how the change is *observed* rather than merely tested, and it is the
verification note's material at phase 5. Steps 1–4 stand alone.

---

## Locked tech choices (nothing new is introduced)

| Concern | What is used | Where it already exists |
|---|---|---|
| The report channel | `logResilienceEvent('hub', …)` | `resilienceLog.ts:9-14`; `CellPage.tsx:195` and `:231` (#2084), `useLayoutLifecycle.ts:99`, `layoutHub.ts:179` |
| The sentinel | `typeof x === 'string' && x !== ''` | `CellPage.tsx:519` (#2084) |
| The latch | `useRef` at component scope, never `useState` | `CameraViewer.tsx:137-145`, `:336-339` (spec 095) |
| Tile → page reporting | a stable `useCallback` prop writing a page-level ref | `CellPage.tsx:298` — `onLagMeasured`, spec 045 |
| Test framework | vitest + `@testing-library/react` + jsdom | `CellPage.test.tsx` |
| Test helpers | `aMunichWall()`, `pushText()`, `resilienceLines(calls, transition)`, `spyOnConsoleInfo()` | `CellPage.test.tsx:1011-1071` |

**`subsystem: 'hub'`, and the alternative is recorded rather than silently
skipped.** `ResilienceSubsystem` (`resilienceLog.ts:1`) is a closed union of
`'stream' | 'hub' | 'session' | 'crash'`. Sites 1 and 2 are faults in a REST
response, not in a hub frame, so a case can be made for widening the union with
`'layout'`. It is **not taken**, for spec 095's own reason at the same fork:
`CellPage.tsx:195` already files *"a message arrived without a field it
declares"* under `hub`, the harm in all three cases lands on the hub-fed
live-update path, and widening a shared union in a contention file for three
lines is speculative generality. The three transition names all begin with the
thing that is wrong (`tile-without-…`, `layout-without-…`,
`resolved-text-for-…`), so a line self-describes. **This is the one judgement in
the spec a reviewer might reverse, and reversing it costs one union member and
three string literals.**

`[resilience]` transitions are additive: nothing reads the *set* of them, so
three new values change no existing reader. (What actually asserts on the prefix
is vitest — `resilienceLog.test.ts`, `CellPage.test.tsx` and four others.
`resilienceLog.ts:3-8` claims Playwright does; spec 095 found
`grep -rn resilience e2e/` returns nothing, and that docstring is still
uncorrected. Not this spec's file to fix either — recorded so the claim is not
quoted a third time.)

---

## Latency-budget impact (constitution §IV)

**Leg touched: `Event → overlay state` (≤ 200 ms).** `Implemented: yes`, so
subject to §VII per ADR-0117.

**No measurement is required, and no cell of §IV's table moves.**

- **Work removed, on every path that changes.** FR-003 removes an HTTP request
  (the collection URL) and the render throw behind it; FR-004 removes a cross-fab
  snapshot request. Nothing is added to either.
- **What is added to the push path is two lookups on the drop branch.**
  `onResolvedOverlayTextChanged` gains one `Map.get` and, only when it answers
  `true`, one `Set.has`. Both sit after the existing bound-overlay and fab
  guards, so a frame this wall applies pays one map read. No dispatch, no render,
  no timer.
- **The tile's new effect provokes no render and does not run per push.** It is
  keyed on `[overlayIdentifier, hasPlaceholder]` and writes a ref — the
  ref-not-state rule `useWallAlignment.ts` states and spec 095's plan re-states.
- **No *before* figure is cited, deliberately.** Producing a figure from a unit
  test would be the discharge-nobody-earned that §IV itself names. The honest
  claim is smaller: a wall that has stopped applying pushes now says which field
  it could not read, so a future measurement of this leg can be told apart from a
  wall that is not receiving anything.

---

## The ADR question — answered: no ADR, and one is flagged as stopped

**No ADR is needed for what this spec builds.** It is the third application of a
pattern decided twice already: #2084 chose the channel, the bounded cadence and
the *"another plant's frame is not a fault"* distinction; spec 095 chose the
latch, the component-scope ref and the declared-once sentinel; #2189 reused both
in `CameraViewer.tsx`. None of the three needed an ADR, and this spec adds no
dependency, no abstraction, no config knob, no union member and no new file. The
one genuinely open judgement — `'hub'` versus a new `'layout'` subsystem — is a
naming call inside an existing closed union, not an architectural decision, and
it is surfaced above rather than buried.

**Option 1 of #2109 — Zod at the read boundary — would need an ADR, and is
stopped here as it was in 095.** ADR-0079 scopes Zod to *form input*
(`zodResolver`), and there is no `safeParse` or `.parse` on any read path in
`apps/shared/src/api`. Moving validation onto response parsing changes where
validation lives for every context. **ADR-0144 forbids the lane writing that
ADR**, so it is flagged and stopped. Nothing in this spec depends on the answer;
if a human later takes option 1, it supersedes this spec's approach rather than
conflicting with it.

---

## Out of scope

- `apps/shared/src/ui/composites/useWhepSession.ts` — #2157, `agent:blocked`
  behind #1714.
- `apps/shared/src/streaming/WhepClient.ts` — #2198, part 3 of #2109.
- A second `[latency]` line for `kioskLatency.ts`'s null token (item 6 of #2109)
  — **accepted in writing**, and not re-opened here.
- `layouts.schema.ts:19` — the write-path form schema, consumed only by
  `gridDesignerModel.ts`. Not touched, and not treated as authoritative for
  anything on the read path.
- The **omitted or renamed `text`** sub-case of site 3 — visible, not silent (the
  table above).
- Making the tile *recover* from any of the three — no identifier is invented, no
  fab is defaulted, no delimiter is guessed, no query is un-skipped (FR-003,
  FR-006).
- `ResilienceSubsystem` — not widened.
- Any change to constitution §IV's table.

---

## Assumptions, marked

- **A1 — the premise, and it is the issue's own.** No server in this repository's
  history has omitted any of these three fields, and §"What this is" proves
  today's cannot. The work does not depend on A1 becoming true: it costs three
  branches and one effect if it never does. Recorded so nobody later reads this
  spec as evidence that DTO drift has occurred.
- **A2 — the site-1 chain is read, not executed.** Every link is verified against
  source (route table, YARP config, realm, RTK's `joinUrls` behaviour), and step
  5 of the test procedure is how it gets *observed*. Until someone runs step 5,
  "200 with the collection payload" is a strong reading rather than a measurement
  — which is exactly the distinction §IV insists on for legs, and it applies
  here too.
- **A3 — two tiles bound to one overlay agree about its label.** ADR-0112 §2
  allows the binding; both tiles read the same RTK cache entry for the same
  overlay, so both compute the same `hasPlaceholder` and the last writer to the
  page's map writes the same value. The map is therefore not reference-counted
  and entries are never deleted — a stale entry for an overlay no longer on the
  wall is harmless because the push handler's existing `boundOverlays.has` guard
  (`:166`) filters first.

---

## Gate — phase 1

No `[NEEDS CLARIFICATION]` remains. Three things for the reviewer to confirm
before phase 2 advances:

1. **Site 1's answer is settled by reading, not deferred to the red** — 200 with
   the collection payload, then a render-phase `TypeError` and a
   `KioskCrashRecovery` reload loop. The red constructs what the route table
   proves rather than discovering it.
2. **`subsystem: 'hub'` for all three**, rather than widening
   `ResilienceSubsystem` with `'layout'`.
3. **The latch rather than #2084's decade cadence** (FR-007), which makes this
   spec's cadence match 095's and not the helper sitting twenty lines away in the
   same file.
