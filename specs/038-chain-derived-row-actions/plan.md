# Implementation Plan: A row offers the actions its chain actually supports

**Branch**: `038-chain-derived-row-actions` | **Date**: 2026-08-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/038-chain-derived-row-actions/spec.md`

---

## Summary

Both rows decide every action from the chain's **newest** revision. Replace that
with a descriptor computed from the whole chain — `{ live, draft, newest,
summarised }` — and read every action and every piece of row text off it.

No service change. No new dependency. No migration. No ADR.

### What Phase 0 changed about this plan

**The spec's five chain shapes are not all of them — there are eight.**
`Publish` archives only the prior *Published* revision, so drafts accumulate.
Enumerating by construction rather than inspection surfaced `{A, D}`, `{D, D}`
and `{P, D, D}`, none of which the spec considered. `{D, D}` — two open drafts,
nothing published — is **two clicks from a published chain**, both offered by the
row: branch, then revert.

That is the same methodological failure this feature fixes, caught one level up.
The design must not assume at most one draft, nor assume a Published revision
exists merely because there is history.

Two smaller corrections: the confirmation's `published` flag becomes a constant
and is removed (research §6), and extraction between the two pages is governed by
the spec-035/036 precedent rather than by ADR-0104, which does not reach the
frontend (research §3).

---

## Technical Context

**Language/Version**: TypeScript 5.7 / React 19

**Primary Dependencies**: RTK Query, Radix, Tailwind. **No new dependency.**

**Storage**: none touched. No migration; no service change of any kind (FR-013).

**Testing**: Vitest + Testing Library; Playwright for e2e.

**Target Platform**: `management-web` only. `kiosk-web` lists no chains.

**Project Type**: Frontend-only change inside one app.

**Performance Goals**: not on the §IV latency path. The descriptor is one pass
over a chain's revisions per row, replacing between one and four passes today.

**Constraints**: two e2e assertions read a row's state text (research §5) and must
keep matching.

**Scale/Scope**: 2 page components, 1 new shared helper, 1 new prop on an existing
confirmation, 8 chain shapes to cover, 2 tests rewritten, 0 service changes.

---

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 — see below.*

| Principle | Verdict | Note |
|---|---|---|
| **II. DDD with value objects** | **Not engaged** | No domain code. The descriptor is a view-model over data the app already receives. |
| **III. Bounded context isolation** | **Pass** | Nothing crosses a context boundary. The shared helper lives inside `management-web`, which already consumes both contexts' APIs through the gateway. ADR-0104 governs the backend twins and does not reach here (research §3). |
| **IV. Latency budget** | **Not on the path** | No kiosk-facing behaviour, no event, no service call added or removed. |
| **V. Spec-driven** | **Pass** | Spec → plan → tasks → implementation. |
| **VII. Observability** | **Not engaged** | No new signal. Nothing here fails silently: every action still surfaces its own mutation error through the existing banner. |
| **VIII. Safe at trust boundaries** | **Pass** | No new trust boundary. The service validates every action independently of what the row offers — which is exactly why this defect was cosmetic rather than dangerous, and why the fix cannot introduce one. |
| **IX. No speculative generality** | **Pass, with one judgement call** | The descriptor is extracted for two callers. Justified as behaviour-sharing under spec 036's precedent rather than shape-sharing under spec 035's (research §3), and because FR-014 asks for identical twins — extraction makes that structural. The `verb` prop is added because a second verb exists **now**, not in case a third appears. |

**Post-Phase-1 re-check**: unchanged. The design adds one function, one optional
prop and one confirmation instance per page.

---

## Project Structure

### Documentation (this feature)

```text
specs/038-chain-derived-row-actions/
├── spec.md
├── plan.md                      # this file
├── research.md                  # Phase 0 — nine findings, starting with the shape space
├── contracts/
│   ├── row-actions.md               # every shape × every action, and what each targets
│   └── row-confirmations.md         # the two confirmations, verbatim
├── quickstart.md
└── checklists/requirements.md
```

**No `data-model.md`.** The feature adds no entity, field or state. The descriptor
is a derived view, and it is specified in `contracts/row-actions.md` where it is
used rather than as data.

### Source code

```text
apps/management-web/src/features/
  chainView.ts                    # NEW — the descriptor, generic over {revisionNumber, state}
  chainView.test.ts               # NEW — the eight shapes, tested once for both pages
  ArchiveConfirmation.tsx         # + optional `verb`, defaulting to 'Archive'
  layouts/LayoutsPage.tsx         # actions, targets, badge, tile summary, discard confirmation
  layouts/LayoutsPage.test.tsx    # 1 test rewritten, shape coverage added
  overlays/OverlaysPage.tsx       # the same, minus the designer step
  overlays/OverlaysPage.test.tsx  # 1 test rewritten, shape coverage added
```

---

## Phase 1 — Design

### The descriptor

```ts
export interface ChainView<TRevision> {
  live: TRevision | undefined;      // the Published revision; at most one
  draft: TRevision | undefined;     // the NEWEST Draft; a chain may hold several
  newest: TRevision;                // highest-numbered; the branch source when stranded
  summarised: TRevision;            // live ?? draft ?? newest — what the row DESCRIBES
  fullyArchived: boolean;           // !live && !draft
}
```

`summarised` is deliberately separate from the action targets. *What the row says
about a chain* and *what each button does to it* are different questions, and
collapsing them is a smaller version of the mistake being fixed.

`draft` is the **newest** draft, not the only one — shapes `{D, D}` and
`{P, D, D}` are reachable (research §1).

### Actions, per shape

Specified exhaustively in [contracts/row-actions.md](./contracts/row-actions.md).
Every one of the eight shapes offers at least one action; the table is the
evidence for FR-001 and SC-001, and it is written per shape rather than in
aggregate because the defect being fixed is a shape nobody enumerated.

### The two confirmations

Verbatim in [contracts/row-confirmations.md](./contracts/row-confirmations.md).

**Archive** targets the live revision, so it now *always* warns about kiosks —
the `published` flag becomes a constant and is deleted (research §6). **Discard
draft** targets the draft and says nothing about kiosks or about the layout going
out of service, because neither happens.

`ArchiveConfirmation` gains `verb?: string` defaulting to `'Archive'`, used for
both the title and the confirm label. Both actions archive a revision
server-side; the verb names it in the operator's terms.

### Testing strategy

**`chainView.test.ts`** carries the eight shapes once. That is the point of
extracting it: the shape table is tested in one place rather than twice with a
chance of divergence.

**Each page** then tests what it does with the descriptor — which button appears,
which revision number each mutation receives, what each confirmation says. Targets
are asserted **by revision number**, per SC-003, because acting on the wrong
revision succeeds exactly as readily as acting on the right one. That is why this
shipped.

**Two existing tests are rewritten** (research §7), keeping their substantive
claim in a stronger form.

---

## Risks

**1. The descriptor assumes one draft.** `{D, D}` is two clicks from a published
chain. Mitigated by `chainView.test.ts` covering it explicitly and by `draft`
being documented as *the newest* rather than *the*.

**2. Archive and Discard get wired to the wrong revision.** Both call the same
mutation; both succeed either way. Mitigated by asserting the revision number on
both, **on the same chain**, so a swap fails rather than passing twice.

**3. The discard confirmation inherits the archive wording.** It is the same
component with different children, and copying the block is the fast way to build
it. The copied sentence claims the layout goes out of service, which is the exact
falsehood this feature removes. Mitigated by the contract giving both bodies
verbatim, and by asserting the **absence** of that sentence in the discard case.

**4. The badge change breaks an e2e assertion.** Two read it. Both are on shape
`{P}`, whose badge is unchanged — checked, not assumed (research §5).
