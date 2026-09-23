# Feature Specification: The number no test ever read

**Feature Branch**: `2515-the-number-no-test-ever-read`

**Spec**: 220

**Issue**: [#2515](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2515) — follow-up to [#2284](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2284) (spec 208)

**Created**: 2026-09-23

**Status**: Draft

**Input**: "The `WhepAuthorizeRateLimiting` production default (2000/min) has no config-level guard."

---

## Why this exists

Spec 208 put a rate limiter on `/streams/authorize` — MediaMTX's anonymous
external-auth hook, the one endpoint in this system reachable with nothing but
network access. The ceiling it chose is `PermitLimit = 2000`, `Window =
00:01:00`, and that pair is not arbitrary: spec 208 §*Sizing the ceiling*
derives a worst computed minute of **≈800 authorize POSTs** (100 concurrent
viewer sessions × ≈8 retry-ladder attempts in the worst 60 s) and sets the
ceiling at **2.5× that figure**. The margin is the whole safety argument. It
exists to absorb the one factor spec 208 could not verify against a running
MediaMTX — whether a WHEP open costs one authorize POST or two — and it is what
keeps a fab-wide reconnect storm from being converted, by our own control, into
a permanent authorization outage with every tile reading "Reconnecting…".

`plan.md` §*Test-mode ceiling* said so, and said what would protect it:

> It is asserted as a configured value (a cheap unit test reading
> `appsettings.json`, so a fat-fingered edit to `200` fails the build), and its
> *derivation* is the spec.

**That test was never written.** `WhepAuthorizeRateLimiting` appears in
`src/StreamDistribution/Api/Program.cs`, `src/AppHost/AppHost.cs`,
`src/StreamDistribution/Api/appsettings.json` and one doc comment in
`tests/Integration.Tests/StreamDistribution/WhepAuthorizeRateLimitTests.cs`.
No assertion anywhere reads the production pair.

**What the existing guards do and do not catch.** #2284's phase-6 fixes added
`Ensure.That(whepAuthorizePermitLimit).AtLeast(1)` and the equivalent for the
window (`Program.cs:33-35`, ADR-0105), so a host whose config is *invalid* —
zero, negative — fails at startup instead of throwing inside the limiter on the
first real request. That is a different failure. `200` is a perfectly legal,
non-zero `int`. It binds, the host starts, every health check is green, and the
ceiling sits at **a quarter of the computed worst minute** until the first fab-
wide outage discovers it. This spec closes exactly that gap and nothing else.

**Nothing is wrong today.** The pair in `appsettings.json` on `develop` is
`2000` / `"00:01:00"`, which is what spec 208 derives. This is a guard for a
correct value, not a fix for a broken one — see §*Phase-4a colour*.

---

## Scope

**In scope.** One new test class under `tests/Architecture.Tests/` that reads
the shipped configuration from disk and asserts the production ceiling is the
number spec 208 derived.

**Out of scope, explicitly.**

- **No `src/` change of any kind.** This is a pure test addition. If a task
  edits `Program.cs`, `appsettings.json` or `AppHost.cs`, the design has been
  misread.
- **No change to the number.** 2000/00:01:00 stands; the derivation is spec
  208's and is not reopened here.
- **No new integration test, no Aspire fixture, no live stack.** The production
  ceiling is deliberately never exercised *at rate* by any test — spec 208
  §*Test-mode ceiling* states that residual and its reason (the partition key is
  the source address, which a test cannot vary; 2001 real requests inside a
  shared fixture buys a worse flake for no additional proof). This spec asserts
  the *configured value*, which is precisely the compensating control spec 208
  named. It does not attempt the thing spec 208 ruled out.
- **The integration lane's override is not governed.** `AppHost.cs:441-442`
  deliberately sets `PermitLimit=50` / `Window=00:00:10` under `isE2ETests`, and
  its own comment explains the 50 (27 known authorize consumers plus margin). A
  guard that swept every occurrence of `WhepAuthorizeRateLimiting` would fail on
  that block, which is correct as written.

---

## User Scenarios & Testing

### User Story 1 — A halved ceiling fails the build (Priority: P1)

An engineer edits `src/StreamDistribution/Api/appsettings.json` — to adjust a
log level, to add a section, to resolve a merge — and the `PermitLimit` digit
count changes by accident, or a well-meaning "let's tighten this" lowers it
without reading where 2000 came from. Today nothing notices. After this story,
the build fails, and the failure message says what the number is derived from
and where the derivation lives.

**Why P1**: this is the entire issue. It is also the whole shippable slice —
one test class, green on `develop`, red the moment the value moves.

**Independent Test**: run `dotnet test tests/Architecture.Tests` on a clean
`develop` — green. Change `PermitLimit` to `200` in the working tree, re-run —
red, with a message naming 2000, naming the ≈800/min derivation, and citing
`specs/208-a-ceiling-the-hook-never-had/spec.md` §*Sizing the ceiling*. Revert.
No Docker, no Aspire, no network; the suite runs in seconds.

**Acceptance Scenarios**:

1. **Happy path.** **Given** `src/StreamDistribution/Api/appsettings.json` as
   shipped on `develop`, **When** the guard binds it through a
   `ConfigurationBuilder` and reads `WhepAuthorizeRateLimiting:PermitLimit` and
   `:Window`, **Then** it observes `2000` and `TimeSpan.FromMinutes(1)` and
   passes.

2. **The fat-fingered edit (the case the issue names).** **Given**
   `PermitLimit` edited to `200`, **When** the guard runs, **Then** it fails,
   and the message states the observed value, the expected value, the ≈800/min
   worst-case it is 2.5× of, and the spec section that derives it.

3. **A widened window is caught too.** **Given** `Window` edited to
   `"00:10:00"` — legal, non-zero, and a 10× dilution of the same ceiling in the
   other direction — **When** the guard runs, **Then** it fails naming the
   window.

4. **A deleted section is not a silent pass.** **Given** the whole
   `WhepAuthorizeRateLimiting` object removed from the file, **When** the guard
   runs, **Then** it fails rather than passing on an absent key. A guard that
   reads nothing and passes is indistinguishable from one that holds —
   `DatabaseCommandLogLevelTests:76-81` makes the same argument about its own
   scan.

5. **A misplaced section is not a pass either.** **Given** the pair moved to
   `appsettings.Development.json` (which today carries no such section), **When**
   the guard runs, **Then** it fails: `appsettings.json` is the file that ships,
   and a Development-only override does not reach production.

6. **The integration lane stays legal.** **Given** `AppHost.cs`'s `isE2ETests`
   block still setting `50` / `00:00:10`, **When** the whole
   `Architecture.Tests` suite runs, **Then** it is green — the guard is scoped to
   the one production file and does not judge the override.

7. **Bad-request analogue — a non-numeric value.** **Given** `PermitLimit` set
   to `"two thousand"`, **When** the guard runs, **Then** it fails with a
   message that distinguishes "could not be read as a number" from "read a
   different number", rather than surfacing a raw binding exception.

---

### User Story 2 — The code fallback agrees with the shipped file (Priority: P2)

**The number lives in two places, and the issue names only one.**
`Program.cs:22,24` reads the pair with the gateway's `GetValue<T?>(…) ?? default`
idiom, and those defaults are `2000` and `TimeSpan.FromMinutes(1)` — a second
copy of the production ceiling, in code, that US1's guard does not see. A host
started without the shipped `appsettings.json` (a container image whose config
was trimmed, an environment supplying only `…__Window`, a future publish-mode
packaging change) falls back to those literals. If one carrier drifts and the
other does not, the effective ceiling depends on which file happens to be
present — the hardest class of configuration bug to diagnose, because both
values look deliberate.

**Why P2, not P1**: US1 alone closes the issue as written and ships on its own.
US2 is one additional assertion on the same guard, and if a reviewer judges the
source scan not worth its fragility, US1 stands unchanged.

**Independent Test**: with US1 green, change only `Program.cs`'s `?? 2000` to
`?? 200` and leave `appsettings.json` alone — US1 stays green (proving it is
genuinely a second carrier), US2 goes red.

**Acceptance Scenarios**:

1. **Happy path.** **Given** `Program.cs` as shipped, **When** the guard reads
   the two fallback literals, **Then** they equal the values bound from
   `appsettings.json`, and it passes.

2. **Divergence.** **Given** `Program.cs`'s `?? 2000` changed to `?? 200` with
   `appsettings.json` untouched, **When** the guard runs, **Then** it fails
   naming both carriers and both values.

3. **The fallback disappearing is a failure, not a pass.** **Given** the `??
   2000` removed (leaving a nullable dereference or a `0` default), **When** the
   guard runs, **Then** it fails rather than matching nothing and passing.

---

### Auth / authorization scenario

**N/A, and deliberately so.** The endpoint this ceiling protects is anonymous by
design — it is MediaMTX's external-auth hook, and spec 208 §*Partition key*
covers the authorization reasoning. This spec adds no endpoint, no scope, no
route, and no code that runs in a hosted process. There is no auth surface to
write a scenario against. The security relevance is entirely indirect: the
guard defends the sizing of an availability control on an anonymous endpoint.

---

## Independent end-to-end test procedure

Runnable by a reviewer with no running stack, on any machine, in under a minute.

```sh
# 1. Green as shipped.
cd D:/Github/sse-2515
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~WhepAuthorizeCeiling"

# 2. Prove it can fail — the counterfactual. Construct exactly what the issue
#    describes: a legal, non-zero, wrong value.
#    Edit src/StreamDistribution/Api/appsettings.json: "PermitLimit": 2000 -> 200
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~WhepAuthorizeCeiling"
#    EXPECT: red, message naming 200, 2000, the ≈800/min derivation, and
#    specs/208-a-ceiling-the-hook-never-had/spec.md.

# 3. Restore, confirm green again.
git checkout -- src/StreamDistribution/Api/appsettings.json
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~WhepAuthorizeCeiling"
```

Step 2 is not optional. This repository has filed guards that could not fail the
thing they claimed to catch; a guard is proved by counterfactual, not by a green
run. Phase 5 quotes the red output from step 2 verbatim.

**A note for whoever runs step 2 on Windows**: a file restored by
`git checkout --` keeps its old timestamp, so MSBuild may skip the rebuild and
step 3 can still show the failure. If it does, touch the file or build with
`--no-incremental` before concluding the guard is broken.

---

## Requirements

| ID | Requirement |
|---|---|
| **FR-001** | A test asserts `WhepAuthorizeRateLimiting:PermitLimit` in `src/StreamDistribution/Api/appsettings.json` is `2000`. |
| **FR-002** | The same test asserts `:Window` is `00:01:00` (one minute), compared as a `TimeSpan` rather than as a string, so `"00:01:00.000"` and `"0:01:00"` are not spurious failures. |
| **FR-003** | The configuration is read by **binding the file** through `ConfigurationBuilder`/`GetValue<T?>` — the same mechanism `Program.cs:21-24` uses — not by regex over JSON text. A guard that reads the file differently from the host can pass on a file the host reads differently. |
| **FR-004** | An absent section, an absent key, or a value that will not parse fails the test with a message that says which of those happened. |
| **FR-005** | The failure message carries the **derivation**, not just the expected number: the ≈800/min worst computed minute, the 2.5× margin, and a citation of `specs/208-a-ceiling-the-hook-never-had/spec.md` §*Sizing the ceiling*. A guard that only says "expected 2000, got 200" invites the next reader to edit the guard. |
| **FR-006** | Scope is the one production file. `AppHost.cs`'s `isE2ETests` override and `appsettings.Development.json` are not judged. |
| **FR-007** | *(US2)* The test asserts `Program.cs`'s `?? 2000` / `?? TimeSpan.FromMinutes(1)` fallbacks agree with the bound values, and fails if either fallback is absent. |
| **NFR-001** | No Docker, no Aspire fixture, no network, no `AspireFixture` (ADR-0103). The test runs in the existing fast `Architecture.Tests` lane. |
| **NFR-002** | No new NuGet package. `Microsoft.Extensions.Configuration.Json` is already reachable in `Architecture.Tests` — `DatabaseCommandLogLevelTests` binds JSON there today. `Directory.Packages.props` is not touched. |
| **NFR-003** | No new `RepositoryRoot()` walk. Spec 190 extracted `RepositorySource.Root()` / `.RelativePath()` for exactly this; a 32nd private copy is a regression against that spec. |
| **NFR-004** | Sentence-style test names with underscores (ADR-0053); xUnit + Shouldly (ADR-0052). |

---

## Locked tech choices this spec relies on

| Concern | Choice | Authority |
|---|---|---|
| Workflow | Seven phases, gated | ADR-0037 |
| Test framework | xUnit + Shouldly | ADR-0052 |
| Test naming | Sentence-style with underscores | ADR-0053 |
| Integration testing | Aspire fixture, no Testcontainers — **and neither is used here** | ADR-0103 |
| Argument guards | `Ensure.That(...)` — the existing `Program.cs:33-35` guards this spec complements | ADR-0105 |
| Observability sink | One OTLP sink; the throttle log this ceiling governs | ADR-0118 |
| Code metrics | Advisory (300 LOC/file, 30 LOC/method) | ADR-0084 |
| Autonomous lane / phase-4a colour | Two colours, ambiguity resolves to red | ADR-0144 |

**No new ADR is needed.** Every decision here is an application of ADR-0052,
ADR-0053 and ADR-0103 to a test that adds no architecture. If a task finds
itself proposing one, that is a signal the scope has grown past this spec.

---

## Phase-4a colour: **characterisation, observed green**

The production value in `appsettings.json` is **already correct** — verified on
`origin/develop` at commit `8ce9b5fa`: `PermitLimit: 2000`, `Window:
"00:01:00"`, exactly spec 208's derived pair. So the guard is written against
behaviour that already holds, captured **green**, and must pass **unmodified**.

This is the colour ADR-0144 calls behaviour-preserving, with one honest caveat
worth writing down rather than eliding: a characterisation test normally
protects code a later task will change, and nothing here will change. That makes
it closer to a *new guard over an existing invariant* — new test, no new
behaviour. A red-first reading would be wrong, because there is no defect to
observe failing.

**The evidence obligation therefore moves to the counterfactual** (§*Independent
end-to-end test procedure*, step 2). Green-on-arrival proves only that the
number matches; it does not prove the guard can see a number that does not. Both
outputs — green as shipped, red against the injected `200` — are quoted verbatim
in the PR body. That, not the colour label, is what makes this guard worth
having.

**If the value in `appsettings.json` does not match at implementation time**,
that is a different and more urgent finding: the shipped ceiling has already
drifted from its derivation and nobody noticed. Stop, do not adjust the expected
value to match reality, and raise it.

---

## Latency budget impact

**N/A.** No code in this change runs in a hosted process. The control it guards
sits on the WHEP-open path, which is *upstream* of the six legs in constitution
§IV — an authorize POST happens before a session exists, so it precedes
`Camera → SFU` rather than occupying any leg. Nothing in the 800 ms budget is
touched, eroded or measured here.

Worth stating for the record all the same: the ceiling's *failure mode* is not a
latency cost but an availability one. A ceiling set too low does not slow the
path down; it removes it, refusing the authorize POST that every reconnecting
tile depends on. That is why the sizing margin is defended by a test rather than
by a comment.

---

## Assumptions and open questions

1. **Assumed**: `appsettings.json` is the production carrier. `AppHost.cs`
   overrides it only under `isE2ETests`, and publish-mode deployment supplies
   nothing that would replace it. Verified by grep across `src/` — the four
   references listed in §*Why this exists* are all of them.
2. **Assumed**: `tests/Architecture.Tests` is the right home. It already holds
   config-reading guards that bind files from disk
   (`DatabaseCommandLogLevelTests`), source-scanning guards over `AppHost.cs`
   (`ContainerImagePinTests`), and a shared reader for the repository root
   (`RepositorySource`). No new project, no new pattern.
3. **Marked guess**: US2's fallback assertion reads `Program.cs` as *source
   text*, because top-level statements give nothing reflectable. That is the
   weaker half of this spec and is why it is P2. `ContainerImagePinTests` sets
   the precedent for regexing a `.cs` file, and its own doc comment is candid
   about what a literal scan cannot see.
4. **No `[NEEDS CLARIFICATION]` remains.** The issue specifies the fix shape,
   spec 208 specifies the number, and both were read rather than assumed.
