# Plan 417: The cross-fab read-guard tests find their seeded fab-neutral row by identifier

**Spec**: [spec.md](./spec.md) · **Issue**: #3035 (item 4 only)

## 1. Bounded context and layers

AuditObservability, **tests only**: `tests/Integration.Tests/AuditObservability/CrossFabReadGuardIntegrationTests.cs`.
No Domain, Application, Infrastructure, Api, Contracts, AppHost or migration change. No entity, value object,
invariant, domain or integration event is added or altered. Boundary rules (NetArchTest) are unaffected.

## 2. Constitution / ADR alignment

| Rule | How this plan meets it |
|---|---|
| ADR-0103 | The tests keep running on the Aspire fixture; nothing moves to fakes or Testcontainers. |
| ADR-0139 / ADR-0144: never weaken a gate | Every replaced assertion is implied by its replacement (§4); `:109` is kept verbatim. |
| ADR-0144: the lane does not decide | Items 1-3 change visibility, a pinned status code, or need a live measurement: left to the owner. |
| ADR-0053 | Test names unchanged. |
| ADR-0036 | Two assertions change; no helper refactor, no rename, no widening to `:317`. |

No new ADR is needed: nothing here is an architectural decision.

## 3. The change

Both tests already call `NeutralRow()`; today the result is passed straight to `SeedAsync` and discarded.

1. Hold the seeded row in a local (`var neutral = NeutralRow();`) and seed it as now.
2. Collect the response rows (both tests already parse the page).
3. Replace the null-presence assertion with a lookup by identifier and an assertion on that row's fab, e.g.

   ```csharp
   var seeded = rows.Where(row => row.GetProperty("auditIdentifier").GetGuid() == neutral.Id.Value).ToArray();
   seeded.ShouldHaveSingleItem("the seeded NotApplicable row must be readable by a fab-assigned caller")
       .GetProperty("fab").GetString().ShouldBeNull();
   ```

   The shape is the test-writer's call within §4; mirror `AuditIdentifiersAsync` where it reads better.
4. Test 1 keeps `fabs.ShouldNotContain("berlin")` exactly. Test 2 keeps its `leaked.ShouldBeEmpty(...)` and
   `archived ... ShouldBe("munich")` assertions exactly; only `:162-164` changes.
5. Seed placement in test 2 stays at `:138` unless Assumption A-1 fails in CI; then move the seed to just before the
   berlin read, which changes arrangement, not assertion strength.

## 4. Why the new assertion is stronger (the "not weakened" proof)

Let `R` be the set of returned rows.

- Old: `exists r in R: r.fab == null`.
- New: `exists exactly one r in R: r.id == neutral.Id and r.fab == null`.

New implies Old (the witness is the seeded row). Old does not imply New: a page holding any other null-fab row
(another test's `NotApplicable` seed, or a leaked `Unresolved` row) and not the seeded one satisfies Old and fails New.
So the change is strictly stronger and can only turn a previously-green run red, never the reverse. A reviewer checks
this by reading the diff; it needs no stack.

`ShouldHaveSingleItem` additionally asserts no duplicate, which holds because `audit_id` is the primary key; if a
reviewer prefers, `ShouldContain` + `ShouldBeNull` is equally acceptable, as both imply Old.

## 5. Phase 4a: colour and evidence with Docker down

**Colour: characterisation, observed green.** Production behaviour is unchanged and the tests assert behaviour that
already holds (spec 396's `A_seeded_unresolved_row_is_hidden_...` proves the same rule at the Npgsql boundary with an
exact identifier set). A red test against unchanged production would mean a defect, not progress. This is not a
"behaviour-preserving refactor of production" either, so the colour's obligations are adapted honestly:

1. **Green against unchanged production**, observed in CI, not locally: Docker is down, so the only run that counts is
   CI's integration job on the PR tip. Its trx must list both names as `Passed` (memory: download the trx; a finished
   watcher is not a green run). Local evidence is limited to `dotnet build` (SC-1).
2. **Not weakened** by the implication in §4, written into the PR body.
3. **Counterfactual, required** (memory: prove a guard by counterfactual). In a throwaway commit **on a separate
   throwaway branch, never on the delivery branch** (rebase-merge lands every commit, and a red commit would poison
   bisect), change the seed of each test from `NeutralRow()` to a row with no fab and `FabScope.Owned`
   (`FabAttribution.Unresolved`, which the scoped search hides). Expected: both tests fail on the identifier lookup.
   Run it either locally once Docker is back, or as a **draft PR** from the throwaway branch whose CI integration job
   is read, then closed and the branch deleted. Quote the failure verbatim in the delivery PR.

   Informational only: under the counterfactual the **old** assertion would likely still pass (other null-fab rows
   exist in the shared table), which is the defect; do not make a claim about it unless it was run.

If neither a local Docker run nor a throwaway CI run is available, **the PR stays parked**: it may not merge on
build-green alone (spec SC-2, SC-3).

## 6. Verification commands

```sh
dotnet build tests/Integration.Tests -c Release
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~CrossFabReadGuardIntegrationTests" --logger trx   # needs Docker / CI
git diff --stat origin/develop
```

No shard-filter edit: the class already exists and is already listed (memory: new classes need an entry; this is not
a new class).

## 7. Phase 6

`backend-reviewer` only. Not security-sensitive: no authorization code changes; the tests guard an existing rule
more tightly.
