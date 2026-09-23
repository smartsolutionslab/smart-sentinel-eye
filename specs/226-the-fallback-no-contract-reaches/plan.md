# Plan — Spec 226, the fallback no contract reaches

**Spec:** `specs/226-the-fallback-no-contract-reaches/spec.md`
**Issue:** #2504 · **Branch:** `2504-dead-entries-no-contract-needs`
**Colour:** characterisation, observed **GREEN** (constitution §Testing second
obligation; ADR-0144's two-colour rule).

---

## 1. Bounded context and layers

**One context: `AuditObservability`.** Two layers, both already existing; no new
type, no new folder, no new project reference.

| Layer | File | Change |
|---|---|---|
| Application | `EventHandlers/V1ResourceMap.cs` | delete 7 array entries; correct 2 comments |
| Application | `EventHandlers/V1ResourceMap.Conventions.cs` | delete 1 dictionary entry |
| Domain | `AuditEvent/ResourceKind.cs` | add a doc comment to `Rule` (D2) |
| Tests (Application) | `EventHandlers/V1ResourceMapFallbackReachabilityTests.cs` | **new** — FR-007 guard |
| Application (csproj) | `SmartSentinelEye.AuditObservability.Application.csproj` | **conditional** — an `InternalsVisibleTo` grant, only if T002 takes §5.2's preferred form |
| Tests (Application) | `EventHandlers/V1ResourceMapTests.cs` | **not touched** — the characterisation net (D3) |

**Nothing else.** `Shared.Contracts` is read-only to this spec: no contract is
added, removed or reshaped, which is what keeps the change behaviour-preserving
in the strongest sense — the input corpus to the registry is identical before
and after.

### Why the Domain file is in the list at all

`ResourceKind` lives in Domain and carries no I/O; a doc comment on a static
property changes no compiled behaviour. It is here because deleting
`NamespaceToResource["Automation"]` removes `ResourceKind.Rule`'s **only
writer**, and the same file already documents exactly that situation for
`ResourceKind.Webhook` (`:31-42`, added by spec 206). Following the established
precedent is cheaper than inventing a new place to say it. §5 records the
alternative.

---

## 2. Entities, value objects, invariants

No entity or value object is created, changed or deleted.

`ResourceKind` (Domain, `IValueObject<string>`, ADR-0038 / ADR-0046 / ADR-0066)
is the only value object in the blast radius. Its invariant — **`All` is a
closed vocabulary and `From` throws for anything outside it** — is the reason
`Rule` stays. The invariant is *preserved by not touching `All`*; removing the
entry would change `GET /audit/rule/x` from 200-empty to 400, which is a
behaviour change and therefore out of bounds for a GREEN-coloured change.

`ResourceIdentifier` — untouched. `V1Mapping` / `V1MappingEntry` — untouched.

---

## 3. Messaging: domain event → integration event

**None.** No domain event is raised, no integration event is published,
consumed, added or versioned. `V1ResourceMap` is a pure in-process lookup read
by `AuditingMessageHandler`; it neither sends nor receives.

ADR-0073's `V<N>` rule does not engage, because no contract's shape changes —
which is worth stating explicitly, since the *names* being deleted look like
contract properties and are not.

---

## 4. Boundary rules

- **No cross-context project reference is added or removed** (constitution §III,
  ADR-0040). `AuditObservability.Application` already references
  `Shared.Contracts` directly — established by spec 206 US2, visible in
  `V1ResourceMap.Conventions.cs:2-6`. The new test file uses the same references
  the existing `V1ResourceMapTests.cs` already has.
- **`NetArchTest` boundary suite unchanged**, and expected green unchanged:
  `BoundaryTests.V1ResourceMap_covers_every_IIntegrationEvent` (`:225`) asserts
  every contract has *an* entry. The prune removes no entry for any real
  contract, so this guard cannot move. It is in the verification set anyway,
  because a guard you assume is unaffected is a guard you have not checked.
- **§II primitive ban:** not engaged. `IdentifierPropertyNames` is a
  `string[]` of reflection keys inside an Application-layer private static, not
  state on a domain model. `PrimitiveBoundaryTests` binds domain models only.

---

## 5. The mechanism, and the alternatives rejected

### 5.1 What makes this prune safe — the argument in one place

The allow-list fallback is guarded by `if (pick is null)` where `pick` is the
first `Guid`-typed property (`V1ResourceMap.cs:158,166`). Therefore:

> The fallback executes **iff** a convention-mapped contract declares no `Guid`
> property at all.

Thirteen contracts are convention-mapped (20 total − 7 hand-tweaked). All
thirteen declare a `Guid`. Therefore the fallback executes zero times, and every
entry in the array — pruned or kept — is currently unreachable.

That argument is **structural**, not statistical: it is not "we checked and none
of them happened to hit the fallback", it is "the branch guarding the fallback
is false for every input the registry is built from". FR-007 turns the premise
of that argument into a standing assertion, so the argument stays true rather
than merely having been true on 2026-09-23.

### 5.2 Where FR-007's guard lives, and what it asserts

**New file:** `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapFallbackReachabilityTests.cs`.

It asserts, over the contracts assembly by reflection:

> For every concrete `IIntegrationEvent` whose namespace tail resolves through
> `NamespaceToResource` and which is **not** hand-tweaked, at least one public
> instance property is of type `Guid`.

**`Conventions.HandTweaks` and `NamespaceToResource` are `internal`, and
`SmartSentinelEye.AuditObservability.Application` grants no `InternalsVisibleTo`
— checked, not assumed:** the five projects in the repo that do grant one are
`EventIngestion.Infrastructure`, `StreamDistribution.Infrastructure`,
`MigrationRunner`, `ScenarioSimulator` and `ServiceDefaults`. This context is not
among them. So the guard cannot read the hand-tweak set as the code sits.

Two forms, in order of preference. **T002 picks one and records which in the
test's doc comment**; both are falsifiable, and neither is an assertion that
checks its own input (`an-assertion-must-not-check-its-own-input`) — in both, a
new Guid-less contract makes the test red without a word of the test changing.

- **Preferred — derive the set.** Add an `InternalsVisibleTo` grant for the test
  project to `SmartSentinelEye.AuditObservability.Application.csproj`, in the
  `AssemblyAttribute` shape the five precedents already use, and have the guard
  read `Conventions.HandTweaks.Keys` and `Conventions.NamespaceToResource` to
  compute the convention-mapped set exactly as `BuildDefault` does. Nothing is
  hard-coded; the guard stays correct when a tweak is added or removed. Cost: a
  four-line project-file edit, which is more than a comment change and is
  therefore called out here rather than slipped into the diff.
- **Fallback — name the single exception.** No project-file change: assert that
  every concrete `IIntegrationEvent` declaring no `Guid` public instance property
  is named in a small exception list, which today holds exactly one entry,
  `WebhookIntegrationRotatedV1`, with a failure message telling the reader to add
  a hand-tweak or extend the list. Weaker (the list can go stale in the
  add-a-tweak direction) but sound in the direction that matters: a new Guid-less
  contract still fails.

**Rejected outright:** hard-coding the seven hand-tweaked type names. It would
go stale silently in both directions, which is the failure mode of every rule
this repo has had to correct.

*Rejected:* putting this assertion as a 21st row in `V1ResourceMapTests.cs`. It
would edit the characterisation net, which D3 forbids, and it is a different
kind of claim (about the corpus's shape, not about one mapping's answer).

*Rejected:* putting it in `Architecture.Tests`. That project deliberately holds
no project reference to `Shared.Contracts` and reaches everything through
`Assembly.Load` + strings — spec 206 US2 removed exactly that pattern from this
area and the reasoning is quoted in spec 206 §US3. Writing new string-typed
reflection here would reintroduce it.

### 5.3 Why the counterfactual is a phase-4 task and not optional

§0.4 of the spec establishes that the 20-row table is green under the prune,
green under pruning all twelve, and green under deleting the fallback entirely.
So "the table stayed green" is compatible with an arbitrarily wrong change, and
on its own it is not evidence.

The counterfactual (spec §6 Part B) supplies the missing sensitivity: it
constructs the contract shape for which the pruned entries *would* matter, shows
the pre-prune map answering via `CameraIdentifier`, shows the post-prune map
answering with no identifier, and then deletes the construction. Both outputs
are quoted in the PR. This is the repo's standing technique
(`prove-a-guard-by-counterfactual`), and here it is the **only** thing
separating this change from "seems unused".

It is deliberately a **throwaway working-tree state, never committed**: a
permanent no-Guid contract in `Shared.Contracts` would be a real contract with
real consequences (a Wolverine listener, a `Handle` entry point, an audit row
shape), and adding one to prove a point about dead code would be the tail
wagging the dog.

### 5.4 Comment corrections (FR-003, FR-004)

`V1ResourceMap.cs:15-20` and `:138-139` both illustrate the namespace convention
with `Shared.Contracts.Automation.RuleCreatedV1` → `"automation"` / `"rule"`.
That type has never existed and that namespace does not exist. Both are replaced
with a real pair — `Shared.Contracts.CameraCatalog.CameraRegisteredV1` →
`camera` — which a reader can verify by opening one file.

This matters more than it looks: a doc comment naming a non-existent type is how
a reader concludes the Automation mapping is load-bearing, which is the exact
confusion #2504 exists to remove. Pruning the entry while leaving the comment
would move the confusion rather than fix it.

---

## 6. What changes, file by file

### `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs`

- `IdentifierPropertyNames` (`:33-47`) → 5 entries: `Name`,
  `OverlayIdentifier`, `RegisteredClientIdentifier`, `ChunkIdentifier`,
  `EventIdentifier`. Relative order preserved. **Order is not behaviour today**
  (the loop never runs) but preserving it keeps the diff a pure deletion, which
  is what makes it reviewable.
- Class doc comment (`:15-20`) — real namespace example (FR-003).
- `ResolveResourceKind` comment (`:138-139`) — real namespace example (FR-003).
- `BuildConventionPicker` comment (`:159-165`) — one added sentence recording
  that the list names properties that exist on real contracts, and that no
  contract reaches it today because every convention-mapped contract carries a
  `Guid` (FR-004).

### `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`

- `NamespaceToResource` (`:27-38`) — delete `["Automation"] = DomainResourceKind.Rule`
  (`:34`). Eight entries remain.

### `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs`

- `Rule` (`:23`) — doc comment in the shape of `Webhook`'s (`:31-42`): currently
  unproducible, its only writer was the `"Automation"` namespace entry,
  `Shared.Contracts` has no `Automation` folder, kept in `All` because the
  vocabulary is a closed public list and removing it would turn
  `GET /audit/rule/x` from 200-empty into 400.

### `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapFallbackReachabilityTests.cs` (new)

- The FR-007 guard, per §5.2.

---

## 7. Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| §Testing — behaviour-preserving ⇒ characterisation, green | `V1ResourceMapTests.cs` captured green before, passes unmodified after; diff on that path empty |
| §III / ADR-0040 — no cross-context refs | none added or removed |
| §IV — latency budget | no leg; provably inert (spec §5) |
| §II — no primitives on domain models | not engaged; no domain model touched |
| ADR-0139 — failure/output quoted in the PR | both characterisation runs **and** both counterfactual runs quoted |
| ADR-0144 — lane may not weaken a gate | no test deleted, no threshold lowered, no suppression, no analyzer narrowed |
| ADR-0144 — lane may not write an ADR | none needed; spec §0 records why |
| ADR-0065 — coverage | removing unreachable lines cannot lower the ratio; new guard is covered test code |
| ADR-0053 / ADR-0105 / §House rules | sentence-style test names, `Ensure.That` for any new guard, no leading-underscore field, explicit-type collection expressions |
| ADR-0109 — `[P]` on disjoint files only | see `tasks.md` §Parallelism |

---

## 8. Risks

1. **The prune is verified by something vacuous and everyone moves on.** The
   highest-probability failure of this spec is that phase 4 runs the 20-row
   table, sees green, and skips the counterfactual — producing a PR whose
   evidence proves nothing (spec §0.4). Mitigated by making the counterfactual
   SC-004 and a blocking task (T006–T008), not a suggestion.
2. **#2505 lands first and the captured baseline is of a file that no longer
   exists.** Mitigated by spec §7's ordering caveat: re-capture on the rebased
   tree.
3. **Spec number 226 collides** with a concurrent architect dispatch
   (`spec-number-check-origin-develop-isnt-enough`). Mitigated by T012's
   re-check before the PR.
4. **The throwaway contract's collateral reds get "fixed".** Three unrelated
   guards will name the throwaway type during §6 Part B. An engineer who
   suppresses or adapts them has weakened a gate (ADR-0144). Mitigated by naming
   all three in T007 up front and requiring them green again after the revert.
5. **FR-007's preferred form needs a project-file edit.**
   `AuditObservability.Application` grants no `InternalsVisibleTo` today (§5.2),
   so the derived-set guard requires adding one. That is a wider diff than a
   "cosmetic prune" advertises. Mitigated by T002 making it an explicit choice
   with a stated fallback, so phase 6 reviews a decision rather than discovering
   a surprise.
