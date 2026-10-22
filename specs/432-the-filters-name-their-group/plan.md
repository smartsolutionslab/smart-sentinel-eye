# Plan 432: The filters name their group

**Spec**: [spec.md](./spec.md) · **Issue**: #3083 · **ADRs**: ADR-0077, ADR-0074, ADR-0139, ADR-0144

## 1. Context and layers

No bounded context, no backend, no `Shared.Contracts`, no AppHost resource, no domain model. Frontend
only (`apps/management-web`), plus one test-only C# file in `tests/Architecture.Tests`. No entities,
value objects, messages or migrations. Constitution §II/§IV untouched (§IV leg: N/A).

## 2. Change

Three one-line attribute additions, copying RulesPage verbatim:

```tsx
<div className="mb-4 flex gap-2" role="group" aria-label="Filter by state">
```

in `LayoutsPage.tsx:135`, `OverlaysPage.tsx:123`, `SystemVariablesPage.tsx:126`. Class names unchanged.
No shared constant for the label: four literal occurrences of a two-word string do not earn an export, and
the guard (§4) pins the shape, not the text.

## 3. Tests (phase 4a colour: red)

Behaviour-changing (the accessibility tree gains a named group), so red. Each page suite gets one test,
sentence-style (ADR-0053):

```ts
it('Groups the state filters under the name Filter by state', () => {
  // render as the suite already does
  const group = screen.getByRole('group', { name: 'Filter by state' });
  expect(within(group).getAllByRole('button').map((b) => b.textContent)).toEqual([...chip labels]);
});
```

- R1 `LayoutsPage.test.tsx` — chips `All, Draft, Published, Archived`. Red: `getByRole` throws
  "Unable to find an accessible element with the role "group" and name "Filter by state"".
- R2 `OverlaysPage.test.tsx` — same chips. Red, same message.
- R3 `SystemVariablesPage.test.tsx` — chips `All, Defined, Archived`. Red, same message.
- P1 `RulesPage.test.tsx` — characterisation pin, **declared green in advance** (Rules already has the
  group; nothing pinned it). Use Rules' own chip labels as read from `STATE_FILTERS` at implementation time.

Asserting the chip list *within* the group (not just the group's existence) is what makes the test fail if
the attribute lands on the wrong element (e.g. the page root).

## 4. Guard (US2)

`TextActionAndChipPrimitiveTests.cs`, new `[Fact] A_ToggleChip_sits_inside_a_labelled_group`: for every
`ScannedSourceFiles` file except `ToggleChip.tsx`, each `HitLines(..., /<ToggleChip\b/)` hit must have a
line among the three preceding lines containing both `role="group"` and `aria-label=`. Violations listed
as `path:line` with a message naming the fix. Red at HEAD with exactly three violations (Layouts:137,
Overlays:125, SystemVariables:128); green after §2.

The three-line window is a heuristic matched to the only shape in the repo (container `div`, `.map(`,
`<ToggleChip`). A future multi-line wrapper that exceeds it fails loudly, not silently, which is the safe
direction; widen the window then, with a reason.

## 5. Counterfactuals (phase 5/6 must run them)

- C1 Remove `role="group"` from one page after green → that page's R test and the guard both go red.
- C2 Put the attributes on the page's outer `<div>` instead → the guard fails (window) and the R test
  fails (`within(group)` returns header buttons too, list mismatch).
- C3 Misspell the label (`Filter by status`) → R test red; guard stays green (it pins shape, not text —
  accepted, the vitest tests pin text).
- C4 Delete Rules' group → P1 and the guard red.

## 6. Risks

None material. `getByRole('button')` within the group must not pick up anything else; the containers hold
only the chips today.
