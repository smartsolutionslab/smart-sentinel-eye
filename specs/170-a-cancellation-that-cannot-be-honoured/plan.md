# Plan — Spec 170, a cancellation that cannot be honoured

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2418](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2418) · **Branch:** `fix/2418-a-cancellation-that-cannot-be-honoured`
**Spec:** `spec.md`
**Engineer:** `backend-engineer` · **Reviewer:** `backend-reviewer`
**New ADR required: no** — `spec.md` §6.

---

## 1. Bounded context and layers

**Context:** StreamDistribution. **Layer touched:** its *test* assembly only,
plus one comment in Infrastructure (US3).

| Project | Layer | Change |
|---|---|---|
| `tests/StreamDistribution.Infrastructure.Tests` | test | three tests, two fakes, three doc comments |
| `src/StreamDistribution/Infrastructure` | Infrastructure | **US3 only**: one comment block, zero IL |

No Domain, no Application, no Api. No entity, aggregate, value object,
invariant, repository, migration or DI registration is added, removed or
changed. **Nothing in this slice can produce a primitive-typed domain property**,
so `PrimitiveBoundaryTests` is not in play.

**Messaging:** none. No domain event, no integration event, no
`Shared.Contracts` type, no Wolverine handler, no queue. Nothing crosses a
context boundary, so the NetArchTest boundary rule is untouched and cannot be
weakened by this slice.

**Boundary rules:** unchanged. The test assembly already references only its
own context's Infrastructure/Application plus `Shared.Kernel` and
`ServiceDefaults`; no new project reference is added.

## 2. The design decision phase 2 owed — what the test should now assert

`spec.md` §4 establishes that `A_cancelled_request_stays_cancelled` asserts a
guarantee `ConfigurationManager<T>` does not make. The decision, and why.

### 2.1 Rejected: keep asserting first-fetch cancellation

Cannot be made to hold. Site 3 passes `CancellationToken.None` on **both**
code paths (`ConfigurationManager.cs:262`, `ConfigurationManager_Blocking.cs:61`),
by an explicit design comment. Any test that waits for the caller's token to
take effect there waits forever. Bounding the fake's delay would convert the
hang into a *silently wrong pass* — the delay expires, the realm answers, and
`Should.ThrowAsync<OperationCanceledException>` fails, or worse, still passes
whenever the already-cancelled race wins. **A bounded delay alone is not a
fix**; it relocates the flake.

### 2.2 Rejected: the "second request" path suggested in the brief

Checked, and it does not work. Once a configuration is cached,
`GetConfigurationAsync` returns at `ConfigurationManager.cs:206-207` *before*
`cancel` is read at all (`spec.md` §2, site 1). A second request does not
honour the token better — it honours it **not at all**, by returning
instantly. There is no in-flight second-request path to cancel.

### 2.3 Chosen: assert the guarantees that exist, and characterise the one that does not

Three tests, each aimed at one row of `spec.md` §4's table, none of them timed.

**T-A — `A_request_cancelled_before_the_realm_is_reached_stays_cancelled`** (replaces the current test; US1)

- Arrange `ValidatorOver(new UnreachableRealm())` and a `CancellationTokenSource`
  **cancelled outright** (`cts.Cancel()`), not on a timer.
- Assert `OperationCanceledException`, and — the discriminator — assert that the
  outcome is *not* an `IdentityProviderUnavailable` failure, plus
  `logs.Entries.ShouldBeEmpty()`.
- **Why `UnreachableRealm` and not a delaying fake.** It is the sharpest
  available control: this exact fake, with a live token, is the arrangement of
  `An_unreachable_realm_refuses_instead_of_throwing`, which asserts
  `IdentityProviderUnavailable`. Same fake, same validator, only the token
  differs, opposite outcomes. That pins the claim to the *token* and nothing
  else — no delay, no scheduler, no window.
- **The counterfactual is preserved.** The `OperationCanceledException` leaves
  `GetConfigurationAsync` from site 2 and crosses `catch (InvalidOperationException)`
  untouched. Widened to `catch (Exception)`, it is swallowed into
  `IdentityProviderUnavailable` and T-A fails — the exact edit the original
  test was written to catch (spec 119), now caught deterministically instead of
  half the time.
- The empty-log assertion is not decoration: under the widened catch the
  transition logger also fires, so a caller who went away would write a
  realm-outage warning. That is the second-order damage of the widened edit and
  it now has an assertion.

**T-B — `A_cancellation_arriving_mid_fetch_does_not_abandon_the_first_metadata_read`** (US1)

- Arrange a realm that answers **after a short bounded delay**; cancel the
  caller's token once the retriever has been entered; assert the call
  **completes on the realm's answer** and does not throw.
- This is a *characterisation of the library boundary*, and it is labelled as
  one in its doc comment. It is the test whose absence made #2418 possible:
  it is the only thing that will go red if a future
  `Microsoft.IdentityModel.Protocols` starts threading the caller's token into
  the retriever. Red here is not a regression — it is the notification that
  T-A's neighbouring assumption moved.
- It also keeps the bounded fake in genuine use, so US1 does not leave dead
  code behind.

**T-C — `A_viewer_queued_behind_another_viewers_first_fetch_can_still_cancel`** (US2)

- The one in-flight cancellation site 2 genuinely honours, and the one that
  matters in production: `WhepAuthValidator` is a singleton (its own
  `Interlocked.Exchange` comment says so) and a wall of kiosks opens WHEP at
  once, so *queued behind someone else's first fetch* is the normal state
  during a cold-cache outage, not an exotic one.
- Caller A enters the retriever and signals a `TaskCompletionSource`; caller B
  calls with its own token; the test awaits A's entry signal, cancels B's
  token, and asserts B throws `OperationCanceledException`. Then the gate is
  released and A is awaited so nothing outlives the test.
- Deterministic in outcome **whichever way the two sub-races resolve**: if B
  reaches `WaitAsync` first it cancels while queued; if B is slower it finds
  the token already cancelled. Both throw `OperationCanceledException` from
  the same statement. There is no third branch — B cannot reach site 3,
  because A holds `_configurationNullLock`.

### 2.4 The invariant that outranks all three: no fake may hang the host

Stated separately because it must survive any later change to §2.3.

**Every `IDocumentRetriever` fake in this file completes within its own fixed
bound, regardless of the token it is handed.** `Timeout.InfiniteTimeSpan` is
banned from this file. A fake's delay is `Task.Delay(<fixed short span>)`; any
gate is `await Task.WhenAny(gate.Task, Task.Delay(<hard cap>))`, never a bare
`await gate.Task`. A fake that can only be released by a token is a fake that
can hang the host, because §2's site 3 decides which token it gets and the
test does not.

This is the part of the fix that must hold even if a future library version
changes everything else — and it is what turns "this flake is fixed" into
"this failure mode cannot recur here".

## 3. The fakes

| Fake | Now | After |
|---|---|---|
| `UnreachableRealm` | throws `IOException` IDX20804 | **unchanged** — used by T-A and by the two existing outage tests |
| `CancellingRealm` | `Task.Delay(Timeout.InfiniteTimeSpan, cancel)` | **renamed** (e.g. `SlowRealm`) — bounded delay, then returns `DiscoveryDocument`; used by T-B. Doc comment rewritten: the current one asserts a retriever-thrown `OperationCanceledException` propagates unwrapped, which 8.19.2's `catch (Exception ex)` falsifies (`spec.md` §4.1) |
| — | — | **new** gate fake for T-C (US2): signals entry via `TaskCompletionSource`, waits on `Task.WhenAny(gate, hard cap)` |
| `ScriptedRealm`, `ReachableRealm` | — | **unchanged** |

Naming follows the file's existing `<Adjective>Realm` convention (ADR-0091: no
shortcuts or aliases).

## 4. US3 — the comment in `WhepAuthValidator.cs`

Comment-only. The sentence "`ConfigurationManager` raises it even when the
retriever wrapped the cancellation" is literally defensible and materially
misleading: the `OperationCanceledException` comes from the configuration
lock's already-cancelled check and *only* from there, and a retriever that
wraps a cancellation yields `IdentityProviderUnavailable` instead. The
replacement names the mechanism (site 2), names what is *not* honoured (site
3, `CancellationToken.None` by design), and says the resulting
`IdentityProviderUnavailable` is correct — a metadata fetch that cannot
complete is an unavailable realm.

**Proof obligation:** strip comments from
`src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` before and
after, hash both, show them equal. Asserting "the prose contains X" proves
nothing about the code; the hash does. Do **not** merely eyeball the diff.

## 5. Risks

| Risk | Mitigation |
|---|---|
| A bounded delay silently converts the hang into a flaky *assertion* failure | §2.1 — no test asserts cancellation against a delay at all. T-B asserts *completion*, which the bound guarantees |
| T-C introduces a new concurrency flake | §2.3 — both sub-races land on the same statement and the same exception; the gate has a hard cap independent of any token |
| The fix is judged by a green CI bucket | `spec.md` §8 — green was the outcome in five of ten runs *with* the defect. Steps 1-4 are the evidence |
| A future library bump re-opens this | T-B is the tripwire; §2.4 is the containment |
| The local machine cannot reproduce the original hang | Already true (`spec.md` §3b). The widened-window counterfactual replaces it and is deterministic |

## 6. Constitution and ADR alignment

- **§IV latency:** N/A — `spec.md` §10. No leg, no figure, no claim.
- **§Testing:** RED (ADR-0144). The behaviour under test changes: T-A asserts a
  different guarantee than the test it replaces. This is not a refactor and
  characterisation is not the right colour — except for T-B, which *is* a
  characterisation and is labelled as one in its own doc comment. Ambiguity
  resolves to red; the slice is declared red.
- **§II value objects:** not engaged — no domain model in this slice.
- **ADR-0105 guards:** no new argument guard; no production method signature
  changes.
- **ADR-0049:** every new async helper in the tests keeps `CancellationToken`
  last where one is taken. The spec's whole subject is that a callee may
  *ignore* that token — ADR-0049 mandates the parameter, not that every
  transitive callee honours it, and this slice makes that distinction explicit
  rather than changing it.
- **ADR-0139:** no build-failing rule weakened; `--blame-hang` (spec 166) stays.
- **ADR-0084:** `S104`/`S138` suppressed for test projects
  (`Directory.Build.props:108`); file growth is not a break.
- **ADR-0144:** the lane may not write an ADR. If T-A cannot be made to hold
  without changing `ValidateAsync`'s control flow, **stop and report** — that
  is ADR-shaped.

## 7. Files phase 4 may touch

| Path | Story | Nature |
|---|---|---|
| `tests/StreamDistribution.Infrastructure.Tests/Auth/WhepValidatorUnreachableRealmTests.cs` | US1, US2 | the whole change |
| `src/StreamDistribution/Infrastructure/Auth/WhepAuthValidator.cs` | US3 | **comment only**, hash-proved |
| `specs/170-a-cancellation-that-cannot-be-honoured/*` | — | already committed at phase 3 |

**Nothing else.** In particular: no `.csproj`, no `Directory.Packages.props`,
no `ci.yml`, no other test file, no `Shared.*`. If the work appears to need
any of them, that is a signal the slice was mis-scoped — stop and report
rather than widening.
