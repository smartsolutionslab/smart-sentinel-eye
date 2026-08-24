# Phase 0 Research: A name is mutable exactly when it is not an address

**Feature**: `033-rename-convention` · 2026-08-24

Five questions. **The first confirmed the defect the plan was told to look for
before planning around it, and the fifth found that the spec's inventory of five
aggregates is incomplete** — the same shape of miss spec 031 hit.

---

## 1. `ExistsByNameAsync` cannot express what a rename needs

**Confirmed, and it is the feature's central obstacle.**

```csharp
Task<bool> ExistsByNameAsync(FabIdentifier fab, CameraName name, CancellationToken cancellationToken);
```

The implementation asks *does any active camera in this fab hold this
normalised name*:

```csharp
return await dbContext.Cameras
    .Where(candidate => candidate.Fab == fab && candidate.Status != CameraStatus.Decommissioned)
    .AnyAsync(candidate => EF.Property<string>(candidate, CameraConfiguration.NormalizedNameProperty)
        == name.NormalizedValue, cancellationToken);
```

For registration that is exactly right — the camera does not exist yet. **For a
rename it answers the wrong question**: the camera being renamed is itself
active, in that fab, and (when the rename is a no-op or a case-only change)
holds that very normalised name. It would find *itself* and report a collision.

That breaks **FR-010** directly: renaming to the current name must succeed.

**Decision**: extend the repository contract so the question can exclude one
camera — *does any camera **other than this one** hold this name in this fab*.

**Alternatives considered:**

- **Short-circuit in the handler**: if the new name equals the current name,
  return success before checking. **Rejected — it is not sufficient.** A
  case-only change (`Line-3` → `line-3`) is a *real* change to what is
  displayed, but normalises to the same value, so it must be allowed while still
  not colliding with itself. Exact-equality short-circuiting passes the obvious
  test and fails that one.
- **Catch the unique-index violation instead**: see §3 — the violation does not
  arrive in a form worth showing an operator.

**This is the third caller of a rule that has already been enforced
inconsistently once.** Spec 028 found this same predicate missing its status
filter while the index had one. The in-memory double in
`tests/CameraCatalog.Application.Tests/Fakes/InMemoryCameraRepository.cs` mirrors
it and must change in step — that divergence *is* how spec 028's defect
happened, so the plan treats the pair as one change, not two.

---

## 2. `CameraName` normalises, and the storage matches it

`CameraName` carries `NormalizedValue` alongside the original, preserving
display casing. The database mirrors this rather than duplicating the logic:

```sql
name_normalized  character varying(200)  GENERATED ALWAYS AS (upper(name)) STORED
ux_cameras_fab_name_normalized_active  UNIQUE (fab, name_normalized)
    WHERE status <> 'Decommissioned'
```

Read from the migration, not from #1850's description of it — and #1850's
description is accurate.

**Consequence for FR-011, and it is worth stating rather than inheriting.** The
index filters on `status <> 'Decommissioned'` and keys on the *current* name, so
the moment a rename commits, the old name is on no active row and is
immediately registrable again. The behaviour is right; **the spec requires it be
chosen and tested** rather than observed, because spec 028's research made
exactly this mistake in the other direction — it read the index, concluded FR-006
needed no code, and missed that the repository was the other half of the rule.

---

## 3. The index is a backstop, not a second opinion

**Answer: FR-007's "both layers" is defence in depth, not two independent
guarantees, and the plan says so plainly.**

A unique-index violation surfaces from EF as a `DbUpdateException` wrapping a
Npgsql error naming the *index*. `ConcurrencyConflictExceptionHandler` in
ServiceDefaults handles `DbUpdateConcurrencyException` — a different type — so a
unique violation is **not** currently translated by anything and would surface as
an unhandled 500.

So:

- The **application check** is what produces an answer an operator can act on.
- The **index** is what guarantees the invariant actually holds under a race
  that the check cannot see, because the check and the write are not atomic.

Both are required, and they do different jobs. **What must not happen is
concluding that because the index exists, the check is optional** — that is spec
028's defect restated. Nor the reverse.

**Not in scope, but worth naming**: nothing translates a unique-index violation
into a useful response. A rename losing the race between check and commit would
produce a 500. The window is small and the outcome is safe (the invariant holds),
so this feature does not close it — **finding to raise**.

---

## 4. The audit trail needs a new event, and it lives in another context

`IntegrationEventAuditHandler` in `AuditObservability` is **one explicit
`Handle` overload per event type** — sixteen of them:

```csharp
public Task Handle(CameraRegisteredV1 message, …) => AuditAsync(message, …);
public Task Handle(CameraRetiredV1 message, …) => AuditAsync(message, …);
public Task Handle(CameraAddressChangedV1 message, …) => AuditAsync(message, …);
```

**Decision**: mirror `CameraAddressChangedV1` exactly — a domain event, a
handler publishing `CameraRenamedV1` from `Shared.Contracts`, and one more line
in `IntegrationEventAuditHandler`.

That last line is a change in a **different bounded context**. It is in scope
because FR-012 requires the rename reach the audit trail and this is the
established extension point every one of the sixteen already uses — not a design
change, and not an absorbed finding.

**Finding to raise**: publishing a new integration event is never a
one-context change. Every new event requires editing `AuditObservability`'s
overload list, and forgetting to means the event is simply never audited, with
nothing failing. Whether that should be a generic handler is not this feature's
question.

**FR-013 needs no work.** `CameraRegisteredV1` and `CameraRetiredV1` carry the
name as a record of what it was at that moment. A rename appends; it does not
revisit them. No other context persists a camera's name, so nothing goes stale.

---

## 5. The check can generalise — and the spec's inventory of five is short

**The mechanism works.** Every route parameter in the product is either
constrained or not:

| Constrained (identifier-addressed) | Unconstrained (addressed by something else) |
|---|---|
| `{camera:guid}`, `{cameraIdentifier:guid}` | `{name}` — Automation, SystemVariables |
| `{layoutIdentifier:guid}`, `{overlayIdentifier:guid}` | `{integrationName}` — EventIngestion |
| `{auditIdentifier:guid}`, `{eventId:guid}` | `{clientId}` — Identity (devices, kiosks) |
| `{revisionNumber:int}` | `{resourceIdentifier}`, `{resourceKind}` — AuditObservability |

So FR-004's check does **not** need a hardcoded list. The rule is mechanical: *a
context whose API binds a route parameter with no type constraint is addressed by
that value, and must not also offer a rename of it.* That fails for a **future**
context inventing `{slug}` or `{code}`, which is the property spec 031's test
had and the thing worth having.

### The inventory is short, and that is the finding

The spec's table names five aggregates. The routes show **at least two more**
surfaces addressed by a non-identifier: `{integrationName}` in EventIngestion and
`{clientId}` in Identity. `{resourceIdentifier}`/`{resourceKind}` is a generic
audit lookup rather than an aggregate address, so it is not in the same
category — but the other two are.

**Decision**: the ADR states the rule **generally** and enumerates today's
surfaces as evidence, rather than ruling on a closed list of five. FR-002 asked
for a ruling on all five; it gets a ruling that covers those five *and*
everything else, which satisfies it rather than narrowing it.

This is spec 031's lesson arriving early enough to act on: there, an inventory
of seven contexts was compiled carefully and the architecture test found an
eighth on its first run. Here the check was designed before the list was trusted,
so the list being wrong costs nothing.

---

## Summary of decisions

| # | Question | Decision |
|---|---|---|
| 1 | `ExistsByNameAsync` | Extend it to exclude one camera. Handler short-circuiting is **not** sufficient — case-only renames defeat it |
| 2 | Normalisation | Already correct in both value object and storage. FR-011 falls out, but is **chosen and tested**, not inherited |
| 3 | Both layers | Defence in depth, honestly labelled. The check gives the message; the index guarantees the invariant |
| 4 | Audit | New `CameraRenamedV1`, mirroring `CameraAddressChangedV1`, plus one line in another context's handler |
| 5 | The check | Generalises via *unconstrained route parameter*. The spec's list of five is short; the ADR states the rule generally |

**No migration.** The column, the index and the normalisation all already exist.
