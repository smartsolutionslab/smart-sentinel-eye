# Quickstart: seeing an archived layout come back

**Feature**: `037-recover-archived-revision` · **Plan**: [plan.md](./plan.md)

How to watch the feature work, and what the verification note on the PR must
contain.

---

## Boot

```sh
dotnet run --project src/AppHost
bash scripts/wait-for-e2e-stack.sh
```

`management-web` serves on <http://localhost:5173>.

---

## The happy path — recover a wall, by hand

1. **Layouts → New layout.** Name it `recovery-demo`, make it a 2×2, place at
   least two tiles on registered cameras, and bind an overlay to one of them.
   The overlay binding matters: FR-002 says the recovered draft carries it, and
   it is the easiest part of the payload to lose.
2. **Publish** it. The row reads `v1 · Published`.
3. **Archive** it, and read the confirmation before you confirm. It must say the
   layout is taken out of service, that you can bring it back by editing it, and
   that the tiles are kept. It must **not** say the layout can never be edited or
   published again, and must **not** say "this cannot be undone".
4. The row reads `v1 · Archived`. **Before this feature it offered nothing at
   all.** It now offers **Edit (new draft)**.
5. **Edit (new draft).** The designer opens on revision 2, pre-loaded with the
   2×2 grid, both tiles and the overlay binding — everything from revision 1.
6. Change something, save, **Publish**. The row reads `v2 · Published`.
7. Confirm the identifier under the name is **the same one** it had at step 2.
   Recovery adds history; it does not mint a new chain.

Repeat with an overlay for the twin. The overlay path is shorter — its Edit
button branches directly with no designer step.

---

## The two shapes that must still refuse

**A chain with an open draft** — create a layout and do **not** publish it. It
has no Published revision, so it is superficially like the recovered one. Ask for
a new draft:

```sh
curl -i -X POST "$GATEWAY/layout-composition/layouts/$ID/draft" \
  -H "Authorization: Bearer $TOKEN" -H "If-Match: \"0\""
```

Expect `409` with code `LAYOUT_NO_PUBLISHED_REVISION` and a message naming the
open draft. **This is the guard the feature is one careless edit away from
deleting** — a fallback written as "the newest revision, whatever its state"
makes this succeed and mints a second competing draft.

**A chain whose name was taken while it sat archived** — archive `recovery-demo`
to strandedness, create a *new* layout also named `recovery-demo` in the same fab
(it succeeds, because a fully-archived chain releases its name), then try to
recover the first. Expect `409` with `LAYOUT_NAME_TAKEN` naming the collision.

---

## Verification note for the PR

State each of these, with what was observed rather than what was expected:

- **Backend**: `dotnet build -c Release` clean with analyzers; the four affected
  test projects green; `pwsh scripts/coverage-check.ps1` meeting Domain ≥ 90% and
  Application ≥ 80%.
- **The integration tests ran**, not just compiled. Both of them, against the
  Aspire stack, with the archive → branch → edit → publish sequence completing.
  Say so explicitly — spec 028 shipped two integration tests that had never
  executed.
- **SC-005**: the four protected refusal tests are **untouched**. Show it as an
  empty `git diff` over `LayoutTests.BranchDraft_without_a_Published_revision_throws`,
  `OverlayTests.BranchDraft_without_a_Published_revision_throws` and the two
  `Chain_without_a_Published_revision_*` / `Branching_without_a_Published_revision_*`
  handler tests. If any of them changed, say which and why — that is a finding,
  not a fix.
- **Frontend**: `pnpm typecheck && pnpm lint && pnpm test` clean; the Playwright
  suite run and its count reported. Note plainly whether any e2e test covers this
  — today none does.
- **The deliberate break.** Prove the wording assertions fire the way spec 036
  T018 did: soften the layout confirmation to exactly `This cannot be undone.`,
  run the page's tests, and record which assertions go red and how many. Then
  revert. An assertion that has never failed is a claim, not a check.
- **The second deliberate break**, specific to this feature: widen
  `NewestWhenFullyArchivedOrNull()` to return the newest revision unconditionally
  and record that the protected draft-only tests go red — in the domain *and* in
  the application layer, in both aggregates. Then revert. That is the evidence
  the narrowing is load-bearing rather than decorative, and it is the single
  most likely regression this feature can suffer later.
- **Both twins.** Every behavioural claim above, demonstrated for the layout and
  for the overlay (SC-007).
