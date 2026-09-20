# Tasks — Spec 198, the match two absences made

**Issue:** #2320 · **Spec:** `spec.md` · **Plan:** `plan.md`
**Phase-4a colour:** **RED** — a test that arrives green is a phase-4 failure.

Both production call sites live in one file, so **there is no `[P]` work
here**: every task below touches `CellPage.tsx` or `CellPage.test.tsx` and they
run strictly in order. The parallelism in this slice is between *this issue*
and any other issue that does not touch `apps/kiosk-web/src/features/cell/` —
and **#2321 is not such an issue** (`spec.md` §*File contention*).

## US1 — A wall that does not know its own fab applies nothing (P1)

### Phase 4a — RED (`test-writer`)

**T001 [US1]** — Red, highlight route (SC-2). In
`CellPage.test.tsx`, add a describe for #2320 and a case that renders a wall on
`layoutWithFab(undefined, [tile({ …, overlayIdentifier: 'ovl-2320-highlight' })])`,
fires `capturedCallbacks.onOverlayHighlightChanged(highlightFrameWithoutFab('ovl-2320-highlight', 1000))`,
and asserts **no tile carries `data-highlighted === 'true'`**. Today this is
red: the tile lights. Reuse `layoutWithFab` (`:311`) and
`highlightFrameWithoutFab` (`:1111`) — do not write new fixtures for either.
*Depends on:* nothing.

**T002 [US1]** — Red, resolved-text route (SC-1). Same fab-less wall, bound to
its own overlay identifier (never a shared one —
`CellPage.test.tsx:850-859` explains why). Fire a fab-less
`ResolvedOverlayTextChangedMessage` via `pushText` and assert **the cache
entry**, not the label (plan R1): that
`systemVariablesApi.endpoints.getOverlaySnapshot.select({ overlayIdentifier, fabId: undefined as unknown as string })(store.getState()).data`
is `undefined`. Today it is defined and carries the foreign text — that is the
red. Add a second assertion that a **later, higher-versioned** fab-less frame
is also refused, which is what shows the version high-water mark was never
advanced by the first.
*Depends on:* T001 (same describe block).

**T003 [US1]** — Red, empty-string twin (SC-3). `layoutWithFab('', …)` plus a
frame carrying `fab: ''`, on both routes. Today both apply.
*Depends on:* T002.

**T004 [US1]** — Controls, green today and green after (SC-4, SC-5, SC-6).
Do **not** write new copies: name the existing cases that already cover them —
*"Ignores a resolved-text frame belonging to another plant's fab"* (`:919`),
*"Does not let another fab's frame advance the version mark"* (`:942`),
*"Still applies a frame carrying the wall's own fab"* (`:1006`), highlight
scenario 1 (`:519`), and *"Leaves countReportableSkew's blind spot
untouched…"* (`:1398`). Add only what is genuinely uncovered: **one** case
asserting that on the fab-less wall of T002 the `layout-without-fab` line is
still logged exactly once and **no** `resolved-text-without-fab` /
`highlight-without-fab` line appears (SC-6 stated at the new describe rather
than inferred from a neighbouring block).
*Depends on:* T003.

**T005 [US1]** — Run `npm --workspace apps/kiosk-web run test -- CellPage`,
confirm T001–T003 **fail** and every control **passes**, and return the
**verbatim** failure output. That output is the phase-4 gate artefact and is
quoted in the PR body (ADR-0139, ADR-0144). Do not touch `CellPage.tsx`.
*Depends on:* T004. **Blocks everything below.**

### Phase 4b — implement (`frontend-engineer`)

**T006 [US1]** — In `CellPage.tsx`, replace
`if (message.fab !== wallFab) return;` with
`if (namedFab(wallFab) === null || message.fab !== wallFab) return;` at the
resolved-text guard (**line 245**) and the highlight guard (**line 296**).
Nothing else moves: `countReportableSkew` stays ahead of both guards, and the
fab test stays ahead of the version mark (plan §*What must not move*).
*Depends on:* T005. **May not edit any assertion written in T001–T004.**

**T007 [US1]** — Comment corrections in `CellPage.tsx`, prose only:
(a) the `wallFab` docblock (`:62-77`) — its claim that during loading "every
frame is dropped" was the false premise this spec fixes; say what now makes it
true and keep the standing argument against a `''` default;
(b) the spec-141 note (`:147-148`) — record that #2320 closed the **filter**
half and left `countReportableSkew` deliberately unchanged;
(c) the cache-key note (`:268-273`) — `message.fab` is now guaranteed a
*named* fab, so `fabId` can no longer be `undefined`.
*Depends on:* T006.

**T008 [US1]** — Correct the docblock of *"Leaves countReportableSkew's blind
spot untouched…"* (`CellPage.test.tsx:1388-1397`), which describes the hole as
open. **Prose only — no assertion in that test may change** (plan R3). If any
assertion there needs editing to pass, stop and report: that is evidence the
change moved more behaviour than intended.
*Depends on:* T007.

**T009 [US1]** — Green: `npm --workspace apps/kiosk-web run test -- CellPage`,
then the gates — `npm run lint`, `npm run format:check`, `npm run typecheck`.
*Depends on:* T008.

### Phase 5 — verify (`/verify`)

**T010 [US1]** — Record the suite red→green with the T005 output quoted, plus
the browser observation from `spec.md` item 2 (blanked layout fab →
`layout-without-fab` logged once, no snapshot request). State explicitly that
the fab-less **frame** half is not reproducible against today's server and is
therefore not claimed as observed end to end. Latency: name the
*Event → overlay state* leg, no measurable impact, no re-measurement claimed.
*Depends on:* T009.

### Phase 6 — review

**T011 [P] [US1]** — `frontend-reviewer` on the diff.
**T012 [P] [US1]** — `security-reviewer` on the diff, asked the specific
question in `plan.md` §*Roles*: after the fix, is there any remaining
(`wallFab`, `message.fab`) pair that passes both guards without naming the same
real fab?
*Both depend on:* T010. These two are genuinely parallel — different reviewers,
read-only, disjoint outputs (ADR-0109).

### Phase 7 — PR

**T013 [US1]** — PR to `develop` (`--base develop`), quoting the T005 failure
output, referencing #2320 with a closing keyword and naming spec 198. Add a
line flagging the #2321 sequencing for whoever picks it up next.
*Depends on:* T011, T012.

## Dependency summary

```
T001 → T002 → T003 → T004 → T005 ──┐
                                   ├→ T006 → T007 → T008 → T009 → T010 → {T011 [P], T012 [P]} → T013
```

## Board

#2320 is on Project #13 (status **Todo**), verified with
`gh project item-list 13 --owner smartsolutionslab --limit 2000`. Feature-level
issue only — no per-task issues (CLAUDE.md §Workflow, since spec 028).
