# Spec 227 — One copy of the failure diagnosis

**Issue:** #2294 — *Four Integration.Tests helpers are copied around fifty-five times across
seven context folders*
**Branch:** `refactor/2294-dedupe-integration-test-helpers`
**ADRs:** 0037 (phases and gates), 0144 (the autonomous lane; the two colours of phase 4a),
0036 (smallest change), 0103 (integration tests run against the Aspire fixture, no
Testcontainers), 0052 (xUnit + Shouldly), 0049 (no `ConfigureAwait`), 0084 (code metrics,
advisory), 0109 (`[P]` disjoint files), 0028 (GitFlow), 0086 (no `Co-Authored-By`).
**Precedent:** spec 137 (#2182) — characterise green from CI trx, assertion-invariant hash,
covering tests unmodified.

**Spec number 227.** `specs/225-the-render-leg-ci-never-reads` is the tip of `origin/develop`;
**226 is claimed** by the unmerged local branch `2504-dead-entries-no-contract-needs`
(commit `f5231aac`, `specs/226-the-fallback-no-contract-reaches/`), found with
`git log --all -- 'specs/226*'`. 226 + 1 = **227**. Re-check before merging if 226's PR
has not landed.

---

## Phase 1 verified the issue. Its premise holds; its counts are low, and one of its four
## "helpers" is really one helper under three names

### The counts, re-measured on `b8c14eb9` (2026-09-23)

Defining files under `tests/Integration.Tests/`, counted with
`grep -rlE "(private|internal|public)[^=(]*\b<Name>\(" --include=*.cs`:

| helper | issue says | today | same helper in every file? |
|---|---|---|---|
| `UniqueName` | 20 | **22** (one is already shared: `Fixtures/VariableRequests.cs:21`) | **No** — prefixes, lengths, `NewGuid` vs `CreateVersion7`, with/without a `prefix` parameter all differ per file. Deliberately local, not drift. |
| `ClientFor` | 15 | **20** | **No** — sync/async, `(username)` vs `(resourceName, jwt)`, different resources. |
| `BodyAsync` | 11 | **13** | **No — two different helpers share the name.** 10 are the diagnosis below; 3 are body-only (see finding B). |
| `DiagnoseAsync` | 9 | **9** | **Yes** — see finding A. |

The issue is not stale: nothing has consolidated any of the four since 2026-09-13. The
population has grown.

### Finding A — the nine `DiagnoseAsync` copies are behaviourally identical

Every copy reads the body, then the resource's log tail, and returns

```
"body: " + body + NL + "<resource> log:" + NL + aspire.RecentLogs("<resource>")
```

with `<resource>` the **same literal** in the label and the `RecentLogs` argument in every
file. Four spellings exist (two-statement block; expression body with `+`; interpolation
split across `+`; `TokenAttribution`'s `+ aspire.RecentLogs(...)` tail) — all evaluate
`ReadAsStringAsync()` before `RecentLogs(...)` and produce the same string. None has
diverged.

| File | resource | call sites |
|---|---|---|
| `Automation/RuleLifecycleIntegrationTests.cs:166` | `automation` | 6 |
| `Automation/CrossFabEvaluationIntegrationTests.cs:207` | `automation` | 4 |
| `EventIngestion/WebhookIntegrationConcurrencyIntegrationTests.cs:197` | `event-ingestion` | 1 |
| `EventIngestion/WebhookRevocationRefusesDeliveryIntegrationTests.cs:260` | `event-ingestion` | 7 |
| `Identity/FabGroupClaimIntegrationTests.cs:203` | `automation` | 4 |
| `Identity/RegisteredClientConcurrencyIntegrationTests.cs:275` | `identity` | 4 |
| `Identity/TokenAttributionIntegrationTests.cs:102` | `overlay-designer` | 1 |
| `LayoutComposition/LayoutNameUniquenessIntegrationTests.cs:284` | `layout-composition` | 5 |
| `OverlayDesigner/OverlayNameUniquenessIntegrationTests.cs:305` | `overlay-designer` | 6 |

38 call sites, every one of them the message argument of a `ShouldBe` on a status code.
The two Identity-folder files that diagnose another context's log are **correct**, not
drift: `FabGroupClaim` calls `automation`, `TokenAttribution` calls `overlay-designer`.

### Finding B — the same helper exists thirteen more times under other names

The diagnosis shape above is also written as:

- **`BodyAsync`, log-bearing — 10 files:** `Automation/RuleFabResolution`,
  `CameraCatalog/CameraFabResolution`, `EventIngestion/{DeadLetterFabScoping,
  EventFabScoping, EventTypeRegistry, ManualIngestFabScoping, MissingPayloadIsRefused}`,
  `LayoutComposition/LayoutFabScoping`, `StreamDistribution/StreamFabScoping`,
  `SystemVariables/VariableFabResolution`.
- **`Diagnose` (no `Async`) — 3 files:** `EventIngestion/EventTypeRegistryAuthorization`,
  `EventIngestion/EventTypeRegistryIdempotency` (both via a `ResourceName` constant), and
  `Identity/ConsoleScopeGrant` (resource passed per call).

And `BodyAsync` also names a **different, body-only** helper in 3 files, which **has
diverged**: `SystemVariables/VariableReadScope` and `SystemVariables/ReverseIndexSeedCredential`
return `"body: " + body` with no log; `Automation/RuleRead` returns the raw body with no
`body:` prefix. Whether those three *should* carry a log is a behaviour question, not a
dedup — **flagged, not normalised** (US-3).

So the real population of the diagnosis helper is **22 copies under three names**, not 9.
This spec delivers the 9 named `DiagnoseAsync` (the issue's recommended first slice) and
builds the shared home the other 13 will reuse; US-2 carries them.

### Finding C — a guard would silently lose nine call sites

`tests/Architecture.Tests/LogTailCoverageTests.cs` reads every `RecentLogs("literal")`
under `tests/Integration.Tests` and fails if the literal is not in
`AspireFixture.TailedResources` (#2053). A **variable** argument is skipped by design
(`:42–46`). Moving the nine bodies into one shared method turns nine literal call sites into
one variable one: the guard stays green and checks 19 sites instead of 28. That is a gate
weakening (ADR-0144) reached by a pure refactor — exactly the route the lane forbids. The
plan keeps the checked population at **28** by keeping the literal at each call site and
widening the guard's pattern to read it there (plan §Guard).

### Finding D — the "other three" in the issue body

`RepositoryRoot` (17), `Relative` / `ReadRepositoryFile` (9 each) are evidence for #2257,
not this issue. Not touched here.

---

## User stories

### US-1 (P1) — Nine `DiagnoseAsync` copies, one body. *This is the whole delivery.*

**As** the engineer reading a red integration run in CI,
**I want** the "body + service log" failure message built in exactly one place,
**so that** a change to how a failure is diagnosed (tail length, a new field, a redaction)
cannot land in one copy and not the others — the #2183 / #2274 shape, where a diverged copy
hides the evidence of a failure.

Independently shippable and observable: the 49 covering tests run in CI's four
`integration tests (Docker)` shards and their outcomes are readable in the uploaded trx.

### US-2 (P2) — *Not delivered.* Point the 13 other-named diagnosis copies at the same home.

10 log-bearing `BodyAsync` + 3 `Diagnose`. Also behaviour-preserving, but it **renames**
call sites inside assertion statements (`BodyAsync(x)` → `DiagnoseAsync(x)`), or keeps
per-file forwarders under the old names — a naming decision best taken on its own diff.
Follow-up issue.

### US-3 (P3) — *Not delivered.* Decide whether the 3 body-only `BodyAsync` helpers should
carry a log.

Behaviour-**changing** (a failure message gains content). Its own issue.

`UniqueName` and `ClientFor` are **not** candidates for "one shared home": their copies
differ by design (finding table). The issue's done-condition for them should be re-scoped on
#2294 rather than inherited.

---

## Acceptance scenarios (US-1)

```gherkin
Scenario: Happy path — the covering suite passes, asserting the same things
  Given the 49 tests in the nine files passed in develop run 35894668870
  When the nine DiagnoseAsync bodies are replaced by forwarders to one shared method
  Then all 49 report outcome="Passed" in this PR's four integration trx shards
  And the assertion hash of the nine files is unchanged (572947b8…)

Scenario: Conflict — the shared helper must bind each file to the resource it bound before
  Given each file names one resource for its log (the binding inventory, 9 pairs)
  When the change lands
  Then the (file, resource) inventory is byte-identical (dbd6ea60…)

Scenario: Bad request — an asserted value or message moves
  Given the baseline assertion text captured at phase 3 (136 statements, 38 of them
        carrying await DiagnoseAsync(...))
  When the extractor is re-run after the change
  Then it is byte-identical; if not, the change is blocked, not adjusted

Scenario: The log-tail guard keeps its reach
  Given LogTailCoverageTests checks 28 literal log requests today
  When the nine literals move from RecentLogs("x") to the forwarders' DiagnoseAsync("x", …)
  Then the guard still checks 28 (file, resource) pairs (273a5587…)
  And an untailed resource written into one forwarder turns the guard red (counterfactual)

Scenario: Auth — nothing to authorise
  Then no production assembly, scope, fab check or Idempotency-Key is touched
```

**Auth note.** Test-only code. No `sse.*` scope, fab authorisation, token or secret is
involved.

---

## Independent end-to-end test procedure

**Do not boot the Aspire fixture on this machine.** C: is at **95% (13 GB free)** on
2026-09-23; the fixture's containers live in the Docker vhdx on C:, and the known failure
mode is the engine ceasing to answer until a GUI restart. The evidence route is CI's own
trx, as in spec 137.

**Baseline captured at phase 1 — run `35894668870`, `develop`, SHA
`b8c14eb9a6f781f787fcf4a0f236d524cb43f951` (this branch's base), 2026-09-23T17:18:14Z, all
jobs green.** Integration is now **sharded four ways** (spec 218), so the artifact is
`integration-test-results-{1..4}-of-4`, not spec 137's single `integration-test-results`:

```
<Counters total="156" … passed="156" failed="0" …/>
<Counters total="157" … passed="157" failed="0" …/>
<Counters total="158" … passed="158" failed="0" …/>
<Counters total="169" … passed="169" failed="0" …/>      (640 passed, 0 failed)
```

All **49** tests of the nine files `outcome="Passed"`: CrossFabEvaluation 10,
RuleLifecycle 6, WebhookIntegrationConcurrency 4, WebhookRevocationRefusesDelivery 3,
FabGroupClaim 6, RegisteredClientConcurrency 7, TokenAttribution 3,
LayoutNameUniqueness 5, OverlayNameUniqueness 5.

Re-runnable verbatim (artifacts expire **2026-10-07**):

```sh
D=/tmp/trx; rm -rf $D && mkdir -p $D
gh run download <RUN_ID> -p 'integration-test-results-*' -D $D
P='RuleLifecycleIntegrationTests|CrossFabEvaluationIntegrationTests|WebhookIntegrationConcurrencyIntegrationTests|WebhookRevocationRefusesDeliveryIntegrationTests|FabGroupClaimIntegrationTests|RegisteredClientConcurrencyIntegrationTests|TokenAttributionIntegrationTests|LayoutNameUniquenessIntegrationTests|OverlayNameUniquenessIntegrationTests'
cat $(find $D -name '*.trx') \
| grep -oE "<UnitTestResult[^>]*testName=\"[^\"]*($P)\.[^\"]*\"[^>]*outcome=\"[^\"]*\"" \
| sed -E 's/.*testName="([^"]*)".*outcome="([^"]*)".*/\2\t\1/' | sort -k2
for t in $(find $D -name '*.trx'); do grep -o '<Counters[^>]*/>' $t; done
```

The **after** is the same against this PR's own run: 49 `Passed`, four `failed="0"`.

**What this does not prove.** `DiagnoseAsync` runs on every call (its result is an eager
argument to `ShouldBe`), so a green trx proves the shared method executes 38 times without
throwing. It does **not** prove the message *text* is unchanged — that text is only
printed on a failure, and no test reads it. Text equivalence rests on the source-level
argument in finding A plus the plan's single shared body; the binding inventory proves each
file still names its resource. Pinning the message text with a test would be a
strengthening, and a strengthening is a second issue (ADR-0144).

---

## Locked tech choices

xUnit + Shouldly (ADR-0052), Aspire fixture, no Testcontainers (ADR-0103), no
`ConfigureAwait` (ADR-0049, matching all nine copies), ≤ 300 LOC per file advisory
(ADR-0084). Nothing new is introduced: one method moves onto the existing partial
`AspireFixture`, beside `RecentLogs`, following the `AspireFixture.Auth.cs` /
`AspireFixture.Db.cs` split.

**No ADR is needed.** No architecture, dependency, boundary or runtime resource changes.
The one judgement call — widening `LogTailCoverageTests`' pattern rather than letting it
lose nine sites — keeps an existing guard at its existing strength; it decides nothing new.

## Latency-budget impact

**N/A.** Test-only. No production assembly, nothing on the event-to-overlay path, no leg of
constitution §IV.
