# Plan 315: The create that went first

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #2598

## 1. Context and layers

- **Bounded context:** none in `src/`. The change is confined to the integration-test project,
  in the Identity test class: `tests/Integration.Tests/Identity/RegisteredClientConcurrencyIntegrationTests.cs`.
- **Domain / Application / Infrastructure / Api:** untouched. No entities, value objects,
  invariants, domain or integration events, migrations or contracts change.
- **Boundary rules:** unaffected — no project references change. `ResilienceRegistrationTests`
  (Architecture.Tests) scans `FixtureHttpClients`/`AspireFixture`; neither is edited, so it stays
  green by construction.

## 2. The edit

`RegisteredClientConcurrencyIntegrationTests.InitializeAsync` (lines 49-54 today):

- keep the existing `WaitForResourceAsync("identity", Running, cts.Token)`;
- then `using HttpClient identity = await aspire.CreateAdminClientAsync("identity", cts.Token);`
  and `await ListWebhooksAsync(identity);` — the class's existing private helper (line 349), which
  sends `GET /webhook-integrations?fabId=munich` and calls `EnsureSuccessStatusCode`;
- a short comment saying *why*: the POST is single-attempt by design (ADR-0143), so the first
  request Identity serves must be one the handler may retry; a fact run alone would otherwise send
  its create to an Identity that has served nothing.

Notes for the engineer:

- `InitializeAsync` runs once per fact (xUnit makes a class instance per fact), so this is one GET
  per fact; the token is cached by the fixture. Acceptable.
- Do not catch anything around the warm-up. A failure there must fault setup loudly (spec §2,
  scenario 3).
- Comment style: no issue/task numbers in code (CLAUDE.md); ADR numbers are fine and are the
  existing practice in this file.
- No new test class, so no shard-filter entry is needed (memory: *new test classes need a
  shard-filter entry* — not applicable).

## 3. What not to touch

`FixtureHttpClients.cs`, `AspireFixture*.cs`, `IdempotentRetry`, any resilience option, any fact
method body, `CreateAsync`, any assertion. A diff outside `InitializeAsync` is a review blocker.

## 4. Verification shape (phase 4a colour: characterisation)

- **Before:** the whole class green (8/8) on the unchanged branch — the characterisation baseline;
  plus the isolated reproduction attempt (T001), recorded whatever it shows.
- **After:** the same 8 facts green with **no fact or assertion edited**; the isolated fact green
  three times on fresh boots; Release build and `dotnet format --verify-no-changes` clean.
- **Not claimable:** a deterministic red. The defect is a cold-start race that cannot be provoked
  on demand, so the effect of the fix is evidenced by isolated-run repetitions, and if T001 never
  reproduces on a quiet machine the PR must say the fix is preventive and its effect is
  undemonstrated.

## 5. Constitution / ADR alignment

| Check | Result |
|---|---|
| ADR-0143 — only idempotent methods retried | Preserved; the only new request is a GET. |
| ADR-0142 — a retried create must not apply twice | Preserved; the POST stays single-attempt. |
| ADR-0103 — integration via the Aspire fixture | Unchanged. |
| ADR-0036 — smallest change, no speculative mechanism | One method, existing helper, no knob. |
| §IV latency | N/A. |
| §Testing (ADR-0139) | Behaviour-preserving → characterised green. |
