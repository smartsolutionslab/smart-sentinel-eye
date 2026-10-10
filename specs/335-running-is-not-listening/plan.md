# Plan — Spec 335, Running is not listening (#2777)

Phase 2 of ADR-0037. Read `spec.md` first.

## 1. Shape

One file changes production-adjacent behaviour: `tests/Integration.Tests/Fixtures/AspireFixture.cs`.
No domain, application, infrastructure or API code changes. No new ADR.

## 2. The edit

In `InitializeAsync`, immediately after the existing block:

```csharp
await app.ResourceNotifications
    .WaitForResourceAsync("identity", KnownResourceStates.Running, cts.Token)
    .ConfigureAwait(false);
```

add:

```csharp
// Running only means the process launched, not that Kestrel has bound its
// listener (see overlay-designer's wait below, and #2598/spec 315's
// per-class warm-up for the same gap in one Identity class). #2777 found a
// second Identity class with the identical exposure and the same cold-start
// POST-is-not-retried risk (ADR-0143) — closing it in the fixture, once,
// covers every Identity class rather than each discovering it separately.
await WaitForServiceHealthAsync("identity", cts.Token).ConfigureAwait(false);
```

placed before the existing `WaitForKeycloakRealmAsync`/`WaitForMediaMtxAsync`/`overlay-designer`
block, consistent with `identity`'s own wait sitting last among the per-resource `Running` waits.

No other line in `AspireFixture.cs` changes. `GatedResources` already contains `"identity"`
(line 158) — unaffected, since that array is read by `ThrowIfAnyGatedResourceDiedAsync`, a
different check than `WaitForServiceHealthAsync`.

## 3. Phase 4a colour and verification

**CHARACTERISATION (green).** No product code changes; the *fixture's* boot sequence gains one
more wait, but every existing test class's assertions, and the fixture's own public contract
(the HTTP clients it hands out, `GatedResources`, `TailedResources`), are untouched. The two
test classes this issue is about (`AbsentDeviceIdentifierIsRefusedIntegrationTests` and
`RegisteredClientConcurrencyIntegrationTests`) get **zero** diff — neither its own
`InitializeAsync` nor any fact's assertion changes.

**Local verification is blocked by this machine's known, pre-existing orphaned-container
state** (stack suffix `52dc1c67`, running ~25h, predates this branch) — the same gap this
repository has accepted and deferred to CI's `integration tests (Docker)` job on every occasion
it has come up this session (#2286, #2511, #2629). Booting a second, ephemeral `E2ETests=true`
fixture stack alongside an existing persistent dev-mode stack risks exactly the
"one machine, one Aspire stack" failure this repo's own memory records (`FailedToStart` that
reads like a code defect, not a real regression). Three sibling autonomous-lane pipelines are
also running concurrently on this machine during this delivery window, which is a second,
independent reason not to attempt a live boot here.

**What stands in for it:**
1. The change is three lines, additive, in a file whose exact sibling pattern
   (`WaitForServiceHealthAsync("overlay-designer", …)`) has been running in this fixture and
   passing in CI continuously since the commit that added it.
2. `dotnet build -c Release` on `tests/Integration.Tests` confirms the edit compiles and the
   `CancellationToken` threading is correct (the method already exists and is `private`, called
   from the same class).
3. `git diff` confirms no test file, no other fixture member, and no resilience/retry setting
   changed.
4. **This PR's own CI run — the `integration tests (Docker)` job — is the authoritative
   verification**, exactly as spec 333/#2297 recorded for its own fixture-adjacent AppHost
   change: it boots the real stack from a cold CI runner on every shard, which is the only
   environment this change is meant to protect.

## 4. Residual gate

If CI's integration job shows `identity /health was not reachable after 60 attempts` on a
normally-booting stack (i.e., a regression this change itself introduced, not a pre-existing
flake), that is a blocked outcome — stop, quote the CI log, do not raise the attempt count or
loosen the wait to reach green.
