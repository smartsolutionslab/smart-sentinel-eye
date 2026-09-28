# Plan 286: The client nobody signs in with

**Spec:** `spec.md` · **Issue:** #2488 · **Lane:** autonomous (ADR-0144)
**Phase-4a colour:** **split, by design.** Two new facts are **red** (behaviour-changing: a
client that authenticates today must not). Three reworked facts are **characterisation**
(behaviour-preserving: what they guard must stay proven). See §4.

## 0. Shape of the change

One realm JSON block removed, three test facts re-subjected onto a planted client, two new
negative facts, one test helper, one doc-comment sentence. No production C#, no frontend, no
AppHost wiring, no migrations.

## Constitution / ADR check

| Check | Result |
|---|---|
| Bounded context / layers | None touched. Realm config lives in `src/AppHost/Realms` (Aspire composition root, `0000` row 024); the tests live in `Integration.Tests` and `Architecture.Tests`. |
| Cross-context references | None added. |
| §II primitives, value objects | No domain model touched. |
| §IV latency | N/A (spec §Latency). |
| ADR-0080 | Its decision (library + auth code/PKCE) is carried by `management-web`, unchanged. Its code sketch is stale and **out of scope** — a human amends ADRs (ADR-0144). |
| ADR-0103 | Tests run on the Aspire fixture; the probe client is planted on the real Keycloak, no Testcontainers. |
| ADR-0139 / §Testing | Two colours, separated by commit (§4, §5). |
| ADR-0144 | No ADR written, no gate weakened: no test deleted, the three facts keep their assertions on the API's answer. |
| ADR-0109 contention | `smart-sentinel-eye-realm.json` is a contention file. Check no parked PR edits it before opening the PR. |
| ADR gap | None (spec header). |

## 1. Research (done at phase 1, recorded in the spec's measurement table)

- The client is unused in client code; three facts mint from it (the issue counted two;
  `TileSpanIntegrationTests:414` is the third).
- Default client scopes cannot be narrowed by the `scope` request parameter, so no surviving
  realm client can stand in for SC-1's subject. A planted client is the only faithful subject.
- The repo already plants such clients (`EventTypeRegistryAuthorizationIntegrationTests`).

## 2. Decisions

### D1: Delete the client, do not disable it

Both refuse tokens once imported. They differ only in what remains:

- **Disable** leaves a full client definition — password grant, `webOrigins: ["+"]`, the audit
  scope — one flag away from live, still iterated by `RealmIdentityTests`,
  `LegacyBundleGrantTests` and `ScopeGrantTests` as if it mattered, and still a thing the next
  reader must reason about.
- **Delete** leaves nothing. Git history is the record; re-creating it is a deliberate edit.

Neither variant reaches a persisted developer volume any differently (both need the volume
reset), and there is no production realm (ADR-0118). No redirect URI or origin is lost:
`management-web` and `kiosk-web` declare their own. So the choice carries no trade-off a human
needs to weigh, and the issue itself says the client has "no reason to exist". **Delete.**

### D2: Plant a probe client per fact, through one `RealmProbe` helper

Add `RealmProbe.PlantPasswordGrantClientAsync(string clientId, IReadOnlyList<string>
defaultClientScopes, CancellationToken)`: public, `standardFlowEnabled = false`,
`serviceAccountsEnabled = false`, `directAccessGrantsEnabled = true`, the given defaults, no
optional scopes; asserts success the way `PlantAsync` does. Cleanup is the existing
`RealmProbe.DeleteAsync`.

Why a helper now: two new call sites (ConsoleScopeGrant, TileSpan) plus the existing private
copy in `EventTypeRegistryAuthorizationIntegrationTests`. That private copy is **left alone** —
folding it is a refactor of a file this issue has no reason to touch (ADR-0036). A later
consolidation can point it at the helper.

### D3: SC-1/SC-2 share one planted client per fact, not per class

Each fact plants its own uniquely named client (`console-scope-probe-{Guid v7:N}`) in a
`try/finally`, exactly as `EventTypeRegistryAuthorizationIntegrationTests` does. No class
fixture: xUnit class fixtures do not compose with `AspireFixture` collection injection without
extra plumbing, and two plants per run cost milliseconds.

Probe scopes, fixed in a `private static readonly string[]` in the test class:
`sse-identity`, `sse-audience`, `sse-groups`, `sse.audit.read` — the retired client's exact set.

TileSpan's probe uses the same four (it grants no `sse.layouts.*`, which is all the fact needs).
It signs in as `admin`, as today.

### D4: The two red facts

- **Static** — `tests/Architecture.Tests/ScopeGrantTests.cs`, new fact
  `The_retired_console_client_is_not_declared`: no `clients[]` entry has `clientId`
  `smart-sentinel-eye-web`. Red today (the entry exists).
- **Runtime** — `ConsoleScopeGrantIntegrationTests`, new fact
  `The_retired_client_cannot_mint_a_token` (SC-9): POST the password grant directly to
  `/realms/smart-sentinel-eye/protocol/openid-connect/token` via `aspire.CreateKeycloakClient()`
  (not `GetAccessTokenForClientAsync`, which throws on refusal and hides the status); assert the
  observed status and `error` code (expected `401` / `invalid_client`). In the same fact, a
  positive control mints for `management-web` with the same `operator` credentials, so a wrong
  password (`invalid_grant`) can never read as the retirement. Red today (Keycloak issues a
  token).

Why both: the static fact proves the design artefact says it; only the running Keycloak proves
the import agrees (memory: *guards that read the design artefact*). Placing SC-9 in the existing
class avoids a new shard-filter entry; `ConsoleScopeGrantIntegrationTests` is already in
`shard-1.filter`.

## 3. The realm change (target shape, for review; not implementation)

Delete `src/AppHost/Realms/smart-sentinel-eye-realm.json` lines 124-147 (the whole
`smart-sentinel-eye-web` object and its trailing comma) so `clients` begins with
`management-web`. Nothing else in the file changes — including `management-web`'s description
("Replaces smart-sentinel-eye-web in spec 009."), which stays true as history. Keep the file's
existing encoding and line endings byte-for-byte outside the deleted block.

## 4. Phase 4a: two colours

| Fact | File | Colour | Observed against unedited realm | After realm edit |
|---|---|---|---|---|
| SC-1 reworked (planted subject) | `ConsoleScopeGrantIntegrationTests` | characterisation | **green** | green, **unmodified** |
| SC-2 reworked (planted subject) | `ConsoleScopeGrantIntegrationTests` | characterisation | **green** | green, unmodified |
| TileSpan 403 reworked | `TileSpanIntegrationTests` | characterisation | **green** | green, unmodified |
| SC-3, SC-4, SC-8 | `ConsoleScopeGrantIntegrationTests` | untouched | green | green |
| SC-9 retired client refused | `ConsoleScopeGrantIntegrationTests` | **red** | **red** (token issued) | green |
| Realm declares no retired client | `ScopeGrantTests` | **red** | **red** | green |

**Why the split is honest, not forced.** The *removal* is new behaviour (a client that mints
today must not), so its facts start red. The three reworked facts guard behaviour that must
**not** move — the policy's 403/200 — and the realm edit is exactly the change that would break
them in their old form (the mint would throw). Re-subjecting them first, and proving them green
on the unedited realm, is what makes the later realm edit safe; an assertion that has to change
after the realm edit means the protected behaviour moved, and blocks.

Order matters: characterisation rework first (green), then red facts, then the realm edit.

## 5. Commits (Conventional Commits, ADR-0030; no `Co-Authored-By`, ADR-0086; each builds, ADR-0087)

1. `test(identity): plant a probe client for the console scope facts` — `RealmProbe` helper,
   SC-1/SC-2/TileSpan re-subjected, `LegacyBundleGrantTests` comment. Green on the unedited
   realm.
2. `test(identity): red the retired smart-sentinel-eye-web client` — SC-9 + static fact. Builds;
   both fail.
3. `fix(apphost): delete the unused smart-sentinel-eye-web client from the realm` — realm JSON
   only. Everything green.

## 6. Risks and residuals

- **Local verification needs a fresh Keycloak volume** (memory: *realm edits need the volume
  deleted*). Without it, SC-9 stays red on a healthy-looking stack. CI boots fresh.
- **ADR-0080 sketch** stays stale — human decision, named in the PR body.
- **#2285** (password grant on `management-web`) is unchanged and out of scope.
- **`EventTypeRegistryAuthorizationIntegrationTests`' private plant helper** duplicates the new
  `RealmProbe` helper. Left alone (D2); a candidate for a later tidy.

## 7. Phase 5 evidence to collect

- On a fresh stack: the raw token-endpoint response for `smart-sentinel-eye-web` (status + body)
  and for `management-web` (200), quoted.
- Keycloak admin `GET /admin/realms/smart-sentinel-eye/clients?clientId=smart-sentinel-eye-web`
  returns `[]`.
- The integration run for `ConsoleScopeGrantIntegrationTests` and `TileSpanIntegrationTests`
  and the `ScopeGrantTests` run, verbatim.
