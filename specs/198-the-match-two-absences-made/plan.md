# Plan — Spec 198, the match two absences made

**Feature:** #2320 · **Spec:** `spec.md` · **Phase-4a colour:** RED

## Context and layers

Frontend only, one bounded context's UI: `apps/kiosk-web`, feature `cell`.
No backend project is touched, no message contract changes, no
`Shared.Contracts` change, so the no-cross-context-reference rule and the
NetArchTest boundary suite are not engaged. Nothing in `apps/shared` moves.

Single file of production change:
`apps/kiosk-web/src/features/cell/CellPage.tsx`.
Single file of test change:
`apps/kiosk-web/src/features/cell/CellPage.test.tsx`.

## The design

### The one-term fix, at both call sites

```typescript
if (namedFab(wallFab) === null || message.fab !== wallFab) return;
```

replacing `if (message.fab !== wallFab) return;` at **line 245** (resolved
text) and **line 296** (highlight).

Why this spelling rather than the issue's suggested
`if (message.fab !== wallFab || wallFab === undefined) return;`:

1. **`namedFab` already exists and already means this.** Spec 141 introduced
   it (`CellPage.tsx:642`) as *the* test for "the wall has a real fab", and it
   is already the test used by the `layout-without-fab` reporter
   (`:159`) and by the snapshot query's `skip` (`:471`). Reuse over a third
   spelling of the same predicate — a rule with three spellings is how this
   defect got here.
2. **It closes the empty-string twin in the same term.** `wallFab === undefined`
   admits `'' === ''`; `namedFab(wallFab) === null` does not. SC-3.
3. **One term is enough — no frame-side test is needed.** Once
   `namedFab(wallFab) !== null`, `wallFab` is a non-empty string, so the
   existing `message.fab !== wallFab` already rejects a frame whose fab is
   `undefined` or `''`. Adding a frame-side `namedFab` call would be a
   redundant condition, and a redundant condition in a security guard is a
   future reader's puzzle.

### What must not move

Three orderings are load-bearing and this change preserves all three:

- **`countReportableSkew` stays *before* the guard**, on both routes. It is
  the #2084 reporter and it must still see the frame it is counting. Moving
  the new term ahead of it would silence the reporter on a named wall.
- **The fab test stays *before* the per-overlay version high-water mark**
  (ADR-0145 §1, `CellPage.tsx:214-231`). `CellPage.test.tsx`'s *"Does not let
  another fab's frame advance the version mark"* is the assertion that fails if
  those two lines are transposed; it stays green.
- **`countReportableSkew`'s own `wallFab === undefined` early return
  (`:610`) is not touched.** This spec changes the filter, not the reporter
  (SC-6). The fab-less wall already reports itself once via
  `layout-without-fab`; a second, per-frame reporter for the same condition
  would be the "permanent skew on a healthy wall" noise `countReportableSkew`'s
  docblock exists to avoid.

### One knock-on comment, and one that gets stronger

- `CellPage.tsx:268-273` justifies passing `message.fab` as the cache key's
  `fabId`, on the grounds that "the guard above has already established it
  equals `wallFab`". Still true, and now stronger: `message.fab` is guaranteed
  a **named** fab, so the key can no longer be `fabId: undefined`. One clause
  added; no code change.
- `CellPage.tsx:62-77` (the `wallFab` docblock) asserts that during the loading
  window `wallFab` "equals no frame's fab and every frame is dropped". That
  claim was false and is what this spec fixes; the comment is corrected to say
  what now makes it true. The neighbouring argument that `''` must not become a
  default stands and is kept.
- `CellPage.tsx:147-148` points at #2320 as an open blind spot. It is corrected
  to record that the filter half closed here and the reporter half stayed
  deliberately as it was.

### Option 2, considered properly

"Resolve `wallFab` to never be `undefined`" can only mean one of three things,
and each is worse than the one-term fix:

- **A sentinel value.** Rejected in the code already, with reasons
  (`CellPage.tsx:68-77`): unmatchability has to hold on *both* the frame
  filter and the snapshot query string, and `''` holds on only one — on the
  query string the server reads it as "every fab I hold", which is #2069 itself.
  Any sentinel has to clear that same bar, and `undefined` already does.
- **Block message processing until the layout lands.** Does not help. The
  exposed window is not the loading window — during loading `tiles` is empty,
  so both handlers already return at their `boundOverlays` check. The exposed
  window is a **loaded** layout that carries no fab, which is precisely when
  message processing is supposed to be running.
- **Refuse to render a fab-less layout at all.** This would blank a wall that
  is otherwise showing live pictures, on a server-drift condition, in a 24/7
  fab. That is a product decision — ADR-0145 decided what to do with *frames*,
  not whether a fab-less layout is renderable — and it would need an ADR. The
  autonomous lane may not write one (ADR-0144).

Option 1, in the `namedFab` spelling above, is therefore the design.

## Where the state lives

Unchanged. `wallFab` stays `data?.fab`, `string | undefined`, derived per
render; the two counters, the version map, the highlight expiry map and the
spec-141 latch set all keep their current shapes and lifetimes.

## Risks

- **R1 — the red passes for the wrong reason (resolved-text route).** On a
  fab-less wall the tile's snapshot query is *skipped* (spec 141), so the
  rendered label does **not** move even today. A red test asserting the label
  would be green-for-the-wrong-reason and would read as "already fixed".
  Mitigation: SC-1 asserts the **cache entry and the version mark**, not the
  label — see `tasks.md` T002 for the exact observable.
- **R2 — the highlight route has no second net.** It has no version guard and
  no query skip, so on the highlight route the defect is fully visible in the
  DOM (`data-highlighted`). That makes SC-2 the strongest red and it is written
  first.
- **R3 — an existing test documents the hole.** `CellPage.test.tsx:1398`
  (*"Leaves countReportableSkew's blind spot untouched…"*) asserts only the two
  reporters, so it stays green through this change. Its **docblock**, which
  describes the hole as open, must be corrected — prose only, no assertion
  edited. Editing any assertion in that test would be the phase-4 smell
  ADR-0144 blocks on.
- **R4 — #2321 collides.** See `spec.md` §*File contention*. Sequence #2320
  first.

## Verification

Phase 4a runs `npm --workspace apps/kiosk-web run test -- CellPage` and
returns the **verbatim** failing output for SC-1/SC-2. Phase 4b may not edit
those assertions. Phase 5 runs the same suite green, plus the browser
observation in `spec.md` §*Independent end-to-end test procedure* item 2, and
records explicitly that the fab-less-frame half is not reproducible against
today's server.

Gates to clear before the PR: `npm run lint`, `npm run format:check`,
`npm run typecheck` (workspace root), and the kiosk-web Vitest suite.

## Roles

| Phase | Agent | Why |
|---|---|---|
| 4a | `test-writer` | RED. Two component tests plus the three controls. |
| 4b | `frontend-engineer` | Two-line change in `CellPage.tsx` plus comment corrections. |
| 6 | `frontend-reviewer` | kiosk-web TypeScript/React, RTK cache keys, test hygiene. |
| 6 | `security-reviewer` **(also)** | See below. |

**Security-reviewer: yes.** This is a cross-fab isolation guard — the same
class as #2069, which ADR-0145 was written for. The defect admits another
plant's data onto a wall, the fix is a trust-boundary predicate, and a
one-term boolean guard is exactly the kind of change whose *negative space*
(the case it still lets through) a functional review is not looking for.
`security-reviewer` holds this repo's fab-authorization model and is asked one
specific question: **after the fix, is there any remaining pair of
(`wallFab`, `message.fab`) values that passes both guards without naming the
same real fab?**
