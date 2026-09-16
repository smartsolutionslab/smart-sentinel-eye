# Tasks — Spec 170, a cancellation that cannot be honoured

**Phase:** 3 (Tasks) — ADR-0037
**Issue:** [#2418](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2418) · **Branch:** `fix/2418-a-cancellation-that-cannot-be-honoured`
**Spec:** `spec.md` · **Plan:** `plan.md`
**Engineer:** `backend-engineer` · **Reviewer:** `backend-reviewer`
**Phase 4a colour:** **BEHAVIOUR-CHANGING → RED.** Two counterfactuals, both
deterministic; see *Evidence* below. T-B alone is a characterisation and is
labelled as one in its own doc comment — the *slice* is red (ADR-0144:
ambiguity resolves to red).
**Latency (§IV): N/A** — no production runtime behaviour changes, no leg moves.
**New ADR required: no** — `spec.md` §6. If T003 cannot hold without changing
`ValidateAsync`'s control flow, **stop and report**; that is ADR-shaped and
the lane may not write one.
**Board gate:** #2418 is **already on Project #13**, status Todo, carrying
`agent:ready` — verified with `--limit 2000`. Nothing to add. Feature-level
tracking since spec 028; do **not** run `/speckit-taskstoissues`.
**Delivery:** one PR → `develop`, cut fresh from `origin/develop`. **Not stacked.**

---

## `[P]` markers — ADR-0109

**Nothing here is parallel.** T002-T005 all edit the same file,
`WhepValidatorUnreachableRealmTests.cs`, so they own no disjoint files and
none may be marked `[P]`. T008 is the only task touching a different file and
it is still ordered last-but-one, because its hash proof must be taken against
a tree that is otherwise final. Sequential throughout: T001 → … → T009.

**Contention (ADR-0109):** `WhepValidatorUnreachableRealmTests.cs` has been
edited by specs 119, 089 and 166's neighbourhood. Check for an in-flight
branch touching it before merging; the diff is a full-file rework and will not
merge cleanly alongside another.

---

## An unusual 4a/4b split — read before starting

The defect lives **in a test**, so `test-writer` (4a) does almost the whole
change and `backend-engineer` (4b) does the evidence, the counterfactuals and
one comment. That is deliberate, not a mis-assignment: the phase-4 rule that
matters is that **the engineer may not edit the tests to make them pass**, and
it binds here exactly as it does anywhere. If 4b finds a test it wants to
change, it reports back to 4a; it does not edit.

---

## Evidence — what must be produced, verbatim, in the PR body

**Counterfactual 1 — the defect (T001).** The hang is a coin flip on a timer,
and this workstation lost that flip zero times in twelve runs
(`spec.md` §3b), so "I ran it and it hung" is not available. Widening the
window from 50 ms to 30 s removes the coin and leaves the defect: the run then
hangs on the first attempt. Already performed at phase 1 and reproduced
verbatim in `spec.md` §3c; **T001 repeats it on the branch** so the PR carries
its own evidence.

**Counterfactual 2 — the assertion (T006).** Widen
`catch (InvalidOperationException)` to `catch (Exception)` in
`WhepAuthValidator.ValidateAsync`. T-A must fail with its own message. This is
the protection the original test was written for (spec 119) and the whole
reason it may not simply be deleted.

**Determinism (T007).** 50 consecutive clean runs, *plus* the structural
proof: the fixed T-A contains no `CancellationTokenSource` delay at all, so
there is no window left to widen. The structural proof is the stronger of the
two — `spec.md` §3b is a live demonstration that 12 green runs can coexist
with the bug.

---

## US1 — the backend bucket stops hanging (P1)

Independently shippable; closes #2418 on its own.

### `[T001]` `[US1]` Capture the defect, deterministically — RED evidence 1

On the branch, **before any fix**: change `TimeSpan.FromMilliseconds(50)` →
`TimeSpan.FromSeconds(30)` at `WhepValidatorUnreachableRealmTests.cs:108`, then

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release \
  --filter "FullyQualifiedName~WhepValidatorUnreachableRealmTests.A_cancelled_request_stays_cancelled" \
  --blame-hang --blame-hang-dump-type none --blame-hang-timeout 45sec
```

Expect `Test Run Aborted` naming this test. **Quote the output verbatim.**
Then `git checkout --` the file **and `touch` it** — a restored file keeps its
old timestamp and MSBuild will skip the rebuild (repo memory). Confirm
`git status --porcelain` is clean before proceeding.

`-c Release` throughout: the Aspire stack (pid 3312) holds the `Debug`
binaries. Do not stop the stack.

**Done when:** the abort message is captured and the tree is clean.

### `[T002]` `[US1]` Bound every fake so none can hang the host

`plan.md` §2.4. Rename `CancellingRealm` → `SlowRealm`; replace
`Task.Delay(Timeout.InfiniteTimeSpan, cancel)` with a **fixed short bound**
that completes regardless of the token, then return `DiscoveryDocument`.
Rewrite its doc comment: the current one claims a retriever-thrown
`OperationCanceledException` propagates unwrapped, which 8.19.2's
`catch (Exception ex)` falsifies (`spec.md` §4.1).

**Done when:** `grep -n "InfiniteTimeSpan" tests/StreamDistribution.Infrastructure.Tests/Auth/WhepValidatorUnreachableRealmTests.cs`
returns nothing, and no fake's completion depends on a `CancellationToken`.

### `[T003]` `[US1]` Rewrite the cancellation test onto the guarantee that exists

`plan.md` §2.3 T-A. `A_request_cancelled_before_the_realm_is_reached_stays_cancelled`
(ADR-0053 naming): `ValidatorOver(new UnreachableRealm())`, a
`CancellationTokenSource` **cancelled outright** — no timer — assert
`OperationCanceledException`, assert the result is not an
`IdentityProviderUnavailable` failure, and assert `logs.Entries` is empty.

Class-level doc: record that a caller **cannot** cancel out of a realm's first
metadata fetch, because `ConfigurationManager<T>` 8.19.2 fetches with
`CancellationToken.None` on both code paths, and that this is the library's
stated design rather than a defect here.

**No timer may appear anywhere in this test.** A window is what created
#2418; choosing a luckier number is not a fix.

**Done when:** the test passes, and it contains no `TimeSpan`.

### `[T004]` `[US1]` Characterise the boundary the library actually draws

`plan.md` §2.3 T-B. `A_cancellation_arriving_mid_fetch_does_not_abandon_the_first_metadata_read`,
over `SlowRealm`: cancel the caller's token once the fetch is under way and
assert the call **completes on the realm's answer**. Doc comment must say
plainly that this pins a third-party boundary, that red here means the library
changed its mind, and that it is the tripwire whose absence made #2418
possible.

Depends on T002.

**Done when:** it passes, and the whole assembly still finishes in its normal
~6 s — a characterisation that adds seconds to the bucket is over-bounded.

---

## US2 — the in-flight cancellation that is real (P2)

### `[T005]` `[US2]` The viewer queued behind another viewer's first fetch

`plan.md` §2.3 T-C. New gate fake: signals entry via `TaskCompletionSource`,
then `await Task.WhenAny(gate.Task, Task.Delay(<hard cap>))` — **never** a
bare `await gate.Task`. Caller A enters and is held; caller B calls with its
own token; await A's entry signal, cancel B's token, assert B throws
`OperationCanceledException`. Release the gate and await A so nothing outlives
the test.

Both sub-races land on the same statement and the same exception
(`plan.md` §2.3); B cannot reach the retriever, because A holds the
configuration lock.

Depends on T002 (same file, and it inherits the §2.4 invariant).

**Done when:** it passes, and T007's loop shows no flake in it either.

---

## Verification and the second counterfactual

### `[T006]` `[US1]` The assertion counterfactual — RED evidence 2

Widen `catch (InvalidOperationException)` → `catch (Exception)` in
`WhepAuthValidator.ValidateAsync`. Run T003's test; it **must** fail. Quote the
failure verbatim. Revert the widening and confirm it passes again and
`git status --porcelain` is clean.

If it does **not** fail, stop: the replacement test has lost the protection
spec 119 built and the design in `plan.md` §2.3 is wrong. Report; do not
proceed.

### `[T007]` `[US1]` `[US2]` Determinism proof

1. Structural: grep the file for a `CancellationTokenSource` constructed with
   any delay — there must be none in T003, and none anywhere that a test's
   outcome depends on.
2. Statistical: 50 consecutive runs of the whole assembly,

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --no-build \
  --blame-hang --blame-hang-dump-type none --blame-hang-timeout 30sec
```

   zero hangs, zero failures. Report the count and the per-run pass total.

Foreground, generous timeout, one loop — background commands die with the turn
in this environment.

### `[T008]` `[US3]` Correct the comment that names the wrong mechanism (P3)

`plan.md` §4. In `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs`,
replace the catch comment's claim about the retriever's wrapping with the real
mechanism: the `OperationCanceledException` arrives from the configuration
lock's already-cancelled check and only from there; a cancellation that
reaches the retriever yields `IdentityProviderUnavailable`, and that is
correct — a metadata fetch that cannot complete is an unavailable realm.
Update the cross-reference to T003's new test name.

**Proof obligation:** strip comments from the file before and after, hash both,
show them equal (repo memory: characterise a comment-only change by hashing
the code). Do not assert that prose contains a string, and do not settle for
eyeballing the diff.

### `[T009]` Build, format, analyzers, and the PR

`dotnet build -c Release` clean (Release is where the metric analyzers bite),
`dotnet format --verify-no-changes`, the whole assembly green. Assemble the PR
body: both counterfactuals verbatim (T001, T006), the determinism figures
(T007), the hash equality (T008), `Phase 5`/`Phase 6` per ADR-0037.

Commits: Conventional Commits, **no `Co-Authored-By`** (ADR-0086/0030). Each
commit must build **on its own** — rebase-merge lands them individually
(ADR-0087) — so T002+T003 belong in one commit (T003 does not compile against
the un-renamed fake) rather than two.

`gh pr create --base develop`. Close #2418 with a closing keyword and **check
the issue state after the merge** (repo memory: a PR mention auto-closes about
one time in three).

---

## Dependencies

```
T001 (evidence, clean tree)
  └─ T002 (bound the fakes)
       ├─ T003 (T-A)  ─┬─ T006 (catch counterfactual)
       ├─ T004 (T-B)   │
       └─ T005 (T-C)  ─┴─ T007 (determinism)
                            └─ T008 (comment + hash)
                                 └─ T009 (build, format, PR)
```

T001 must precede T002: once the fake is bounded, the defect cannot be
reproduced any more, and the evidence is gone.

## Stop conditions

Report and halt rather than widening scope, if:

- T006's counterfactual does not fail — the replacement test lost spec 119's
  protection.
- T003 cannot be made to hold without editing `ValidateAsync`'s control flow —
  ADR-shaped, and the lane may not write an ADR (ADR-0144).
- Any file outside `plan.md` §7's table turns out to need editing — in
  particular a `.csproj`, `Directory.Packages.props`, or `ci.yml`.
- T007's loop shows a single hang or failure.
