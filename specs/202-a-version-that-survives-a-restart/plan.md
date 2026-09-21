# Plan — Spec 202, a version that survives a restart

## 1. The choice among the three directions, made and argued

The issue named three candidate fixes and left the choice to this phase. All
three were investigated. **Direction 1 is chosen — persist the counter — with
one element borrowed from direction 2 (a durable store rather than a
reseed-at-startup cache) and direction 3 rejected outright as the primary fix.**

### Chosen: a durable per-overlay counter, advanced atomically in one statement

A table `overlay_text_version (overlay_identifier uuid primary key, version bigint not null)`
in the `system-variables` database. Every version is minted by a single
statement that both advances and reads:

```sql
INSERT INTO overlay_text_version (overlay_identifier, version)
SELECT unnest(@ids), @floor
ON CONFLICT (overlay_identifier)
DO UPDATE SET version = overlay_text_version.version + 1
RETURNING overlay_identifier, version;
```

**Why this and not something cleverer.** It is the shape this context already
uses. `VariableValueRequestDedupStore.cs:26-31` is an Application-layer
interface implemented in Infrastructure by a single parameterised
`INSERT … ON CONFLICT` over `SystemVariablesDbContext`. That is precedent for
every moving part here: the layering, the raw SQL, the atomicity argument, and
the migration. No new mechanism is introduced, which is what keeps this a bug
fix rather than an architectural decision the autonomous lane may not make.

**Atomicity comes free and is not hand-rolled.** `ON CONFLICT DO UPDATE` takes
a row lock, so two concurrent fan-outs touching one overlay serialise on that
row and get two distinct, increasing values (SC-4). No `SELECT`-then-`UPDATE`,
no retry loop, no optimistic token — nothing to get wrong.

**One round trip for the whole fan-out.** `unnest(@ids)` advances every affected
overlay in one statement and returns every new version, so the cost added to the
`event → overlay state` leg is constant in the number of overlays, not linear.
This is the single reason the SQL takes an array rather than a scalar.

### Rejected: direction 2 as "derive from the variable's `AggregateVersion`"

**This is not merely worse, it is incorrect**, and the counterexample is
concrete. A resolved overlay text is a function of *several* variables. Take
overlay X with label `{{a}} / {{b}}`, where `a.Version` is 9 and `b.Version` is
2. Change `a` → push version 9. Change `b` → push version 2. `2 <= 9`, the
kiosk drops it, and the wall freezes — the exact bug being fixed, now reachable
without any restart at all. There is no repair for this short of taking a max
over referenced variables, which regresses the moment a high-versioned variable
is removed from the label or archived. Rejected on correctness.

### Rejected: direction 2 as "a Postgres sequence"

A sequence would work, and it is genuinely the tidier primitive — monotonic by
construction, non-transactional so a rollback leaves a harmless gap rather than
a reused number. It is rejected for two reasons, in order of weight:

1. **The repo has no sequence anywhere.** `grep -rn 'nextval\|HasSequence\|Sequence(' src/`
   returns zero. No ADR covers identity or monotonic generation (the nearest,
   ADR-0113, is about detecting conflicts, not minting numbers). Introducing the
   first one is an architectural choice, and ADR-0144 forbids this lane from
   writing an ADR. Choosing it would mean either a blocked issue or an
   architecture decision made silently — and "don't invent architecture
   silently" is the rule that stops both.
2. **A global sequence makes the read path harder, not easier.** `CurrentVersionFor`
   needs the value *this overlay* last carried; a sequence's `last_value` is
   global and larger than that, which — returned from the snapshot — would
   suppress a legitimately-lower in-flight push. Recovering the per-overlay value
   means a table anyway, at which point the sequence buys nothing.

If a future reviewer prefers the sequence, that is ADR-0155's argument to have.
It is recorded here so the option is visibly weighed rather than unnoticed.

### Rejected as the primary fix: direction 3, client-side self-healing

Three independent reasons, any one of them sufficient.

1. **Nothing triggers the refetch it depends on.** The only refetch path is
   `onReconnected` (`CellPage.tsx:299-301`), and the hub belongs to
   LayoutComposition — a *different process*, which a SystemVariables restart
   does not disturb. The heal would simply never fire. Making it fire means
   treating a dropped push as the signal to refetch; but a drop is also the
   normal and correct outcome for a duplicate or out-of-order frame, so that
   turns every legitimate drop into a REST round trip, on every tile, on every
   wall.
2. **The number it would heal from is broken by the same restart.** Spec
   Finding A: the kiosk does not read `snapshot.version` at all today — the tile
   reads `snapshot?.resolvedText` and nothing else. So direction 3 requires
   building a new `CellPage`↔tile wire carrying a value that `CurrentVersionFor`
   resets to `0` on the very restart being healed. Correcting a corrupted number
   with a corrupted number.
3. **It leaves the server publishing provably wrong versions.** The wire
   contract documents monotonicity. Every future consumer — a second kiosk app,
   a recording, an audit trail — inherits the defect, and each would need its own
   copy of the workaround.

Direction 3 is symptom management over a server that lies. With a server that
does not regress, the client's existing filter is already correct, so **no
kiosk change ships in this spec.** That is the strongest evidence the root
cause is the one being fixed.

## 2. Bounded context, layers, boundaries

**Context: SystemVariables, alone.** No other context is touched. LayoutComposition
relays `Version` verbatim (`SignalRLayoutLifecycleBroadcaster.cs:113`) and needs
no change; the wire record `ResolvedOverlayTextChangedV1` is unchanged, so no
`V2` and no `Shared.Contracts` edit. The cross-context rule (no project
references; `Shared.Contracts` only) is satisfied trivially because nothing
crosses.

| Layer | Change |
|---|---|
| **Domain** | **None.** A push version is not domain state — it is a transport concern for a fan-out, with no invariant, no aggregate and no lifecycle. §II's primitive-obsession rule binds *domain models*, so the `long` here is outside its scope rather than an exemption to it; introducing an `OverlayTextVersion` value object would put a transport counter on a domain surface that has never held one. |
| **Application** | New `IOverlayTextVersions` in `Application/Resolution/`. Two methods: `Task<IReadOnlyDictionary<Guid, long>> AdvanceAsync(IReadOnlyCollection<Guid> overlays, CancellationToken)` and `Task<long> CurrentAsync(Guid overlay, CancellationToken)`. `IReverseIndex` **loses** `NextVersionFor` and `CurrentVersionFor` — a reverse index that also mints versions is why this state ended up in a singleton that could not reach a database. |
| **Infrastructure** | New `Persistence/OverlayTextVersionStore.cs` implementing it over `SystemVariablesDbContext`, scoped, one migration. `InMemoryReverseIndex` loses `versionByOverlay` and its two methods. |
| **Api** | **None.** Same route, same DTO, same scope, same 404. |

**Messaging.** Unchanged in shape: `VariableValueChangedDomainEvent` /
`VariableArchivedDomainEvent` (in-process, ADR-0040) fan out to
`ResolvedOverlayTextChangedV1` (integration, ADR-0073) per affected overlay.
Only where the `Version` on that event comes from changes. The store runs inside
the handler's ambient Wolverine transaction on the same connection (ADR-0088),
so a rolled-back change does not leave a consumed version behind — and if it
did, a gap is harmless: the client filter needs strict increase, not density.

## 3. The cutover floor (SC-3), and the figure

New rows are inserted at **`1_000_000_000`**, not at 1.

Kiosks connected across the deploy still hold marks minted by the in-memory
counter. A durable counter starting from an empty table would hand out `1` and
reproduce #2426 on the deploy that fixes it — once, silently, and a browser does
not reload when a server restarts.

**Why the figure cannot have been reached.** The retired counter is
per-process and per-overlay, incremented once per variable change that touches
that overlay. One billion increments for a single overlay inside one process
lifetime would need roughly 11,500 changes per second sustained for a full day,
against a context whose write path is an operator typing a value in
management-web or an MQTT event arriving through EventIngestion. The bound is
not tight; it is unreachable by four orders of magnitude, and it is written down
here so it is a stated assumption (spec A2) rather than a magic constant.

`bigint` has room: `1e9` leaves ~9.2e18 of headroom.

## 4. Why an archived overlay keeps its row

`IReverseIndex.RemoveOverlay` drops an archived overlay's label references. The
counter row is **not** dropped with them, and that is deliberate: an overlay
identifier can be republished, and a kiosk session can outlive the archive. A
deleted row means the next version for that identifier starts at the floor
again — which is fine (the floor exceeds everything) — but a row deleted *after*
the counter has climbed past the floor would hand back a lower value than the
kiosk holds. Retention costs one 24-byte row per overlay ever published.

This is recorded because `VariableResidueCleanupTests` shows the repo otherwise
cleans up after itself, and an unexplained exception to that habit is how the
next person removes it.

## 5. The ordering fix in the snapshot handler (SC-7, Finding B)

`GetOverlaySnapshotQueryHandler` currently resolves the text (`:40`) and then
reads the version (`:41`). The two must swap: **read the version first, then
resolve the text.** A version read before the text is a lower bound on the
text's freshness, so a push that commits during the request carries a strictly
higher version and the kiosk accepts it. Read after, the snapshot can stamp
stale text with the newer push's number and suppress it permanently.

This does not claim to impose a total freshness order across concurrent
readers — the current code never had one either, and pretending otherwise would
be a claim nobody could check. It closes one specific, one-line-wide window.

### The constructor constraint, met head-on

`GetOverlaySnapshotQueryHandler.cs:12-16` carries a comment from spec 148:

> *…so this handler's public constructor stays exactly `(IReverseIndex, IVariableRepository, IResolver)` — a shape `GetOverlaySnapshotQueryHandlerTests` constructs directly at six call sites and may not be edited.*

That constraint was spec 148's, and it was a **characterisation** constraint:
its change was behaviour-preserving, so an unedited test file was the proof. This
spec's change is behaviour-changing, the tests are red first by obligation, and
the handler genuinely needs a fourth dependency. **The six call sites are edited,
and the comment at `:12-16` is rewritten in the same commit** — leaving a comment
that forbids what the file now does is how a rule outlives its reason. Four
constructor parameters sits at SonarAnalyzer's advisory limit, not past it
(ADR-0084).

## 6. Risks

| # | Risk | How it is caught |
|---|---|---|
| R1 | The new statement adds measurable latency to the `event → overlay state` leg. | `NFR_VariableResolutionLatencyTests` run twice before and twice after, medians quoted in the PR. Its own 800 ms assertion is 4× the budget and would not catch this — the figure is the evidence. |
| R2 | The restart test passes for the wrong reason: the resource never actually restarts, or the assertion could not have failed. | Proved by counterfactual — the test is observed **red** against unmodified `develop` first, and that output is quoted. A restart test that is green before the fix is testing nothing. |
| R3 | `unnest` over an empty id array, or over duplicate ids in one fan-out. | The handlers already return early on an empty overlay set; duplicates within one `INSERT … ON CONFLICT` raise `ON CONFLICT DO UPDATE command cannot affect row a second time` in Postgres. The store de-duplicates its input, and a test covers a repeated identifier. |
| R4 | The migration runs but the table is missing in an environment because `MigrationRunner` did not pick it up. | The integration tests run against the real Aspire stack, which runs `MigrationRunner` (ADR-0067); a missing table fails every one of them loudly. |
| R5 | Someone reintroduces a process-lifetime version cache later "for speed". | **Correction (phase-6 review):** `VersionSurvivesARestartTests` carries `[Trait("Category", "Disruptive")]`, and `ci.yml` excludes that category permanently — it does not run on any PR and cannot catch this. The guard that actually runs in CI on every PR is `OverlayTextVersionStoreIntegrationTests.A_store_from_a_brand_new_scope_continues_from_the_persisted_value`: a cache re-added *inside the store* fails it, since a fresh `DbContext`/connection would not see the cached value. A cache layered in front of `IOverlayTextVersions` in DI, or a regression in the real restart path itself, has no continuous safety net — only the one-time verification recorded in `verification.md`. |

## 7. What this plan will not do

- Touch any file under `apps/`.
- Add a `V2` of `ResolvedOverlayTextChangedV1`, or change `ResolvedOverlaySnapshotDto`.
- Re-key the counter on `(fab, overlay)` — spec, Out of scope.
- Write ADR-0155, or amend the constitution. If review concludes one is needed,
  that is a **blocked** outcome for this issue, not a decision taken here.
- Close, reopen or relabel #2423, or alter ADR-0153's table.
