# Plan 247 — The service the filter never asked about

**Spec**: [spec.md](spec.md) · **Issue**: #2469 · **Phase**: 2 (Plan)
**ADRs**: 0036, 0037, 0088, 0109, 0139, 0144 · **Constitution**: §IV N/A; §Testing red-first.

## Context and layers

No bounded context changes. Test code only:

| File | Change |
|---|---|
| `tests/Architecture.Tests/OutboxCommitTests.cs` | candidate filter, permitted list, stale-entry fact, docs, theory rename |
| `tests/Architecture.Tests/Attribution/OutboxCommitServiceProbe.cs` (new) | one probe offender outside `.Persistence`, not `…Repository`-named |
| `tests/StreamDistribution.Infrastructure.Tests/Attribution/StreamFabAttributionTests.cs` | the reason-pin fact |

No domain model, no messaging, no contracts, no Aspire resource, no `src/`
change. Architecture.Tests already references
`SmartSentinelEye.StreamDistribution.Infrastructure` (csproj), so
`typeof(StreamFabAttributionService)` needs no new reference and no cross-context
rule is engaged.

## Design

### 1. Candidate filter (`Offenders`)

```csharp
private static List<string> Offenders(Assembly assembly) =>
    [.. assembly.GetTypes()
        .Where(type => !type.IsNested)
        .Where(CallsSaveChangesDirectly)
        .Select(type => type.FullName ?? type.Name)];
```

`!type.IsNested`, not "all types": `BodiesOf` already walks a type's direct
nested types, so an async state machine (`X+<AttributeOnceAsync>d__4`) is found
through `X`. Admitting nested types as candidates would report the same offence
twice, once under a compiler-generated name. Nested-of-nested bodies are not
walked — pre-existing, belongs to #2470; say so in the doc.

`Offenders` stays **unfiltered by the exception** — the probe fact and the
counterfactuals exercise the raw detector.

### 2. The permitted list

```csharp
private static readonly Type[] PermittedDirectCommits =
[
    typeof(StreamFabAttributionService),
];
```

Doc on the field (the decision record lives here, where a reader hitting red
will look): the three reasons from spec §1.2, with point 3 first; that the
entry is keyed by type so a copy is not exempt; that the reason is pinned by
`StreamFabAttributionTests.The_pass_raises_nothing_a_direct_commit_would_drop`;
that if that test ever fails the fix is `IStreamRepository.SaveAsync`, and this
entry is deleted, not the test adjusted. Cite spec 247 / #2469.

### 3. The theory

Rename `No_repository_commits_without_its_announcements` →
`Nothing_commits_without_its_announcements` (the old name now misdescribes the
rule). Body:

```csharp
List<string> offenders = [.. Offenders(Assembly.Load(assemblyName))
    .Except(PermittedDirectCommits.Select(type => type.FullName!))];
```

Failure message unchanged in substance (still directs to
`ITransactionalCommit`; it must **not** mention the permitted list — the list is
not the remedy).

### 4. Stale-entry fact

```csharp
[Fact]
public void Every_permitted_direct_commit_still_commits_directly()
```

For each permitted type: its assembly name is in `PersistenceAssemblies` **and**
`CallsSaveChangesDirectly(type)` is true. Message names the entry and says to
remove it. Both conditions: an entry for a type the theory never scans would
be an exemption that exempts nothing and still reads as a decision.

### 5. Probe

New file, namespace `SmartSentinelEye.Architecture.Tests.Attribution` (a file
can hold one file-scoped namespace; `OutboxCommitProbe.cs` is `.Persistence`).

```csharp
public sealed class OffenderAttributionService(ProbeDbContext dbContext)
{
    public async Task AttributeAsync(CancellationToken cancellationToken) =>
        await dbContext.SaveChangesAsync(cancellationToken);
}
```

Awaited, so the offence sits in the nested state machine — the same shape as
the real service. Doc mirrors `OutboxCommitProbe.cs`: deliberately violates the
rule, must never be fixed, and the exact expected list in the probe fact must be
updated with any change here.

The probe fact `The_rule_sees_both_spellings_of_a_direct_commit` gains one
expected row, `…Architecture.Tests.Attribution.OffenderAttributionService`, and
its doc drops "same namespace/name candidate filter" for "the same candidate
filter". Name kept (renaming a test-writer-owned fact in the fix commit would
muddy the red/green diff; the name is still true — it does see both spellings).

### 6. Reason pin

In `StreamFabAttributionTests`:

```csharp
[Fact]
public void The_pass_raises_nothing_a_direct_commit_would_drop()
```

Two `Unattributed()` streams, `ClearPendingEvents()` on each (the builder goes
through `Provision`, which raises `StreamProvisionedDomainEvent`), run
`StreamFabAttributionService.Attribute` resolving both, assert both attributed
and **both `PendingEvents` empty**. The "both attributed" assertion is what
stops this passing vacuously (an `Attribute` that did nothing would also raise
nothing). Doc: why this is the exemption's premise; what to do if it fails.

## Class doc of `OutboxCommitTests`

Replace "deliberately a rule with no exemption list…" with: the rule has one
recorded exception, why the rot argument does not reach it (the four rows of
spec §2's table, compressed), and that `DeadLetterRepository` still commits
through the seam — the exception is for a type that *cannot* use the seam, not
one that would rather not. `ReferencesSaveChanges`' doc: "from a repository
body" → "from any type's body".

## Commit sequence (each builds on its own — ADR-0087)

1. `test(architecture): make the outbox guard's probe commit outside a repository`
   — T001–T003. Probe file + one expected row + reason-pin fact. Probe fact red
   (two of three); reason pin green. Builds.
2. `fix(architecture): consider every type, not only repositories, as an outbox-guard candidate`
   — T004–T007. All green.

## Verification

Spec §5: filtered run, full `Architecture.Tests`, full
`StreamDistribution.Infrastructure.Tests`, three counterfactuals, verbatim into
`verification.md`. No Aspire stack needed (reflection + pure unit test).

## Risks

- **Other guards walk the test assembly** (`BoundaryTests`,
  `PrimitiveBoundaryTests`, `NameMutabilityConventionTests`,
  `StaleCodeConventionTests`…). A new `…Service` type holding a `DbContext`
  might trip one. Mitigation: full-project run in T003 and T007.
- **#2470 edits the same file.** Rebase whichever lands second; neither changes
  the other's lines except possibly `BodiesOf`'s doc.
- **Locked binaries**: a running AppHost in this worktree held `src/*/Api/bin`
  on 2026-09-25. Build with `--artifacts-path` outside the repo; do not stop a
  stack you did not start.
