# Plan 248 — The copies the realm never sees

**Spec**: [spec.md](spec.md) · **Issue**: #2466 · **Phase**: 2 (Plan)
**ADRs**: ADR-0139, ADR-0144, ADR-0103, ADR-0052, ADR-0053, ADR-0036 · **New ADR**: none

## 1. Placement

- **Project**: `tests/Integration.Tests` — the only project that can see all three kinds of copy:
  `RealmProbe` (lives there), `KeycloakAdminOptions` (already referenced via
  `SmartSentinelEye.Identity.Infrastructure`), and the AppHost model (`Projects.SmartSentinelEye_AppHost`,
  `Aspire.Hosting.Testing`). `Architecture.Tests` cannot: it must not reference `Integration.Tests`
  (`IntegrationTestSelectionTests` explains why it keeps the Aspire graph out).
- **File**: `tests/Integration.Tests/Identity/RealmImportMirrorTests.cs`, namespace
  `SmartSentinelEye.Integration.Tests.Identity` (beside `RealmProbe`; no `using` needed for it).
- **Category**: `[Trait("Category", "FixtureLogic")]` — runs in `ci.yml`'s Docker-free step and
  satisfies `IntegrationTestSelectionTests`. **No** `[Collection(AspireCollection.Name)]`.
- **No** csproj, `src/`, AppHost, realm-file, workflow or existing-test edit.

No bounded context, domain model, entity, value object, domain/integration event, or messaging is
involved. Boundary rules unaffected: the test project already references `Identity.Infrastructure`.

## 2. Class shape

```text
[Trait("Category", "FixtureLogic")]
public class RealmImportMirrorTests
  const string RealmImportPath (relative: "src/AppHost/Realms/smart-sentinel-eye-realm.json", split on '/')
  const string IdentityAdminClientSecretParameter = "IdentityAdminClientSecret"   // the parameter NAME, not a value

  [Fact] RealmProbe_Realm_is_the_realm_the_import_defines
  [Fact] RealmProbe_AdminClientId_names_a_client_the_import_seeds
  [Fact] RealmProbe_AdminClientSecret_is_the_secret_the_import_seeds_for_that_client
  [Fact] KeycloakAdminOptions_default_Realm_is_the_realm_the_import_defines
  [Fact] KeycloakAdminOptions_default_AdminClientId_names_a_client_the_import_seeds
  [Fact] async The_AppHost_default_IdentityAdminClientSecret_is_the_secret_the_import_seeds_for_Identitys_client

  private static JsonDocument ReadRealmImport()          // missing file → InvalidOperationException naming the full path
  private static string DeclaredRealm(JsonDocument)       // root "realm"; absent/non-string → throw naming it
  private static JsonElement[] ClientsNamed(JsonDocument, string clientId)   // all matches; the fact asserts Length == 1
  private static string SecretOf(JsonElement client, string clientId)        // absent "secret" → throw naming clientId + path
  private static string RepositoryRoot()                 // same walk as AppHostE2ESwitchTests.cs:218
```

Rules for the facts:

- **Expected from the file, actual from the copy**: `actual.ShouldBe(expected, customMessage)`.
- Every message names: the copy (type.member or `file:line`), both values, the realm-file path, and
  the remedy ("mirror the realm edit into <copy>, or revert it; then delete the Keycloak volume").
  Secrets in messages are the dev-only literals already committed in plain text — no new exposure.
- Existence facts assert `ClientsNamed(...).Length.ShouldBe(1, ...)`, so a duplicate `clientId`
  in the import (which Keycloak would reject) is also caught rather than silently `First()`-ed.
- Site 4 facts construct `new KeycloakAdminOptions()` — **no initialiser, no binding** — each time.
- Site 2 fact: `DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>([])`,
  `.Resources.OfType<ParameterResource>().Single(p => p.Name == IdentityAdminClientSecretParameter)`,
  `GetValueAsync` under a 10 s `CancellationTokenSource` (mirrors `AppHostParameterOverrideTests.ValueAsync`;
  a private copy — that helper is private and not worth widening). Client looked up by
  `new KeycloakAdminOptions().AdminClientId`; its absence fails naming fact 4b as the likely cause.
- `using JsonDocument` disposal per fact; collection-expression declarations (`JsonElement[] x = [.. ...]`)
  per the analyzer; no `ArgumentNullException.ThrowIfNull`; no underscore fields; sentence-style names.

## 3. Coverage of site 3 (no code)

Transitive, argued in spec §4: new site-2 fact (composed == realm) + existing
`AppHostParameterOverrideTests.A_parameter_with_no_argument_keeps_its_default` (composed == site 3).
Evidence is counterfactual C3.

## 4. Verification

- `dotnet build -c Release` clean (analyzers are errors in Release).
- `dotnet test tests/Integration.Tests/... -c Release --no-build --filter "Category=FixtureLogic" -- RunConfiguration.TreatNoTestsAsError=true`: green, six new facts present in the trx.
- `dotnet test tests/Architecture.Tests -c Release --filter "FullyQualifiedName~IntegrationTestSelectionTests"`: green.
- Counterfactuals C1–C5 (spec §5), verbatim, reverted.

## 5. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | Realm JSON has a BOM / non-ASCII (`—` in a client `name`) | `File.ReadAllText` detects UTF-8 BOM; `SeededCredentialStrengthTests` trims `﻿` defensively — do the same. |
| R2 | Composition-mode `GetValueAsync` hangs | 10 s token; a hang is a finding (spec A1). |
| R3 | Stale binaries during counterfactual revert give a false green/red | Rebuild with `--no-incremental` (or touch the file) after each mutation and each revert; stop a running AppHost first (MSB3027). |
| R4 | Guard becomes a fifth "copy" to maintain | It holds no values — only the parameter *name* and the file *path*. Every expected value is read. |
