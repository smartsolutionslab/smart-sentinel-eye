# Spec 386: Named-fab list reads refused end to end on /devices and /webhook-integrations

**Feature Branch**: `test/3001-crossfab-devices-webhook-403` (cut from `origin/develop` at `bd04dd8e`)

**Created**: 2026-10-16

**Status**: Draft for Phase 1-3 review. No `[NEEDS CLARIFICATION]` remains: the shape of the work
was decided by the owner on #3001 (2026-10-16, delegated decision).

**Input**: Issue [#3001](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3001), found
by the security review of #2975 (spec 382). Refs spec 183 (#2281, the class's origin), spec 382
(in-process characterisation of the three list routes), spec 367.

**Lane.** Autonomous (ADR-0144). **Tests only; no production change.** Phase 4a colour is
**characterisation, observed green** (the behaviour exists and is unchanged; the deliverable is the
missing end-to-end proof of it). Test-writer delivers; **no engineer pass**. Security-relevant
(fab tenancy) but nothing under `src/` changes, so Phase 6 is `/code-review` plus a
security-reviewer read of the assertions only.

**Spec number.** 386. Checked 2026-10-16 after `git fetch`: highest spec on `origin/develop` is
383; 384 (#2998) and 385 (#2991) are claimed by sibling branches. No local or remote head carries
`specs/386-*`. Re-check before the PR is opened.

**ADRs.** ADR-0114 (fab authorisation decision table: a named fab the caller does not hold is
refused, 403 `RESOURCE_FAB_NOT_AUTHORIZED`), ADR-0103 (integration against the real Aspire stack,
no Testcontainers), ADR-0053 (sentence-style test names), ADR-0144 (lane, Phase 4a colours). **No
new ADR needed**: no decision is made, an existing one is tested.

## 1. Problem

`CrossFabListIntegrationTests` I5/I6 prove the explicitly-named-fab branch end to end, with the real
`DefaultFabAuthorizationGuard` and the global 403 mapping, only on `GET /kiosks`. `GET /devices` and
`GET /webhook-integrations` share the branch (`IdentityFabResolution.ResolveReadFabsAsync` ->
`FabResolution.ResolveForReadAsync` -> `fabGuard.EnsureAccessAsync`), but each route makes its own
call into it. If one of them stopped passing the named fab, a munich operator holding
`sse.identity.devices.read` or `sse.webhooks.write` could list dresden's devices or webhook clients.
Today that is covered piece by piece (spec 382's in-process tests with a mocked guard asserting the
raw `FabAuthorizationException`; `FabResolutionTests` for the real guard; the global mapping), not
end to end.

## 2. User story (P1, the only one)

As a security reviewer, I want the named-fab refusal and the named-own-fab narrowing proven end to
end on every Identity list route, so that a regression in any one route's call fails CI rather than
reaching a deployment.

### Acceptance scenarios

```gherkin
Scenario: foreign fab named on /devices is refused (auth / conflict with fab membership)
  Given admin@munich.test, whose only fab group is /fabs/munich
  When they GET /devices?fabId=dresden
  Then the response is 403
  And the problem title is "RESOURCE_FAB_NOT_AUTHORIZED"

Scenario: foreign fab named on /webhook-integrations is refused
  Given admin@munich.test
  When they GET /webhook-integrations?fabId=dresden
  Then the response is 403
  And the problem title is "RESOURCE_FAB_NOT_AUTHORIZED"

Scenario: own fab named on /devices narrows to that fab (happy)
  Given a device registered in munich by admin@munich.test and one in dresden by op-dresden@dresden.test
  When admin@munich.test GETs /devices?fabId=munich
  Then the response is 200
  And it contains the munich device's clientId and not the dresden one's
  And the distinct fab values are exactly ["munich"]

Scenario: own fab named on /webhook-integrations narrows to that fab (happy)
  (same shape, with webhook integrations created via POST /webhook-integrations/{name}/rotate)
```

Bad-request (unusable `fabId`, 400 `DEVICE_INVALID_INPUT` / `WEBHOOK_INVALID_INPUT`) and
unauthenticated (401) are **out of scope**: spec 382's in-process tests pin the 400 bodies, and the
401 is the shared scope policy, not this branch.

### Independent test procedure

CI job `integration-shards`, shard 2 (the class is already in
`tests/Integration.Tests/ci-shards/shard-2.filter`). Locally only with Docker up:
`dotnet test tests/Integration.Tests --filter "FullyQualifiedName~CrossFabListIntegrationTests"`.

## 3. Requirements

- **FR-001** Four new `[Fact]`s in the existing `CrossFabListIntegrationTests`, mirroring I5 and I6
  per route (names in plan §2). No new test class, no new helper, no fixture change.
- **FR-002** The 403 facts assert status and problem `title`, exactly as I5 does.
- **FR-003** The 200 facts arrange their own munich and dresden rows (never rely on other tests'
  rows) and assert: munich id present, dresden id absent, distinct fabs `["munich"]`.
- **FR-004** The class `<summary>` paragraph that says I5/I6 are "written once, against `/kiosks`"
  is updated so it does not contradict the file.
- **FR-005** No file under `src/` changes. No shard filter changes.

## 4. Latency budget

N/A. No code on the event-to-overlay path changes; the Identity list routes are not on it.

## 5. Assumptions (explicit)

- **A-1** These tests have never run: Docker is not available to the lane locally, so CI shard 2 is
  their first observation. The PR says so.
- **A-2** admin@munich.test holds `sse.identity.devices.read` and `sse.webhooks.write` already: I2
  and I3 list both routes with that caller today.
