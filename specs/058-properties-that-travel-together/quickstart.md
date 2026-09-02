# Quickstart: Properties that travel together

**Feature**: 058 | **Date**: 2026-09-02 | **Plan**: [plan.md](./plan.md)

How to do one site, and how to prove it. StreamDistribution is the worked
example because it is the smallest — one aggregate, one configuration — and it
is the recommended first slice.

---

## One site, start to finish

1. **Check the covering tests exist first.** This work is behaviour-preserving
   (FR-009), so the obligation is green-throughout, not red-first. If the pair
   has no test asserting both halves survive a round trip, write one *before*
   touching the aggregate, while the old shape still compiles.

2. **Add the composite** in the context's aggregate folder, beside the types it
   wraps (ADR-0092):

   ```csharp
   // src/StreamDistribution/Domain/Stream/Provisioning.cs
   public sealed record Provisioning(ProvisionedAt At, OperatorIdentifier By) : IValueObject
   {
       public static Provisioning From(ProvisionedAt at, OperatorIdentifier by)
       {
           Ensure.That(at).IsNotNull();
           return new(at, by);
       }
   }
   ```

3. **Replace the pair on the aggregate**, and fix the one place that sets it.

4. **Map it**, with the required-navigation line that is easy to forget:

   ```csharp
   builder.OwnsOne(stream => stream.Provisioning, provisioning => { /* two columns */ });
   builder.Navigation(stream => stream.Provisioning).IsRequired();
   ```

5. **Follow the compiler outward** — readers change from `stream.ProvisionedAt`
   to `stream.Provisioning.At`. Mappers unwrap to the same DTO fields; the wire
   shape does not move (see [contracts](./contracts/README.md)).

6. **Verify**, below.

---

## Verifying a slice

**The schema did not move** — the check that matters most, because breaching
FR-004 fails no test:

```sh
dotnet ef migrations has-pending-model-changes --project src/StreamDistribution/Infrastructure --no-build
```

Expect *No changes have been made to the model since the last migration.*

**If it reports a pending change**, generate the migration and read it before
assuming it is yours — then delete it and restore the snapshot:

```sh
dotnet ef migrations add TmpCheck --project src/<Context>/Infrastructure --no-build
# read the Up() body, then:
rm src/<Context>/Infrastructure/Persistence/Migrations/*TmpCheck*.cs
git checkout -- src/<Context>/Infrastructure/Persistence/Migrations/
```

Two contexts already report a pending `version` nullability change that has
nothing to do with this feature — **issue #2022**, which predates it. Confirm
what you are looking at before blaming the slice. `dotnet ef migrations remove`
tries to reach the database and fails here; delete the files instead.

**A nullable column means the required-navigation line is missing.** That is the
one failure mode of this feature that reaches production silently.

**Tests stay green**:

```sh
dotnet test tests/StreamDistribution.Domain.Tests --no-build
dotnet test tests/StreamDistribution.Application.Tests --no-build
dotnet test tests/StreamDistribution.Infrastructure.Tests --no-build
dotnet test tests/Architecture.Tests --no-build
```

A red test during this work is a regression, not a step (constitution
§Testing).

---

## Known local limitations

These are properties of this machine, not of the feature. Record them in the PR
rather than reporting a clean run that did not happen.

- **Integration and e2e suites cannot run here** — they need Docker, which this
  machine does not have. CI is the gate for them, and for FR-008's outward
  shapes.
- **`scripts/coverage-check.ps1` needs PowerShell 7**; this machine has 5.1. To
  check a coverage figure locally, collect and merge by hand:

  ```sh
  dotnet test tests/<Project> -c Release --no-build --collect:"XPlat Code Coverage" --results-directory <dir>
  dotnet reportgenerator "-reports:<dir>/**/coverage.cobertura.xml" "-targetdir:<out>" \
      "-reporttypes:TextSummary" "-assemblyfilters:+SmartSentinelEye.<Context>.Domain"
  ```

  Each new composite is a new type in a gated assembly; a composite without
  tests drags its context's Domain figure toward the 90% floor. That is how
  spec 057's Automation slice failed CI.

- **Inspect EF columns via the relational model, not property metadata.**
  `IProperty.GetColumnName()` without table context reports columns that do not
  exist for owned types (research [R1](./research.md)):

  ```csharp
  foreach (var t in context.Model.GetRelationalModel().Tables)
      Console.WriteLine($"{t.Name}: {string.Join(", ", t.Columns.Select(c => c.Name))}");
  ```
