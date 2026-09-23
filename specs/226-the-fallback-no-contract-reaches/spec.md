# Spec 226 — The fallback no contract reaches

**Issue:** [#2504](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2504)
— *"`V1ResourceMap.Conventions` has dead entries in `IdentifierPropertyNames`
and `NamespaceToResource`"* (`tech-debt`, `agent:ready`, on Project #13, status
Todo, **zero comments** — the body is the whole brief).

**Branch:** `2504-dead-entries-no-contract-needs`
**Lane:** autonomous (ADR-0144).
**Phase-4a colour:** **characterisation, observed GREEN** — this change is
behaviour-preserving (constitution §Testing, second obligation). A red test
during it is a regression, not progress.

**ADRs this spec is bound by:** ADR-0037 (the seven phases), ADR-0144 (the
autonomous lane and its two colours), ADR-0040 (integration events cross
contexts only via `Shared.Contracts`), ADR-0073 (`V<N>` suffix), ADR-0039 /
ADR-0090 (Guid v7 identity, `Identifier` suffix) — the last pair is *load
bearing here*, not decorative: it is the reason the reflection picker's
Guid-first rule is reliable and therefore the reason the allow-list is dead.
Also ADR-0052 / ADR-0053 (xUnit + Shouldly, sentence-style names), ADR-0105
(`Ensure.That`), ADR-0065 (coverage gates), ADR-0109 (`[P]` disjoint-file
parallelism).

**No new ADR is needed.** Nothing here decides anything architectural: no
boundary moves, no contract changes, no new mechanism. It deletes source that
cannot execute. Recorded explicitly because CLAUDE.md requires an ADR flag when
a decision is not covered, and the honest answer is that no decision is being
made.

---

## 0. The corrected premise

The dispatch asked for the issue's claims to be checked against the code rather
than believed. Three of the four survive; one is wrong in a way worth writing
down, and a fifth thing nobody asked about turns out to matter more than any of
them.

### 0.1 It is **seven** names, not six — and the miscount is inherited

The issue says *"contains six entries"* and then lists seven. Counting the
actual array in `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs:33-47`,
there are **twelve** entries, of which exactly **seven** match no property on
any concrete `IIntegrationEvent` in `src/Shared.Contracts/`:

| # | Entry | Matches a contract property? |
|---|---|---|
| 1 | `Identifier` | **no** — prune |
| 2 | `Name` | yes — 9 contracts | 
| 3 | `OverlayIdentifier` | yes — `OverlayHighlightRequestedV1` |
| 4 | `CameraIdentifier` | **no** — prune |
| 5 | `LayoutIdentifier` | **no** — prune |
| 6 | `RuleIdentifier` | **no** — prune |
| 7 | `VariableIdentifier` | **no** — prune |
| 8 | `RegisteredClientIdentifier` | yes — `DeviceRegisteredV1`, `KioskEnrolledV1` |
| 9 | `ChunkIdentifier` | yes — `AuditChunkArchivedV1` |
| 10 | `WebhookIntegrationIdentifier` | **no** — prune |
| 11 | `DeadLetterIdentifier` | **no** — prune |
| 12 | `EventIdentifier` | yes — `FabEventIngestedV1` |

Verified by reading all 22 files under `src/Shared.Contracts/` in full (21
records + the marker interface), not by grep alone. The corpus is **20 concrete
`IIntegrationEvent` records**; `LayoutTileV2` is the 21st record and is a nested
payload, not an event.

One near-miss worth naming, because it is exactly the false positive the
dispatch warned about: `EventIdentifier` appears **twice** under
`src/Shared.Contracts/` — once on `FabEventIngestedV1` (a contract, so the entry
is kept) and once on `EventMetadata` (the envelope carried by *every* contract,
not itself an `IIntegrationEvent`). Either sighting alone justifies keeping the
entry; had only the `EventMetadata` one existed, keeping it on that evidence
would have been wrong. `CausingEventIdentifier` on two contracts is **not** a
match — the picker compares `property.Name == candidate` by exact equality, not
by suffix.

**Where the miscount came from.** The issue is a faithful copy of
`specs/206-a-row-no-timeline-can-reach/spec.md` §*Out of scope* (4), which says
"six entries" over the same seven names. The error is upstream and has now been
propagated once. It is corrected here and nowhere else; spec 206 is history and
is not edited.

### 0.2 "The allow-list fallback runs for zero contracts" — **true**, and for a stronger reason than the issue gives

Confirmed by reading the mechanism rather than trusting the claim
(`V1ResourceMap.cs:151-188`):

```
BuildConventionPicker(type):
    pick = first public instance property whose type is exactly Guid
    if pick is null:            <-- the allow-list fallback, and only here
        pick = first property whose Name is in IdentifierPropertyNames
```

`BuildConventionPicker` is called (`:130`) only for a type that is **not** in
`Conventions.HandTweaks` and whose namespace tail resolves through
`NamespaceToResource`. Seven of the twenty contracts are hand-tweaked
(`V1ResourceMap.Conventions.cs:62-80`), so the picker is built for the other
**thirteen** — and every one of those thirteen declares a `Guid` as its **first**
parameter:

`CameraAddressChangedV1`, `CameraRegisteredV1`, `CameraRenamedV1`,
`CameraRetiredV1` (`Guid Camera`); `FabEventIngestedV1` (`Guid EventIdentifier`);
`LayoutRevisionArchivedV1`, `LayoutRevisionPublishedV2` (`Guid Layout`);
`OverlayRevisionArchivedV1`, `OverlayRevisionPublishedV1` (`Guid Overlay`);
`StreamHealthChangedV1` (`Guid Camera`); `SystemVariableArchivedV1`,
`SystemVariableDefinedV1`, `SystemVariableValueChangedV1` (`Guid Variable`).

So `pick is null` is **never true**, and the `foreach` over
`IdentifierPropertyNames` **never executes**. The only contract in the corpus
with no `Guid` at all — `WebhookIntegrationRotatedV1` — is hand-tweaked and
never reaches the picker.

The stronger reason: this is not luck. ADR-0039 and ADR-0090 make Guid v7 the
identity of every aggregate root, so a `Shared.Contracts` event that is *about*
an aggregate carries that Guid by construction. The fallback is dead because the
identity convention holds, not because of an accident of the current twenty.

**Consequence the issue does not draw, and it changes the shape of the work:
all twelve entries are dead, not seven.** The five kept entries match a property
name but are just as unreachable — their contracts all have a Guid the picker
takes first. Spec 206 §*Out of scope* (4) says this itself ("a fallback that,
after US1, runs for no contract at all") and then prunes on the narrower
criterion anyway. §*Decisions* (D1) settles which criterion this spec uses and
why.

### 0.3 The `"Automation" → rule` entry is dead — **true**

`src/Shared.Contracts/` has exactly eight context folders: `AuditObservability`,
`CameraCatalog`, `EventIngestion`, `Identity`, `LayoutComposition`,
`OverlayDesigner`, `StreamDistribution`, `SystemVariables`. **There is no
`Automation` folder**, so no type's namespace tail can ever be `"Automation"`
and `ResolveResourceKind` (`:136-149`) can never return `Rule`.

Automation really does publish into other contexts' namespaces, confirmed at the
publishing site: `src/Automation/Application/EventHandlers/FabEventIngestedV1Handler.cs:94`
and `:109` publish `OverlayHighlightRequestedV1`
(`Shared.Contracts.LayoutComposition`) and `SystemVariableValueRequestedV1`
(`Shared.Contracts.SystemVariables`). Automation's `using` set names only
`EventIngestion`, `LayoutComposition` and `SystemVariables`. It owns no
integration event of its own.

### 0.4 The finding the issue missed: **its own proposed verification is vacuous**

The issue says *"verify via the existing 20-row `V1ResourceMapTests` table
staying green."*

That table **cannot fail for this change, and cannot fail for a much larger
one.** Every row exercises `V1ResourceMap.Lookup` on a real contract; every real
contract resolves either through a hand-tweak or through the Guid-first branch.
Not one row reaches the allow-list. The table stays green if you prune the seven
names; it stays green if you prune all twelve; **it stays green if you delete
`IdentifierPropertyNames` entirely and replace the fallback with `return
_ => null;`**. A safety net that is green under the change *and* under an
arbitrarily larger version of the change proves the change is safe only in the
sense that a switched-off smoke alarm proves there is no fire.

This is the repo's own recurring defect — an assertion that cannot fail
(`an-assertion-must-not-check-its-own-input`), and a guard whose claim nobody
tested by counterfactual (`prove-a-guard-by-counterfactual`). The table is
**necessary and not sufficient**. §6 says what is added so that something in the
tree *can* fail, and §5 says how the whole thing is proven by construction
rather than by assertion.

---

## 1. Decisions taken, with the alternative recorded

**D1 — prune the seven, keep the five, on the issue's criterion.** The seven
that match no property go; the five that match a property stay, even though all
twelve are equally unreachable today.

*Rejected:* deleting `IdentifierPropertyNames` and the fallback outright. It is
defensible — the array is entirely dead — but it is a **design change**, not a
prune: it removes the system's only graceful answer for a future contract that
has no Guid, and turns that case from "picks a sensibly-named property" into
"silently null resource identifier". That is a decision about the map's
behaviour under a corpus that does not exist yet, and under ADR-0144 the
autonomous lane does not make decisions. Recorded in §*Out of scope* (1) as a
follow-up to file.

The kept five have a stated meaning after this spec: *the allow-list names
properties that exist on real contracts and would be the right pick if that
contract ever lost its Guid.* That is a coherent sentence. "Names of properties
that exist nowhere" is not, which is exactly the reader-confusion the issue
names as the motivation.

**D2 — `ResourceKind.Rule` gains a doc comment, mirroring `Webhook`.** Removing
the `"Automation"` entry leaves `ResourceKind.Rule`
(`src/AuditObservability/Domain/AuditEvent/ResourceKind.cs:23`) with **no writer
anywhere in the system** — the entry was its only producer. The file already has
this exact situation for `ResourceKind.Webhook` (`:31-42`), with a doc comment
explaining why an unproducible value stays in a closed public vocabulary. `Rule`
gets the same treatment: comment added, value kept, `All` untouched.

*Rejected:* removing `Rule` from `All`. It would turn `GET /audit/rule/x` from
200-empty into 400 (`ResourceKind.From` throws for an unlisted value) — an API
behaviour change, which would break this spec's own GREEN colour. Also rejected:
silence. Deleting the only producer and saying nothing relocates the confusion
the issue exists to remove.

*This is the one judgement call in the spec.* It is three lines of comment in a
file the issue does not name. The alternative — leave `ResourceKind.cs` alone —
is available and costs only that a reader of `Rule` has no way to learn it is
unwritable. Flagged here so the phase-6 reviewer can reject it on sight rather
than discover it in the diff.

**D3 — `V1ResourceMapTests.cs` is not edited. At all.** It is the
characterisation net and must pass **unmodified** (constitution §Testing). The
new guard in §6 therefore lands in a **new file**. This also keeps this spec
disjoint from issue **#2505**, which renames that file's `_map` field — see §7.

---

## 2. User stories

One story. The change is a single vertical slice: it is independently
shippable, independently observable, and splitting it would produce two commits
that are each meaningless alone.

### User Story 1 — The dead entries go, and what makes them dead is written down (Priority: P1)

**As** a developer reading `V1ResourceMap` to understand which contracts the
allow-list serves,
**I want** the allow-list to contain only names that exist on real contracts,
and the namespace map to contain only namespaces that exist,
**so that** I stop reverse-engineering a `RuleIdentifier` / `Automation`
relationship that the codebase has never had.

#### Acceptance scenarios (Gherkin)

The four canonical shapes, mapped to what this change actually has: there is no
HTTP surface, no auth surface and no write path here, so "bad request" and
"auth" are read as *the analogous failure modes of a pure mapping registry* —
an unknown type, and a contract the registry must not silently drop. Stated
rather than omitted, because an omitted scenario class reads as an oversight.

```gherkin
Scenario: Happy path — every contract resolves exactly as it did before
  Given the 20 concrete IIntegrationEvent records in Shared.Contracts
  When each is looked up through V1ResourceMap.Default after the prune
  Then each returns the same ResourceKind it returned before the prune
  And each returns the same resource identifier value it returned before
```

```gherkin
Scenario: Conflict — the pruned entry would have been reachable, and is not
  Given a hypothetical IIntegrationEvent with no Guid property
  And a string property named CameraIdentifier
  And a namespace tail of CameraCatalog
  When it is looked up before the prune
  Then the allow-list fallback picks CameraIdentifier
  And when the same type is looked up after the prune
  Then no identifier is picked at all
  And no such type exists in Shared.Contracts, which is why the prune is inert
```

```gherkin
Scenario: Bad request — an unmapped type still degrades, it does not throw
  Given a type that is not an IIntegrationEvent
  When V1ResourceMap.Default.Lookup is called with it
  Then V1Mapping.Unmapped is returned
  And no exception is thrown
```

```gherkin
Scenario: Auth / integrity — no contract silently loses its resource pivot
  Given the architecture guard V1ResourceMap_covers_every_IIntegrationEvent
  And the mapping table's both-direction completeness assertions
  When the prune is applied
  Then every concrete IIntegrationEvent still has a map entry
  And every map entry still names a type the table covers
```

```gherkin
Scenario: Reachability is pinned, not assumed
  Given every contract V1ResourceMap maps by convention rather than hand-tweak
  When its declared properties are inspected
  Then at least one is of type Guid
  And therefore the IdentifierPropertyNames fallback is unreachable by construction
```

---

## 3. Requirements

### Functional

- **FR-001** — `IdentifierPropertyNames` loses exactly the seven entries named
  in §0.1: `Identifier`, `CameraIdentifier`, `LayoutIdentifier`,
  `RuleIdentifier`, `VariableIdentifier`, `WebhookIntegrationIdentifier`,
  `DeadLetterIdentifier`. Five remain, in their current relative order.
- **FR-002** — `Conventions.NamespaceToResource` loses exactly the
  `["Automation"] = DomainResourceKind.Rule` entry. Eight remain.
- **FR-003** — The `ResolveResourceKind` doc comment (`V1ResourceMap.cs:138-139`)
  no longer illustrates the convention with
  `"Shared.Contracts.Automation" → "rule"`, because that namespace does not
  exist. It uses a real one.
- **FR-004** — The `IdentifierPropertyNames` / `BuildConventionPicker` comment
  gains one sentence recording *why* the list is short: it names properties that
  exist on real contracts, and it is reached only by a contract with no `Guid`,
  of which there are none today.
- **FR-005** — `ResourceKind.Rule` gains a doc comment recording that it is
  currently unproducible and why it stays in `All` (D2).
- **FR-006** — No observable behaviour changes: for every one of the 20
  contracts, `(Kind, ResourceIdentifier)` is byte-identical before and after.
- **FR-007** — A new characterisation guard asserts that every
  convention-mapped contract declares a `Guid` property — pinning the precondition
  under which the fallback is unreachable, so a future Guid-less contract fails
  the build instead of silently resolving to a null identifier.
- **FR-008** — The guard of FR-007 lands in a **new** test file.
  `V1ResourceMapTests.cs` is not modified (D3).

### Non-functional

- **NFR-001** — `tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`
  is captured **passing** before any source edit and passes **unmodified**
  afterwards. Both runs' verbatim output go in the PR body (ADR-0139, ADR-0144).
- **NFR-002** — Coverage gates hold (ADR-0065): Application ≥ 80%. Deleting
  unreachable lines cannot lower covered-line ratio; the new guard adds covered
  test code only.
- **NFR-003** — Release build clean: no new analyzer warning, no suppression
  added, no analyzer narrowed. ADR-0144 forbids reaching green by weakening a
  gate.
- **NFR-004** — Style: no leading-underscore private field, collection
  expression with explicit type for any new collection, `Ensure.That` for any
  new guard, sentence-style test names (CLAUDE.md §House rules, ADR-0053,
  ADR-0105).

### Out of scope, stated so each is a decision rather than an omission

1. **Deleting `IdentifierPropertyNames` and the fallback outright.** All twelve
   entries are unreachable (§0.2), so the whole mechanism is arguably dead. That
   is a design change about behaviour under a future Guid-less contract, not a
   prune, and the lane may not make it. **Recommend filing** with §0.2 as the
   input.
2. **Pruning the five kept entries.** Same reason; they are the narrower half of
   (1).
3. **Removing `ResourceKind.Rule` (or `Webhook`) from `All`.** An API behaviour
   change (400 instead of 200-empty) for an unrelated reason. Spec 206
   §*Out of scope* (2) already recommended filing this for `Webhook`; `Rule`
   joins it.
4. **The `_map` leading-underscore field in `V1ResourceMapTests.cs`** — that is
   issue **#2505**, and D3 means this spec must not touch the file anyway.
5. **`Option<T>` migration** of any touched signature (ADR-0141 is advisory;
   existing signatures are not a defect).
6. **Backfilling audit rows.** Nothing written changes, so there is nothing to
   backfill.
7. **The audit UI.** No frontend file is touched.

---

## 4. Locked technical choices

Nothing new is chosen. The change lives entirely inside existing choices: .NET
10 / C# (ADR-0000 row 024), xUnit + Shouldly (ADR-0052), sentence-style test
names (ADR-0053), `Shared.Contracts` as the only cross-context path (ADR-0040,
constitution §III), Guid v7 identity (ADR-0039 / ADR-0090). No package is added,
no resource is added to `AppHost`, no migration is written.

---

## 5. Latency-budget impact

**N/A — no leg.** `V1ResourceMap` is read by the AuditObservability subscriber
(`AuditingMessageHandler`) when writing an audit row. That write is downstream of
and parallel to the `event → overlay state` leg; it is not on the
`event arrival → overlay rendered` path at all (constitution §IV). More
decisively, the change is **provably inert** — §0.2 shows the deleted lines
cannot execute, so there is no code path whose cost could change in either
direction.

Stated rather than omitted because CLAUDE.md requires every change to say which
leg it affects, and "none" is an answer that has to be given, not skipped.

---

## 6. Independent end-to-end test procedure

Runnable by someone who has read none of the above.

**Part A — the existing net, observed green twice.**

```sh
cd <worktree>
git stash list                     # expect empty: nothing hidden
dotnet test tests/AuditObservability.Application.Tests \
  --filter FullyQualifiedName~V1ResourceMapTests
# expect: Passed! - Failed: 0, Passed: 28   (20 theory rows + 8 standalone facts)
```

Apply the prune. Re-run the identical command. Expect the identical line, and
`git diff --stat -- tests/AuditObservability.Application.Tests/EventHandlers/V1ResourceMapTests.cs`
to print **nothing**.

**Part B — the counterfactual, which is what actually proves it.** Part A alone
proves nothing (§0.4). Do this in a throwaway working-tree state that is
**never committed**:

1. On the **pre-prune** tree, add
   `src/Shared.Contracts/CameraCatalog/ThrowawayNoGuidV1.cs`:
   a `sealed record ThrowawayNoGuidV1(string CameraIdentifier, EventMetadata Metadata) : IIntegrationEvent`
   — no `Guid` anywhere, one property named after a **pruned** entry, in a
   namespace the map resolves.
2. Assert through a scratch test that `V1ResourceMap.Default.Lookup` returns
   `Kind = camera` and `ResourceIdentifier = <the CameraIdentifier value>`.
   **This is the fallback executing.** Capture the output.
3. Apply the prune on top. Re-run the same scratch test: expect
   `Kind = camera` and `ResourceIdentifier` **absent**. Capture the output.
4. Revert the throwaway contract and the scratch test entirely.

Steps 2 and 3 together are the falsifiable demonstration: the pruned entries
*were* reachable for a contract of that shape, the prune *does* change the
answer for such a contract, and no such contract exists in `Shared.Contracts`
(§0.2 enumerates all 20). "Dead" is then a measured property of the corpus, not
an adjective.

Expect **collateral reds** in step 2 while the throwaway contract exists —
`Every_integration_event_has_a_row_in_the_mapping_table`,
`V1ResourceMap_covers_every_IIntegrationEvent`, and the
`IntegrationEventAuditHandler` `Handle`-entry-point guard will each name the
throwaway type. That is those guards working; record it, do not fix it, and
confirm all three go green again after the revert.

**Part C — the namespace entry.**

```sh
ls src/Shared.Contracts/          # expect 8 context folders, no Automation
grep -rn "Shared.Contracts.Automation" --include=*.cs src tests | grep -v obj/
                                   # expect: no hits after FR-003
```

**Part D — the whole suite.**

```sh
dotnet test tests/AuditObservability.Application.Tests
dotnet test tests/AuditObservability.Domain.Tests
dotnet test tests/Architecture.Tests
dotnet build -c Release            # analyzers clean, no new warning
```

---

## 7. Contention

| File | This spec | Also touched by |
|---|---|---|
| `src/AuditObservability/Application/EventHandlers/V1ResourceMap.cs` | edited | — |
| `src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs` | edited | — |
| `src/AuditObservability/Domain/AuditEvent/ResourceKind.cs` | comment only (D2) | — |
| `tests/.../EventHandlers/V1ResourceMapFallbackReachabilityTests.cs` | **new** | — |
| `tests/.../EventHandlers/V1ResourceMapTests.cs` | **not touched** (D3) | **#2505** renames `_map` here |

**#2505 is the one real interaction, and D3 defuses it.** Because this spec adds
no row and edits no line of `V1ResourceMapTests.cs`, the two issues can land in
either order without conflicting. One ordering caveat: if #2505 merges first,
the NFR-001 "before" capture must be **re-taken on the rebased tree** — a
baseline captured against the pre-rename file is a baseline of a file that no
longer exists, and quoting it in the PR would be evidence of the wrong thing.

---

## 8. Success criteria

- **SC-001** — `IdentifierPropertyNames` has 5 entries; none of the 7 named in
  §0.1 appears.
- **SC-002** — `NamespaceToResource` has 8 entries; `"Automation"` is absent.
- **SC-003** — `V1ResourceMapTests.cs` passes unmodified; `git diff` on that
  path is empty, and both runs' output are quoted in the PR.
- **SC-004** — The counterfactual of §6 Part B is performed and both captured
  outputs are quoted in the PR. **Without this the PR does not satisfy the gate**
  — Part A alone is the vacuous check the issue proposed (§0.4).
- **SC-005** — The FR-007 guard exists, passes, and is demonstrated to fail
  against the §6 Part B throwaway contract.
- **SC-006** — `Architecture.Tests` green; `dotnet build -c Release` clean, no
  suppression added.
- **SC-007** — No behaviour change is observable: the 20 mappings are identical.

---

## 9. Assumptions, marked because they are guesses until phase 4 confirms them

1. **`Type.GetProperties(Public | Instance)` returns record parameters in
   declaration order.** The CLR does not guarantee it. It does not matter here:
   all 13 convention-mapped contracts declare their `Guid` **first**, and 12 of
   the 13 have exactly one candidate Guid anyway — but the honest statement is
   that §0.2's "first Guid" reasoning leans on observed ordering, not a
   guarantee. The existing 20-row table already pins the actual picked value per
   contract, which is what makes the lean safe.
2. **The corpus is the `Shared.Contracts` assembly, nothing else.**
   `BuildDefault` scans `typeof(IIntegrationEvent).Assembly` (`:103`) and nothing
   else, so a contract defined outside that assembly would not be scanned at all
   — a different defect, not this one. No such type exists (ADR-0040 forbids it).
3. **Spec number 226 is free.** Verified against `origin/develop` and all 12
   remote branches' `specs/` trees at dispatch: the highest is
   `225-the-render-leg-ci-never-reads`. Another in-flight architect dispatch
   could claim 226 concurrently
   (`spec-number-check-origin-develop-isnt-enough`); re-check before the PR.
4. **The test-count figure `Passed: 28` in §6 Part A is a prediction**, derived
   by counting the file's 20 `AllCases` rows plus its 8 `[Fact]` methods
   (`grep -c '\[Fact\]'` → 8, `grep -c '\[Theory\]'` → 1). If the observed number
   differs, the **observed** number is the baseline and this line is wrong, not
   the run.
