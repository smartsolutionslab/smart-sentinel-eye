# Spec 198 — The match two absences made

**Issue:** #2320 — *A fab-less wall accepts a fab-less push — two absent fabs compare equal*
**Branch:** `2320-reject-fabless-match`
**Phase-4a colour:** **RED** (behaviour-changing)
**ADRs:** ADR-0145 (a kiosk's fab is derived from its wall), ADR-0144 (autonomous lane), ADR-0037 (phases), ADR-0109 (parallel markers)
**Related issues:** #2069 (cross-fab leakage this reaches by another door), #2084 (the sibling reporter), #2197 / spec 141 (where this was found), #2321 (same file, not yet in flight — see *File contention*)

## Problem

`apps/kiosk-web/src/features/cell/CellPage.tsx` filters every pushed frame
against the wall's own fab, twice — once on the resolved-text route
(**line 245**, was `:197` before spec 141 landed) and once on the highlight
route (**line 296**, was `:233`):

```typescript
if (message.fab !== wallFab) return;
```

`wallFab` is `data?.fab` (line 78) and is `string | undefined`. When **both**
sides are absent, `undefined !== undefined` is `false` and the guard does not
return: **the frame is applied.**

That inverts ADR-0145's own stated consequence:

> The client filter fails **closed**: a frame carrying no fab does not match a
> wall's fab and is dropped.
> — `docs/adr/0145-a-kiosks-fab-is-derived-from-its-wall.md:69-71`

It fails closed in the case #2084 was built for (a fab-less frame at a wall
that *has* a fab). It fails **open** in the mirror case, which is the one that
matters: once a wall is running against a fab-less layout, every push from
**any** fab whose own `fab` field is missing is accepted as this wall's own —
the cross-fab "whichever sorts first" outcome #2069 closed, reached through a
different door.

The same arithmetic holds for the empty-string twin: a wall on `fab: ''` and a
frame on `fab: ''` also compare equal. `CellPage.tsx:69-72` argues `''` "never
matches a `!==`" — true only against a *named* wall fab, and that is the same
shape of unchecked premise that produced this defect.

## What spec 141 (#2197) did and did not do — the option-2 investigation

The issue offers a second fix: *"resolve `wallFab` to never be `undefined` in
the first place … if a wall's own fab-less state is already reported elsewhere
per #2197's site 2."* Checked against the merged code:

- Spec 141 site 2 added **a report and a query skip, nothing more**. On a
  fab-less layout it logs `layout-without-fab` once per mounted page
  (`CellPage.tsx:159-162`) and skips the snapshot query
  (`CellPage.tsx:471`, via `namedFab`).
- It **does not** resolve `wallFab`, **does not** block hub message
  processing, **does not** unmount or error the page. The layout renders, the
  hub subscribes, and both guards run with `wallFab === undefined`.
- Spec 141 says so in the code itself: *"`countReportableSkew` is not touched
  (plan invariant 3; its own blind spot is tracked separately as #2320)"*
  (`CellPage.tsx:147-148`), and its own test
  *"Leaves `countReportableSkew`'s blind spot untouched"*
  (`CellPage.test.tsx:1398`) documents the hole as deliberately left open.

**Conclusion: `wallFab === undefined` is fully reachable at both call sites
after #2197. Option 2 as "no change needed here" is false.** Option 2 as "make
the kiosk refuse to run on a fab-less layout" is rejected in `plan.md`
(§*Option 2, considered properly*) — it would blank a working wall on a
server-drift condition, and it is a product decision ADR-0145 does not make.

## Locked tech choices

React + TypeScript + Vite kiosk app (ADR-0074); Redux Toolkit + RTK Query
(ADR-0075); SignalR push (ADR-0152); Vitest + Testing Library component tests
against the real store, as the two sibling specs (067, 141) already use. No new
dependency, no new abstraction.

## User story

### US1 (P1) — A wall that does not know its own fab applies nothing

*As* the operator of a wall in Munich,
*I want* a wall whose layout carries no fab to refuse every pushed frame,
*so that* a neighbouring plant's figure can never appear on it.

This is the whole slice. It is one file, two lines, and it is independently
shippable and observable on its own.

## Acceptance scenarios

**SC-1 (the defect, resolved-text route) — two absences are not a match**

```gherkin
Given a wall showing a published layout that carries no fab
  And a tile on it bound to overlay "ovl-blind"
When a ResolvedOverlayTextChangedV1 arrives for "ovl-blind" carrying no fab
Then no overlay-snapshot cache entry is written for that overlay
  And the per-overlay version high-water mark is not advanced
```

**SC-2 (the defect, highlight route) — the tile does not light**

```gherkin
Given a wall showing a published layout that carries no fab
  And a tile on it bound to overlay "ovl-blind"
When an OverlayHighlightChangedV1 arrives for "ovl-blind" carrying no fab
Then no tile is highlighted
```

**SC-3 (the empty-string twin)**

```gherkin
Given a wall showing a published layout whose fab is the empty string
When a frame arrives for a bound overlay whose own fab is the empty string
Then the frame is refused on both routes, exactly as in SC-1 and SC-2
```

**SC-4 (conflict — the case that already worked, and must keep working)**

```gherkin
Given a Munich wall showing a published layout with fab "munich"
When a resolved-text frame for a bound overlay arrives carrying fab "dresden"
Then the wall's label does not move
  And the per-overlay version high-water mark is not advanced
```

**SC-5 (control — the wall's own frames still land)**

```gherkin
Given a Munich wall showing a published layout with fab "munich"
When a resolved-text frame for a bound overlay arrives carrying fab "munich"
Then the label shows the pushed text
  And a "munich" highlight frame still lights the bound tile
```

**SC-6 (the reporters are untouched)**

```gherkin
Given a wall showing a published layout that carries no fab
When a frame carrying no fab arrives for a bound overlay
Then exactly one "layout-without-fab" line is logged for the page
  And no "resolved-text-without-fab" or "highlight-without-fab" line is logged
```

`countReportableSkew`'s `wallFab === undefined` early return
(`CellPage.tsx:610`) stays exactly as it is: nothing dropped before the layout
lands is evidence of a server skew, and the wall already says once that it has
no fab. This spec changes the **filter**, not the **reporter**.

**SC-7 (auth / trust boundary)**

There is no HTTP request and no scope check on this path, so the usual
bad-request and 401/403 scenarios do not apply. The security-relevant boundary
here is **fab isolation**, and SC-1, SC-3 and SC-4 are its scenarios: a frame
is admitted only when the wall names a fab *and* the frame names the same one.

## Independent end-to-end test procedure

The new behaviour needs **two simultaneous server-drift conditions** —
a layout row with no (or blank) `Fab`, and a hub frame predating the `fab`
field. Today's server produces neither, which is why #2084's own coverage is
component-level. The honest split:

1. **Primary (reproducible, mechanised).** Run `apps/kiosk-web` Vitest against
   the real Redux store:
   `npm --workspace apps/kiosk-web run test -- CellPage`. SC-1 and SC-2 are
   observed **failing** on today's tree (phase 4a red, output quoted in the
   PR), then passing after 4b, with SC-4/SC-5/SC-6 green throughout.
2. **Secondary (reproducible in a browser, covers the half that is real).**
   Boot the Aspire stack, blank a layout's fab in Postgres
   (`UPDATE layouts SET "Fab" = '' WHERE …`), open that layout in kiosk-web,
   and confirm in the console that the wall logs `layout-without-fab` once and
   issues **no** `/system-variables/snapshot` request — i.e. the fab-less wall
   state this spec guards is a state a real kiosk genuinely reaches.

The fab-less *frame* half cannot be produced against the current server and is
not claimed as observed end to end. Phase 5 records that distinction rather
than papering over it.

## Latency budget

Leg: **Event → overlay state (≤ 200 ms)** — both guards sit on it.
Impact: **none measurable.** The change adds one `typeof` plus a string
comparison per frame and strictly *removes* work on the path it changes (a
refused frame does no cache write and no re-render). No re-measurement is
required, and none is claimed.

## Out of scope

- Extracting or restructuring the hub handlers — that is **#2321**, and it
  touches this same file. See *File contention*.
- Changing `countReportableSkew`, the `layout-without-fab` reporter, or when
  either fires (SC-6 pins them unchanged).
- Widening `Layout.fab` or `ResolvedOverlayTextChangedMessage.fab` away from
  `string`. Spec 141 FR-001 settled that: the declared types describe the
  server that is supposed to exist; the guards admit the drift.
- Server-side filtering. ADR-0145 rejected it with reasons.
- Making the kiosk refuse to render a fab-less layout (option 2 proper) — see
  `plan.md`.

## File contention

Nothing is in flight against `CellPage.tsx`: `gh pr list --state open` returns
**no open PRs** at time of writing. **#2321** ("CellPage.tsx is 675 lines and
growing — extract the hub message handlers") is open, `agent:ready`, and
rewrites both guards' surroundings. **Sequencing matters: land #2320 first.**
It is two lines; #2321 is a move of the code around them, and doing it in the
other order means the extraction carries the defect into its new home and this
spec's red has to be rewritten against a file that no longer exists in that
shape.
