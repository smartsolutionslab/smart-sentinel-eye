# Plan 361: One client wire row; citations that name their member

**Spec**: [spec.md](./spec.md). **Issue**: #2939.

## 1. Context and layers

Identity bounded context, **Infrastructure only** (`src/Identity/Infrastructure/KeycloakAdmin/`),
plus doc/message text in one Application file and two test files. No Domain, Application logic,
Api, contract, migration, Aspire resource or messaging change. No cross-context reference is added
or removed; NetArchTest is unaffected because both records are `private` nested types of one class.

No entities or value objects change. The wire rows are deserialisation shapes at the Keycloak trust
boundary, which constitution §II's scope does not cover (they are not domain model state).

## 2. Change A — remove `ClientDetailRow` (behaviour-preserving)

1. `HttpKeycloakAdminClient.WireRows.cs`: delete line 7 (`ClientDetailRow`) and the blank line
   after it.
2. `HttpKeycloakAdminClient.Sweeps.cs:20,63`: `ReadFromJsonAsync<ClientDetailRow[]>` →
   `ReadFromJsonAsync<ClientRow[]>`.

Nothing else. Identical members + identical `JsonOptions` ⇒ identical deserialisation; the lambdas
read `ClientId`, `Attributes`, `Enabled`, which `ClientRow` has with the same types.

## 3. Change B — citations (comment / message text only)

| File | Now | After (substance) |
|---|---|---|
| `tests/Architecture.Tests/KioskPrivilegeSweepRegistrationTests.cs:136-138` (string literal) | "…the best-effort comment in TryDeleteClientAsync (HttpKeycloakAdminClient.cs:363) delegates that case here by name." | Drop the line number and the delegation claim: the client `HttpKeycloakAdminClient.TryDeleteClientAsync` could not remove keeps `offline_access` unless this sweep takes it. |
| `tests/Integration.Tests/Identity/KioskPrivilegeSweepStartupIntegrationTests.cs:21-23` (XML doc) | "The best-effort comment in `TryDeleteClientAsync` (`HttpKeycloakAdminClient.cs:363`) says the startup sweep is the backstop for exactly that." | "`HttpKeycloakAdminClient.TryDeleteClientAsync` reports such a residue; the startup sweep is what takes its privilege (the client itself is `OrphanedClientSweep`'s)." |
| `src/Identity/Application/KeycloakAdmin/KioskPrivilegeSweep.cs:20-21` (XML doc) | "…`TryDeleteClientAsync`, whose comment delegates the case here by name." | "…`TryDeleteClientAsync`." (rest of the paragraph already states the division correctly) |
| `src/Identity/Infrastructure/KeycloakAdmin/KioskPrivilegeSweepHostedService.cs:19-21` (XML doc) | "See … `TryDeleteClientAsync`, whose comment delegates that case here by name." | "See … `TryDeleteClientAsync`." |

Exact wording is the engineer's; the constraint is no line number and no "delegates … by name".

**Hash proof for the three comment-only files** (memory: characterise a comment-only change by
hashing the code). Strip `//`/`///` comment lines and blank lines, hash before and after:

```sh
h() { sed -E '/^[[:space:]]*\/\//d;/^[[:space:]]*$/d' | sha256sum; }
for f in <three files>; do git show origin/develop:$f | h; h < $f; done   # pairs must match
```

Counterfactual: add a non-comment token (e.g. a blank `;`) to one file, re-run, see the pair
differ; revert. Quote both in the PR.

The Architecture.Tests file is **not** comment-only (spec §5): its proof is that the diff touches
only the `customMessage` literal and the test stays green unmodified in its assertion.

## 4. Verification

- `dotnet test tests/Identity.Infrastructure.Tests --filter <spec §3 filter>` — 53 green, before and
  after; `git diff --stat origin/develop -- tests/Identity.Infrastructure.Tests` empty.
- `dotnet test tests/Architecture.Tests --filter KioskPrivilegeSweepRegistrationTests` — 3 green.
- `dotnet build -c Release` clean (analyzers, IDE0005).
- `grep -rn "HttpKeycloakAdminClient.cs:" src tests` and
  `grep -rnE "delegates (the|that) case here by name" src tests` — no matches.
- No new test class ⇒ no shard-filter entry needed.

## 5. Boundary rules

No cross-context references; nothing in `Shared.*` changes. No ADR, constitution, test assertion or
analyzer change.
