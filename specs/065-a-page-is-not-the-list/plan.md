# Implementation Plan: A page is not the list

**Spec**: `specs/065-a-page-is-not-the-list/spec.md`
**Issue**: #1982
**Branch**: `chore/1982-a-page-is-not-the-list`
**ADRs**: ADR-0139 (rules that fail the build, not the review), ADR-0144 (the
autonomous lane), ADR-0074 (two frontend apps), ADR-0052 (xUnit + Shouldly),
ADR-0053 (test naming)

---

## Shape of the change

No bounded context. No domain model, no aggregate, no value object, no message,
no migration, no runtime code at all. This feature adds **one test file and one
spec directory**.

That is worth stating plainly, because the usual plan sections — entities,
invariants, domain-to-integration event mapping, boundary rules — have nothing to
say here, and filling them with "N/A" six times reads as an oversight rather than
a fact.

| Layer | Change |
|---|---|
| Domain | none |
| Application | none |
| Infrastructure | none |
| Api | none |
| `apps/*` | none — read only, by the guard |
| `tests/Architecture.Tests` | one new file, `PaginatedConsumerTests.cs` |
| `specs/065-…` | `spec.md`, `plan.md`, `tasks.md` |

### Boundary rules

Unaffected. No project reference is added, no cross-context reference is
introduced, and `Shared.Contracts` is untouched. `tests/Architecture.Tests`
already reads repository files by relative path and references no product
project for that purpose, so the new file inherits an existing capability rather
than adding one.

---

## The guard

`tests/Architecture.Tests/PaginatedConsumerTests.cs`, following
`OverlayFrameClaimTests` in form: **consistency checks, not text pins.** Each
assertion reads two things and fails only when they disagree, so the guard pins
the tree against drift rather than against progress.

### The register

The set of bounded response types, and the boundary field each one carries, is
held **in the test file** as `[Theory]` data — not in a markdown table the test
reads back.

This is deliberate and it is the one design decision here worth arguing. A guard
that reads its expectations out of a document proves that the document was
written, not that the code obeys it; the two look identical from a green test
and diverge the first time someone edits the document to make the build pass.
The register is a handful of rows and belongs where it is enforced. In code it
is one row per *(response type, producing hook, boundary field)* triple — three
response types, four rows, because `AuditPage` has two producers — and both
assertions read the same rows.

| Response type | Produced by | List field | Boundary field |
|---|---|---|---|
| `CameraListPage` | `useListCamerasQuery` | `items` | `count` |
| `CameraChoices` | `useListAllCameraChoicesQuery` | `items` | `complete` |
| `AuditPage` | `useSearchAuditQuery`, `useGetResourceTimelineQuery` | `rows` | `nextCursor` |

`CameraChoices` carries `count` too, but `complete` is its boundary: `count` is
deliberately a sentinel (`gathered + 1`) after a mid-walk page failure, and a
consumer that reads `count` while ignoring `complete` would put a fabricated
number in front of an operator. Asserting on `complete` names the field a
consumer must actually consult.

### Assertion 1 — every consumer reads the boundary

For each register row: find every file under `apps/*/src` and `apps/shared/src`
that names the hook, excluding tests — **both** conventions, `*.test.ts(x)` and
`*.spec.ts(x)` — and the producing `*.api.ts` itself. Each such file must also
name that row's boundary field.

The second test convention was missed at implementation and caught in phase 6:
`apps/shared/src/realtime/client.spec.ts` sat inside a sweep the spec said
excluded tests. One file, no live consequence, and fixed in the code rather than
by narrowing the claim — a test fixture rendering `items` is a fixture, not a
screen, whichever suffix it carries.

Failure message names the file, the hook, and the missing field, plus a sentence
on why — enough to act on without opening the guard.

This is the assertion that fails on the spec-048 defect: `LayoutEditorDialog.tsx`
calling `useListCamerasQuery` and never mentioning `count`.

### Assertion 2 — the register is complete

Scan `apps/shared/src/api/**/*.api.ts` for exported object shapes — `export
interface X {` and `export type X = {`, the second having precedent at
`rules.api.ts:84` — that carry a list field beside a boundary field. **Three
shapes, not two.** Planning named two; `CameraChoices` fits neither, and the
third arm was added at implementation:

| Shape | Boundary | Example |
|---|---|---|
| Offset | `count` with `offset` / `limit` | `CameraListPage` |
| Gathered | `count` with `complete`, no `offset` / `limit` | `CameraChoices` |
| Cursor | `nextCursor` | `AuditPage` |

Recorded rather than quietly implemented: the two-shape rule stated here
originally excluded `CameraChoices`, which the same plan required the assertion
to find. That contradiction is the documentation-drift class CLAUDE.md has had
to correct repeatedly, and it is cheaper to write the third row down than to
leave the next reader reconciling a rule against a guard that disagrees with it.

**Every pair found must be in the register — the type *and* the hook.** Each
bounded response is paired with each `build.query` that answers with it, the
hook name derived from the endpoint by RTK Query's own convention (`use` +
capitalised endpoint + `Query`). This closes the hole phase 6 found: held as two
independent lists — response types here, hook rows there — a new contract could
be silenced by adding its type name alone. Assertion 1 would gain no row, every
consumer of its hook would go unswept, and the build would be green. The
register guaranteed only that someone had been *told*, which is the review
convention this whole feature argues against. As pairs, the smallest edit that
restores green is the one that names the hook and decides the boundary field.

A bounded response that no read endpoint produces is reported paired with
`(nothing declares it)`, which no register row can match — so it arrives red
rather than sitting outside the sweep.

Without this the guard is a snapshot with a longer shelf life: it polices the
three contracts that existed on 2026-09-04 and is blind to the fourth. This is
the assertion that makes it self-maintaining. It fails loudly on a *new*
paginated contract, which is precisely the moment a human should decide what its
boundary field is.

### Assertion 2b/2c — the sweep has a corpus

Assertion 1 reads whatever `apps/*/src` yields, **including nothing**. An app
restructured to hold its sources elsewhere, or a fourth app with a different
layout, leaves the corpus silently and every row of assertion 1 passes vacuously
and for ever — a failure indistinguishable from compliance, and the second one
phase 6 found. Two facts close it:

- every directory under `apps/` carrying a `package.json` has a `src/`, so no
  app's sources are outside the sweep;
- the corpus holds at least 40 files. It held 84 on 2026-09-04; the bound is
  under half, so ordinary churn cannot reach it and a collapse falls straight
  through it.

The mode is not hypothetical: `useGetResourceTimelineQuery` has no consumer, so
one register row is *already* vacuous. A documented vacuity does not detect an
undocumented one.

### Assertion 3 — no exception mechanism exists

The guard asserts that its own source contains no allowlist, skip-list or
exception collection.

This looks like belt-and-braces and is not. FR-005 says a necessary exception is
a **blocked outcome requiring human acceptance**, not a line added to the guard —
and the failure mode ADR-0144 names is precisely an agent reaching green by
weakening a gate. An assertion that the gate has no soft edge is cheap.

**It polices a vocabulary, not the mechanism**, and the earlier wording here
oversold it: it does not turn "someone quietly added an allowlist" into a build
failure, it turns *an allowlist called an allowlist* into one. Anyone who names
the same collection something else walks past it. That is still worth three
lines — the obvious move is the one an agent under pressure to reach green
makes, and this makes the obvious move loud — but it is a spelling check, not a
proof that no soft edge can be added.

If this proves awkward in practice it should be removed by a human with a
reason, not silently.

---

## Declared limitations

Stated here rather than discovered later, because a guard oversold is a guard
that gets trusted past its reach.

1. **It sees a reference, not a use.** A consumer that names `count` in a comment
   and ignores it passes. The guard makes the *omission* loud; it does not make
   *misuse* impossible. Full enforcement would need a type-aware AST pass over
   TypeScript from a C# test, which is a great deal of machinery for a defect
   whose observed form was "the field is never mentioned anywhere in the file".
   The cheap version catches the observed form.

2. **It is a source scan, not a type check.** A consumer that reaches a bounded
   response through an intermediate helper in a third file would be assessed on
   the wrong file. No such indirection exists today; `listAllCameraChoices` is
   the one helper and it is itself a register row.

3. **It polices `apps/`.** The one backend caller
   (`CameraCatalogFabLookup`) is out of scope per the issue and is recorded in
   `spec.md` as a follow-up candidate. Extending the guard across the language
   boundary is a larger change and would want its own justification.

4. **`getResourceTimeline` has no consumer**, so assertion 1 is vacuously true
   for it today. That is correct behaviour — an unconsumed endpoint cannot
   mislead — but it means one third of the register is currently unexercised
   against real consumers. Assertions 2b/2c exist because this vacuity is
   *documented*, and a documented one does not detect an undocumented one.

Limitations 1–3 are all **false negatives** — shapes the guard lets through.
Phase 6 noted the list was one-sided, so the other direction is recorded too:

5. **It can also be wrong the other way.** A component that forwards the whole
   response to a child — `<CameraTable page={data} />` — reads the boundary in
   the child and will still be asked to name it in the parent; so will a call
   made only to warm the cache, which renders nothing to qualify. Neither shape
   exists in `apps/` today. It is written down so the first occurrence reads as
   a known limit to be discussed with a human, per FR-005, rather than as a
   broken guard to be worked around — which is exactly how an undeclared false
   positive gets a rule quietly weakened.

---

## Phase 4a: red, and how

**Behaviour-changing.** The guard is new behaviour: a build that failed on
nothing now fails on something. Per constitution §Testing and ADR-0139 the test
is observed failing and the failure is quoted in the PR.

The red is not free here, and the honest route matters. The guard is green on the
tree as it stands — that is the whole argument for its admissibility — so
"write it and watch it fail" is not available. The red is produced by
**temporarily reintroducing the defect**:

1. Write `PaginatedConsumerTests.cs`. Run it. Expect green.
2. In the working tree only, strip `LayoutEditorDialog.tsx` back to the
   pre-spec-048 shape: remove the truncation notice block and every reference to
   `cameras.complete` / `cameras.count`, leaving it rendering `cameras.items`
   alone.
3. Run the guard. **Observe it fail**, naming `LayoutEditorDialog.tsx`. Capture
   the verbatim output — this is what phase 4a returns and what the PR quotes.
4. `git checkout -- apps/management-web/src/features/layouts/LayoutEditorDialog.tsx`.
   Confirm the guard is green again and the file is byte-identical to `HEAD`.

Step 4 is a task in its own right, not a footnote. A guard demonstration that
leaves the demonstration committed would silently re-ship the defect this feature
exists to prevent — the worst possible outcome for this particular change.

This mirrors spec 048's own Independent Test — *"remove the notice and confirm
the test fails; an assertion that passes on a complete list proves nothing"* —
applied one level up, to the guard rather than to the notice.

**Note for the engineer**: the test-writer's step-3 output is the brief. The
guard may not be edited to pass, and the `LayoutEditorDialog.tsx` revert may not
be substituted for a change to the guard's file filter.

---

## Risks

| Risk | Mitigation |
|---|---|
| The demonstration edit gets committed | Its own task; verified with `git status` and a diff against `HEAD` before the commit task runs |
| The guard is brittle against a rename of a hook | It fails loudly and names what it could not find. A renamed hook with no register update is exactly the drift it should catch |
| Assertion 2 false-positives on an unrelated interface with a `count` field | Requires `count` **and** `offset`/`limit`, or `nextCursor` — the two shapes actually in use. A bare `count` does not trip it |
| Assertion 3 is judged over-strict in review | Documented above as removable by a human with a reason; it is three lines |

---

## Gate

Phase 3 ends here. The feature's issue (#1982) goes on Project #13 by hand —
`/speckit-tasks` adds nothing to the board — and this feature does **not** want
per-task issues. Per-task issues stopped after spec 028; the feature-level issue
is the tracked artefact and `tasks.md` is what the work is measured against.

```sh
gh project item-add 13 --owner smartsolutionslab --url <issue-url>
```
