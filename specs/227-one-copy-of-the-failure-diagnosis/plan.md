# Plan 227 — One copy of the failure diagnosis

**Spec:** [`spec.md`](spec.md) · **Issue:** #2294 · **Branch:**
`refactor/2294-dedupe-integration-test-helpers`

## Bounded context and layers

**None.** `tests/Integration.Tests/` (nine test files + one new fixture partial) and one
guard in `tests/Architecture.Tests/`. No `Domain`/`Application`/`Infrastructure`/`Api`
assembly, no `Shared.Contracts` message, no project reference moves. No entities, value
objects, invariants or events; constitution §II does not bind test helpers. NetArchTest is
unaffected.

## Design

### 1. The shared home — new file `tests/Integration.Tests/Fixtures/AspireFixture.Diagnostics.cs`

A third partial of `AspireFixture`, beside `RecentLogs` (which it calls) and matching the
existing `AspireFixture.Auth.cs` / `AspireFixture.Db.cs` split. `AspireFixture.cs` is
already 1449 lines; it is not grown.

```csharp
namespace SmartSentinelEye.Integration.Tests.Fixtures;

public sealed partial class AspireFixture
{
    /// <summary>(one doc comment: why — a bare status tells a reader nothing and CI has
    /// no other route to the service's stack trace; merge the best of the nine.)</summary>
    public async Task<string> DiagnoseAsync(string resourceName, HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        return $"body: {body}{Environment.NewLine}{resourceName} log:{Environment.NewLine}{RecentLogs(resourceName)}";
    }
}
```

Load-bearing details:

- **Resource first, response second.** So the resource literal is the *first* argument at
  every call site, which is what the guard's pattern reads (§3). Not
  `ConsoleScopeGrant`'s `(response, resourceName)` order — that file is US-2.
- **Body read before `RecentLogs`.** All nine copies evaluate in that order; keep it.
- **No `ConfigureAwait`** (ADR-0049; the nine copies have none — `AspireFixture.Db.cs`
  uses it, do not copy that). **No argument guard**: none of the nine has one and
  `RecentLogs` has none; adding one is a behaviour change, however small.
- `lines` default (120) inherited from `RecentLogs`, as every copy did.

### 2. The nine files — body becomes a one-line forwarder, call sites untouched

In each file the private `DiagnoseAsync` keeps its **name, signature and position** and its
body becomes a forwarder that binds the file's resource:

```csharp
private Task<string> DiagnoseAsync(HttpResponseMessage response) =>
    aspire.DiagnoseAsync("automation", response);
```

| File | literal |
|---|---|
| `Automation/RuleLifecycleIntegrationTests.cs` | `"automation"` |
| `Automation/CrossFabEvaluationIntegrationTests.cs` | `"automation"` |
| `EventIngestion/WebhookIntegrationConcurrencyIntegrationTests.cs` | `"event-ingestion"` |
| `EventIngestion/WebhookRevocationRefusesDeliveryIntegrationTests.cs` | `"event-ingestion"` |
| `Identity/FabGroupClaimIntegrationTests.cs` | `"automation"` |
| `Identity/RegisteredClientConcurrencyIntegrationTests.cs` | `"identity"` |
| `Identity/TokenAttributionIntegrationTests.cs` | `"overlay-designer"` |
| `LayoutComposition/LayoutNameUniquenessIntegrationTests.cs` | `"layout-composition"` |
| `OverlayDesigner/OverlayNameUniquenessIntegrationTests.cs` | `"overlay-designer"` |

**Why a forwarder, not rewritten call sites.** The 38 call sites all sit inside assertion
statements (`x.StatusCode.ShouldBe(…, await DiagnoseAsync(x))`). Rewriting them to
`aspire.DiagnoseAsync("automation", x)` would change 38 assertion statements and force the
invariance extractor to normalise that text away — and a normalisation is exactly the hole
spec 137 had to plug with a separate task (its T007), because a wrong resource literal
would pass through it. A forwarder leaves **every assertion byte-identical**, keeps the
resource bound once per file (where it is genuinely per-file), and moves all *logic* — the
part that can drift — to one place.

The per-copy doc comments on the six documented forwarders are deleted (the reasoning now
lives on the shared method). `RuleLifecycleIntegrationTests.cs:143`'s comment mentioning
"DiagnoseAsync's body dump" stays — it is still true.

`Async` suffix on a non-`async` forwarder returning `Task<string>` is correct (it returns a
task); `async`/`await` elision in a one-line forwarder does not change exception timing
observably here — the shared method is itself `async`, so any fault still surfaces through
the awaited task.

### 3. Guard — `tests/Architecture.Tests/LogTailCoverageTests.cs`, one pattern widened

Today (`:47–49`):

```csharp
private static readonly Regex LiteralRequest = new(
    @"RecentLogs\(\s*""([^""]+)""",
```

becomes

```csharp
private static readonly Regex LiteralRequest = new(
    @"\b(?:RecentLogs|DiagnoseAsync)\(\s*""([^""]+)""",
```

and its `<summary>` (`:42–46`) gains one sentence: a `DiagnoseAsync("x", …)` request asks
for `x`'s log exactly as `RecentLogs("x")` does, so it is read the same way.

- The forwarders' own declarations — `DiagnoseAsync(HttpResponseMessage response)` — have
  no quote after `(` and are not matched. The shared method's `RecentLogs(resourceName)`
  is a variable and is skipped, as `ConsoleScopeGrant`'s already is.
- **The guard's three assertion statements do not change** (hash below). The `Explain`
  text still says "RecentLogs call sites"; it points at `path:line`, which is enough. Do
  not edit it — that would move an assertion message.
- **Invariant:** the multiset of (file, resource) pairs the guard reads over
  `tests/Integration.Tests` is **28 pairs, identical before and after**. Before the change
  the widened pattern and the old one read the same 28 (no `DiagnoseAsync("` exists yet),
  so the pattern change is provably inert until the forwarders land, and the forwarders
  land exactly the 9 pairs the removed `RecentLogs` lines held.

Also `AspireFixture.cs:106–108` says "every `RecentLogs` call site must name a resource
tailed here" — leave it; it remains true.

## Messaging

N/A.

## Boundary rules

Unchanged. No project reference is added; `Architecture.Tests` still reads
`Integration.Tests` from disk, never by reference (`LogTailCoverageTests.cs:29–35`).

## Phase 4a — colour, procedure and the mechanical checks

**Declared colour: behaviour-preserving → characterisation, observed green.** No
production code; no assertion changes; the nine copies were verified identical (spec
finding A). Ambiguity candidates resolved: the guard widening keeps a gate at its current
reach (28 → 28) rather than adding one — it is part of preserving, not a strengthening; the
body-only `BodyAsync` divergence is out (US-3).

### Characterisation run — CI trx, both ends

**Before:** run `35894668870`, SHA `b8c14eb9…` (the branch base), 49/49 `Passed`,
640 passed / 0 failed across four shards — spec §Independent e2e procedure. **After:** the
same commands against this PR's run. **Do not boot the fixture locally** (C: at 95%).

Locally, the fast evidence is: Release build clean, and
`dotnet test tests/Architecture.Tests --filter FullyQualifiedName~LogTailCoverageTests`
green (no Docker).

### Invariance checks — all captured on `b8c14eb9` at phase 3

`F` = the nine files, in the table order of §2, each prefixed `tests/Integration.Tests/`.

```sh
A=specs/227-one-copy-of-the-failure-diagnosis/assertions.sh

# (1) assertion text of the nine files — 136 statements, 38 carry await DiagnoseAsync(
sh $A $F | sha256sum
# 572947b8782b72328b8ebf131d38b3f50b8d1d4d4b2a1d871381b48a4059ee1b

# (2) test-name inventory — 49 names
grep -hA3 -E '\[(Fact|Theory)' $F | grep -oE 'public async Task [A-Za-z_]+' \
  | sed 's/public async Task //' | sort | sha256sum
# 5402f58f092f978b04e9ad63bfa34da1cf7ac07876d0e36b17889e513fb9eb56

# (3) binding inventory — which resource each of the nine files diagnoses with (9 pairs)
grep -HoE '\b(RecentLogs|DiagnoseAsync)\("[^"]+"' $F \
  | sed -E 's/:(RecentLogs|DiagnoseAsync)\("/\t/; s/"$//' | sort | sha256sum
# dbd6ea6076fb63fc52fd75b97c1532a89bd3ddcf8a5450fa68f6a9f7de657120

# (4) guard reach — (file, resource) pairs over the whole tree (28 pairs)
grep -rHoE --include=*.cs '\b(RecentLogs|DiagnoseAsync)\(\s*"[^"]+"' tests/Integration.Tests \
  | grep -v '/obj/\|/bin/' | sed -E 's/:(RecentLogs|DiagnoseAsync)\(\s*"/\t/; s/"$//' \
  | sort | sha256sum
# 273a5587e62bc4d402e3ffebad7a6be2a69d1b6593a078683792e569224fba49

# (5) the guard's own assertions — 3 statements
sh $A tests/Architecture.Tests/LogTailCoverageTests.cs | sha256sum
# 7c5336da344bcd81c2b7481206c419402c0279ecf392516db8f181d565b3acc4
```

`assertions.sh` differs from spec 137's in one way: **no normalisation** (137 stripped
`RealmProbe.`). Nothing here needs one, so there is no hole to plug. The 136 baseline
statements are in [`baseline-assertions.txt`](baseline-assertions.txt).

**What the checks do not catch.** (1) is source-text only — it is blind to a forwarder that
binds the wrong resource, which is why (3) exists; (3) is blind to the shared body's
*format* changing, which is why the body is dictated verbatim in §1 and must be read in
review against finding A. None of them is runtime evidence; the trx is.

**Counterfactual for the guard (mandatory, memory: prove a guard by counterfactual).** With
the change in place, temporarily set one forwarder's literal to `"no-such-resource"`, run
`LogTailCoverageTests` → it must fail naming that file and `'no-such-resource'`. Revert;
green. Quote the red output in the PR. Without the widened pattern this edit passes
silently — which is finding C.

An engineer who finds any hash changed **stops and reports**. Editing an assertion to
restore a hash, editing `assertions.sh`, or narrowing the guard are gate weakenings under
ADR-0144.

## Risks

| Risk | Mitigation |
|---|---|
| A forwarder binds the wrong resource | Check (3); the table in §2 |
| The guard silently loses 9 sites | §3 widening; check (4); counterfactual |
| The shared format drifts from the copies | Body dictated in §1; reviewer compares to spec finding A |
| An assertion moves | Check (1) |
| Local fixture boot kills Docker on C: | Prohibited; CI trx is the evidence |
| Baseline artifact expires | Retention to 2026-10-07; re-capture from newest green `develop` run otherwise |
| Spec number 226 lands first under a different name / 227 taken | Re-check `git log --all -- 'specs/227*'` before merge |

## Engineer

**Backend.** C# test code in `tests/Integration.Tests/` and one regex in
`tests/Architecture.Tests/`. No frontend, AppHost, CI, Docker or realm file. The evidence
is a CI artifact (`gh run download`; `jq` is not installed — use `gh --json … -q`).
