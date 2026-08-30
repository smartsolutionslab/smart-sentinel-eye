# Implementation Plan — 048 the camera picker finds every camera

**Branch**: `048-picker-finds-every-camera` | **Spec**: [spec.md](./spec.md)
**Research**: [research.md](./research.md)

---

## Summary

Two stories ship: the picker **stops being silent** about an incomplete list
(US1), and it **reaches every camera** in a fab at the 250-camera target (US2).
Search by name (US3) is deferred to its own spec, for reasons recorded below and
in research R6.

The whole change is in two frontend packages. **No C# is touched** — see R7.

---

## Technical Context

| | |
|---|---|
| **Language** | TypeScript, React 19 |
| **Packages** | `apps/shared` (API client), `apps/management-web` (the dialog) |
| **State** | Redux Toolkit + RTK Query 2.12 |
| **Forms** | React Hook Form — the picker is a `register()`-bound native `<select>` |
| **Tests** | Vitest + Testing Library; Playwright for e2e |
| **Backend** | **Unchanged.** Nothing needed; see R7 |
| **Unknowns** | None. Every open question from Phase 0 was closed by reading source |

---

## Constitution Check

| Principle | Assessment |
|---|---|
| **§IV latency budget** | **Not on the path.** Building a wall is configuration, not monitoring; no leg is affected and no latency is claimed. The spec says so explicitly under *Explicitly not claimed*. |
| **§VII observability** | No new leg, so no dashboard obligation attaches (ADR-0117). |
| **Scale — 250 cameras/fab** | **This is the principle the feature serves.** The picker currently fails at 20 % of the stated target. |
| **DDD / value objects** | No domain code. No primitives cross a context boundary because no context boundary is crossed. |
| **No cross-context references** | None introduced. The change is inside the frontend, and the API client stays in `apps/shared` where both apps consume it. |
| **Smallest possible change** | US3 is deferred precisely to hold to this. The two deferred items are filed as issues rather than absorbed. |
| **No speculative generality** | The paging helper is written for this endpoint, not as a generic "page any list" abstraction. The urge to generalise is the deferred audit issue, not this change. |

**Gate: PASS.** No violation, no exemption needed.

---

## Design

### The one idea worth holding on to

**The picker is complete up to a stated bound, and honest at any scale.**

US2 makes it complete to 1000 cameras. US1 makes it honest past that — and past
*any* other cause of incompleteness, including the concurrent-edit race in R4
that no amount of paging can eliminate. This is why US1 is P1 and built first:
it is not polish on US2, it is what makes US2's residual limit safe to have.

The defect being removed is a **hidden** cliff at 50. What replaces it is a
**stated** one at 1000. That is the whole of the improvement, and the plan
should not pretend otherwise.

### What changes

**`apps/shared/src/api/cameras.api.ts`** gains one endpoint that pages
internally via `queryFn` and returns a single result:

- `sort=name`, `order=asc` (R2) — alphabetical, which is both what an operator
  expects and what makes native type-ahead usable.
- 200 per page (the maximum the source allows), at most 5 pages (R3).
- Concatenate, de-duplicate by `cameraIdentifier` (R4).
- Return the items **and** the source's `count`, so the consumer can tell
  complete from truncated without arithmetic of its own.

**`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`** consumes
it instead of `useListCamerasQuery({ limit: 50 })`, and renders the notice when
the returned items are fewer than the count.

**`GridDesigner.tsx`** points each camera `<select>` at the notice with
`aria-describedby` (R5), and distinguishes the three empty states (FR-003).

### What deliberately does not change

- **The `<select>` stays a native select.** Replacing it with a combobox is
  US3's problem and needs a primitive the design system does not have.
- **`cameras.map(...)` keeps its shape.** FR-011 — a selection surviving a
  refresh — is protected most cheaply by not changing what the field renders.
- **The overlay picker is untouched** (FR-012). It is not paginated; changing it
  would be a change without evidence.
- **`FormField` gains nothing.** Widening a shared composite for one caller is a
  change to everything to serve one screen (R5).

---

## "Done" — stated before any code is written

Per the Karpathy rules, each story gets a verifiable criterion up front. Not
"it compiles"; not "the tests pass".

| Story | Done when |
|---|---|
| **US1** | With a fixture whose count exceeds its returned items, the dialog states both numbers, and each camera select is associated with that statement by `aria-describedby`. With a complete list, **no notice is rendered** — and a test proves the absence, because a notice that is always there says nothing. |
| **US2** | With a fixture of 250 cameras across two pages, the picker offers all 250, including the alphabetically last, and issues exactly two requests. A mutation that stops after the first page must fail a test. |
| **Bound** | With a fixture of 1200 cameras, the picker offers 1000, issues exactly 5 requests, and shows the notice saying 1000 of 1200. The bound must be observable, not merely believed. |

---

## Phases

1. **Client paging** — the `queryFn` endpoint in `apps/shared`, with unit tests
   over the paging arithmetic: page count, de-duplication, the bound, and the
   count passthrough. Pure enough to test without React.
2. **The notice (US1)** — the dialog renders it; `GridDesigner` associates it.
   Tested through the dialog, including the negative case.
3. **Reach (US2)** — the dialog consumes the paging endpoint. Tested with a
   two-page fixture asserting the alphabetically last camera is selectable.
4. **Verify** — the full CI suites, then a person opening the dialog against a
   fab with more cameras than one page.

Phases 2 and 3 are separable and either can ship alone; US1 first if only one
does.

---

## Risks

**1. The paging loop is the kind of code that looks right and is off by one.**
Offsets, a bound, a de-duplication step and a truncation flag interact. Mitigated
by keeping the arithmetic in `apps/shared` and testing it directly rather than
only through the dialog — a component test that renders 250 options proves the
total but not which camera was dropped at a boundary.

**2. A green suite will not prove the thing that matters.** Every test runs
against a fixture. None runs against a real fab of 250. Recorded in R8 and
repeated here because spec 046 shipped a defect that all sixteen of its
mutations missed — the tests covered the transitions they had thought of and
could not cover a starting state they never constructed. The blind spot here is
**scale**: correctness of the arithmetic is testable, usability of a 250-option
select is not.

**3. The notice becomes decoration.** If it renders whenever the list loads, or
its text is vague ("some cameras may not be shown"), it stops carrying
information and operators learn to ignore it. It must state both numbers and
must be absent when the list is complete — which is why the negative test is
part of "done" rather than an extra.

---

## Deferred, and filed rather than implied

Both were named in the spec's Scope section and are filed as issues in this
phase — writing them is part of the plan, not a follow-up.

- **US3, search by name.** Needs a filter the camera source does not have *and*
  a combobox the design system does not have — Radix ships none. A spec, not a
  task. Deferred rather than dropped because native prefix type-ahead gives an
  operator a real if imperfect route to a named camera (R6), and R2's
  alphabetical sort makes that route materially better.
- **Other consumers that render a page as though it were the whole list.** The
  audit instinct is right and belongs in its own issue: a spec that fixes one
  picker and a spec that reviews every list are different sizes of work, and the
  second would swallow the first.
- **The 200 cap sitting below the 250-camera target**, with no reasoning anyone
  recorded. Whether that is a defect, a deliberate bound needing a written
  reason, or correct as-is is a question this feature surfaces and does not
  settle. It is not blocking: paging reaches 250 regardless.
