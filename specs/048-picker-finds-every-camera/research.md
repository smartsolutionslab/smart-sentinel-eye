# Research — 048 the camera picker finds every camera

Phase 0. Everything below was read out of the source or the dependency tree, not
recalled. Where a claim is a judgement rather than an observation it says so.

---

## R1. How to fetch past one page: a paging `queryFn`, not `infiniteQuery`

**Decision: one endpoint whose `queryFn` pages internally and returns a single
combined result.**

RTK Query 2.12 is present and does ship `infiniteQuery`. It is the wrong tool
here for two reasons, and the second is the one that matters:

- **It models user-driven "load more".** It exposes `fetchNextPage` and hands
  back `data.pages` — an array of pages. The picker does not want a page at a
  time on demand; it wants the choosable set, once, when the dialog opens.
- **It changes the shape the picker consumes.** The camera `<select>` is a
  controlled React Hook Form field registered with `register()`. FR-011 requires
  a selection already made to survive the list being refreshed or extended.
  Every change to the option-list shape is a chance to break that, and the
  cheapest way not to break it is not to change the shape: keep returning
  `items`, and let the picker's `cameras.map(...)` stay exactly as it is.

A `queryFn` receives the endpoint's `baseQuery` and may call it as many times as
it likes before returning one result. `camerasApi` is built on
`gatewayBaseQuery('camera-catalog/cameras')`, a `fetchBaseQuery`, so the paging
loop reuses the same auth and error handling every other call gets rather than
reimplementing them.

**No `queryFn` exists anywhere in `apps/shared/src/api` today.** This introduces
the pattern, which is worth saying out loud so the next person knows it was a
decision and not an accident.

**Alternatives considered**

- *`infiniteQuery`* — above.
- *Two `useListCamerasQuery` calls in the dialog and concatenate in the
  component.* Rejected: it puts paging arithmetic in a component, and
  `apps/shared` owns the API client. It also multiplies cache entries the picker
  has to keep consistent.
- *A new server endpoint returning a lightweight all-cameras list.* Rejected as
  out of proportion: it is a contract change for a problem the existing contract
  can answer in two requests.

---

## R2. Sort by name, ascending — and this is not just cosmetics

**Decision: the picker requests `sort=name`, `order=asc`.**

`AllowedSortFields` is `["name", "registeredAt"]`, so `name` is available. The
picker currently inherits the default, `registeredAt desc` — which is why the
cameras it showed were "the 50 most recently registered", an order no operator
has any reason to think in.

Two consequences beyond looking tidier:

1. **It is what makes the deferred search story survivable.** Native `<select>`
   elements have built-in keyboard type-ahead. On an alphabetical list that is
   genuinely navigable; on a list ordered by registration date it is close to
   useless, because the operator cannot predict where anything is.
2. **It makes the truncation honest in a different way.** "The 250 cameras whose
   names come first" is a describable set. "The 50 most recently registered" is
   an accident of history that reads as arbitrary to the person looking at it.

**Cost, recorded rather than hidden**: `registeredAt desc` puts new cameras at
offset 0, so a registration mid-paging shifts every later page by one — which
duplicates a camera at a page boundary rather than dropping one. `name asc`
inserts in the middle, so a registration can duplicate *or* a retirement can
drop. See R4; both are covered.

---

## R3. Page size 200, bounded at 5 pages — complete to a stated bound, honest at any scale

**Decision: request 200 per page (the maximum the source permits), fetch at most
5 pages, then stop.**

- **200 per page** because the handler refuses anything larger and there is no
  reason to ask for less. A 250-camera fab costs **two** requests.
- **5 pages — 1000 cameras — as the bound.** Four times the constitution's
  250-camera production target, so the target is met with room, while an
  unbounded loop is avoided. Unbounded "fetch until `count`" turns a
  10,000-camera fab into 50 sequential requests issued while an operator waits
  on a dialog, and that failure would be far worse than the one being fixed.

**This is the plan's central idea, and it only works because US1 exists.** The
picker is **complete** up to a stated bound and **honest** at any scale. When
the bound is reached and cameras remain, the picker does not pretend — it falls
straight through to US1's truncation notice, which says how many are shown and
how many exist. So the residual limit is not a hidden cliff like the one being
removed; it is a stated one.

**1000 is a judgement, not a measurement.** Nothing was benchmarked. It is
chosen as a comfortable multiple of the only scale number the constitution
states. If someone later runs a fab of 1200 cameras, the picker tells them what
it is not showing rather than lying — which is the property worth having.

---

## R4. The list changing under the paging loop

**Decision: concatenate, de-duplicate by camera identifier, and let the count
speak for the rest.**

Paging by offset over a list someone else is editing has two failure modes:

- **A camera registered mid-loop** shifts later pages down, so a camera at a
  page boundary can be fetched **twice**. In a `<select>` that renders as two
  identical options and a duplicate React key. De-duplication by
  `cameraIdentifier` removes it.
- **A camera retired mid-loop** shifts later pages up, so one can be **missed**
  entirely. This cannot be fixed by de-duplication — and it does not need to be.
  The result carries fewer items than `count`, which is exactly the condition
  US1's notice fires on. The operator is told the list is incomplete.

**The same mechanism covers the page bound, the concurrent-edit race, and any
future cause of incompleteness.** That is the argument for building US1 first
rather than treating it as polish.

---

## R5. Where the notice lives so it is announced, not merely painted

**Decision: one notice per dialog, associated with every camera `<select>` via
`aria-describedby`.**

The camera list is fetched once for the whole dialog and shared by every tile —
a 2×2 wall has four selects backed by one list. Two things follow:

- **The notice is stated once**, not per tile. Twelve copies of the same
  sentence is noise on screen and considerably worse through a screen reader.
- **Each select points at it** with `aria-describedby`. That is what gets it
  read when the control takes focus, which is the moment an operator needs to
  know the list is incomplete. A paragraph rendered elsewhere in the dialog is
  painted, not announced — a sighted operator might notice it; a screen-reader
  user tabbing straight into the select would not.

The text is static once loading finishes, so `aria-describedby` is the right
association. A live region would announce it on arrival and then be silent for
anyone who focuses the control later.

**Settled by reading it**: `FormField` takes `label`, `htmlFor`, `error`,
`children` and `className` — there is no description slot. So the notice sits
beside the grid with its own id and each select references it. Adding a slot to
`FormField` was considered and rejected: it is a shared composite used across
both apps, and widening it for one caller is a change to everything to serve
one screen.

---

## R6. Native `<select>` type-ahead — the honest reason US3 is deferred, not dropped

**Finding: browsers give prefix type-ahead on `<select>` for free. It softens
the 250-option problem; it does not solve it.**

Typing jumps to the option whose text **starts with** what was typed. So a
camera named `Furnace 3` is reachable by typing `furn`. A camera named
`Line 2 Furnace` is **not** — the operator would have to know it begins with
`Line`.

This is why US3 (search by name) is deferred rather than dropped:

- **Deferring is defensible** because an operator who knows their camera's name
  has a working, if imperfect, way to reach it — especially once R2 sorts the
  list alphabetically.
- **Dropping would not be**, because prefix matching fails exactly where fab
  naming conventions put the distinguishing part last, which is common
  (`Line 2 Furnace`, `Bay 4 Inlet`).

US3 also needs two things that do not exist: a name filter on the camera source,
and a combobox in the design system. `@radix-ui/react-select` is a dependency
but **Radix ships no combobox**, and a Select is not one — it does not filter.
So US3 is a new server capability plus a new UI primitive: a spec, not a task.

---

## R7. No C# is needed, and that is worth stating plainly

**Finding: this feature touches no backend code at all.**

Everything US1 and US2 need already exists server-side: the endpoint pages, it
reports a total, it sorts by name, and it scopes to the caller's fabs before
counting. The defect is entirely in one consumer.

So the house rules on `Ensure.That`, collection expressions and value objects do
not come into play here — not because they are being skipped, but because there
is no C# in this change. The work is:

- `apps/shared` — the API client gains a paging endpoint. This is where it
  belongs; the client is shared and management-web is not its owner.
- `apps/management-web` — the dialog consumes it and renders the notice.

If planning discovers a server change is needed after all, that is a signal the
scope was misjudged and should go back through the gate rather than be absorbed.

---

## R8. What a green suite here will not prove

Recorded because spec 046's review found a defect that **every one of its sixteen
mutations missed** — the tests covered the transitions they had thought of and
could not cover a starting state they never constructed.

The equivalent blind spot here is **scale**. Every test will run against a
fixture of a few cameras, or a mocked count. None of them will run against 250.
So a green suite will establish that the paging arithmetic is right, that the
notice appears when items are fewer than the count, and that a selection
survives a refresh. It will **not** establish:

- that two round trips at 250 cameras feel acceptable to an operator opening a
  dialog;
- that a 250-option `<select>` is usable in practice, as opposed to correct;
- that the 1000-camera bound is the right number, since nothing was measured.

The first two need a person and a populated fab. The verification note must say
which of those actually happened rather than implying the suite covered them —
and if the fab is not available, say that instead of quietly narrowing the claim.
