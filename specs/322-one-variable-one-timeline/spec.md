# Spec 322: One variable, one timeline

**Issue:** #2502 · **Branch:** `fix/2502-standardize-variable-audit-identifier` · **Lane:** autonomous (ADR-0144)
**ADRs:** ADR-0040 (domain vs integration events), ADR-0073 (integration-event versioning), ADR-0101 (TimescaleDB for audit)
**Prior art:** spec 206 (`specs/206-a-row-no-timeline-can-reach/spec.md`) — §Out of scope (1), §Assumptions (4)

---

## 1. The defect

AuditObservability files every integration event under a `(resource kind, resource identifier)` pivot,
chosen by `V1ResourceMap`. After #2429 (spec 206), the four `variable`-kind contracts pivot on two
disjoint identifier spaces:

| Contract | Pivot today | Source |
|---|---|---|
| `SystemVariableDefinedV1` | `Variable` (guid) | convention picker — first `Guid` property |
| `SystemVariableArchivedV1` | `Variable` (guid) | convention picker |
| `SystemVariableValueChangedV1` | `Variable` (guid) | convention picker |
| `SystemVariableValueRequestedV1` | `Name` | hand-tweak (#2429) |

So `GET /audit/variable/<name>` returns only Automation's requests, and `GET /audit/variable/<guid>`
returns only the lifecycle and value changes. Neither answers "what happened to this variable".

## 2. The decision (already made — not re-opened here)

User, on #2502, 2026-10-09: **standardise the three guid-pivoting contracts on the variable's name**,
matching `SystemVariableValueRequestedV1` and how SystemVariables routes every request
(`GET /system-variables/{name}`). No ripple into Automation or any other context. The two alternatives
(give `SystemVariableValueRequestedV1` a guid; keep the split and add a cross-space audit query) are
declined.

## 3. Where the issue's framing turns out smaller than it reads

The issue speaks of changing the contracts to "also carry/pivot on the variable name". **All three
already carry `string Name`** (`src/Shared.Contracts/SystemVariables/SystemVariable{Defined,Archived,ValueChanged}V1.cs`).
The guid is not something the contract forces on the audit trail; it is what the convention picker
in `V1ResourceMap.BuildDefault` happens to select because `Variable` is the first `Guid` property.

So neither "add a name field" nor "replace the guid with name" is needed. **The contracts are not
touched.** The fix is three hand-tweaks in `V1ResourceMap.Conventions` — the exact mechanism #2429
used for the fourth contract. Consequences:

- No `V2` contract, no ADR-0073 versioning step, no publisher or consumer change.
- The guid stays on the wire and in every audit row's serialised payload, so the incarnation remains
  recoverable from a row (relevant to §5.2).
- The only reader of these three events other than their publisher is the audit handler
  (`IntegrationEventAuditHandler`) — checked by grep across `src/` and `apps/`. Removing the guid would
  therefore have broken nothing today, but there is no reason to pay for a contract version to achieve
  what a map entry achieves.

## 4. User stories

### US1 (P1) — Every variable event lands on the variable's name

As an operator typing a variable's name into the audit page's resource field, I see that variable's
definition, value changes, Automation's set-requests and its archival on **one** timeline.

```gherkin
Feature: The variable audit timeline pivots on the variable's name

  Scenario: Happy path — a lifecycle lands on one timeline
    Given variable "Line4_Temp" is defined in fab "munich"
    And its value is set to "82.4"
    And Automation requests it be set to "90"
    And it is archived
    When an authorised operator requests GET /audit/variable/Line4_Temp for fab "munich"
    Then the timeline holds the Defined, ValueChanged, ValueRequested and Archived rows
    And every row's resourceIdentifier is "Line4_Temp"

  Scenario: The guid no longer reaches a variable timeline
    Given variable "Line4_Temp" was defined with identifier G
    When an authorised operator requests GET /audit/variable/G
    Then the response is 200 with an empty timeline

  Scenario: Pivot picks Name, never the variable guid nor an actor guid
    Given a SystemVariableDefinedV1 / ArchivedV1 / ValueChangedV1 whose every field carries a distinct sentinel
    When V1ResourceMap resolves it
    Then the kind is "variable"
    And the identifier is the Name sentinel
    And the identifier is not the Variable guid

  Scenario: Bad request — unchanged
    When an operator requests GET /audit/notakind/Line4_Temp
    Then the response is 400, exactly as before this spec

  Scenario: Auth — unchanged
    When an unauthenticated caller requests GET /audit/variable/Line4_Temp
    Then the response is 401, exactly as before this spec
    And a caller authorised only for fab "berlin" sees no "munich" rows (existing cross-fab guard)
```

There is no "conflict" scenario: the change is a read-side pivot with no write, no version and no
`If-Match`. The bad-request and auth scenarios are listed to pin that they do **not** move; no new
test is required for them (existing coverage — `CrossFabReadGuardIntegrationTests`, `ResourceKindTests`).

## 5. Consequences, stated so they are decisions

1. **The `variable` kind has one identifier space: the name.** Matches spec 206 §Assumptions (4) —
   operators type a handle into a free-text field; no variable-page deep link by guid exists.
2. **A name timeline spans incarnations.** FR-005 releases an archived name for re-use
   (`VariableRepository`, the filtered unique index on `(Fab, Name)`), so `Line4_Temp` defined, archived
   and defined again shows both lives on one timeline, separated by the Archived row. This is the cost
   spec 206 §Out of scope (1) named ("loses the guid a deep link would use") and the user accepted.
   The guid remains in each row's payload, so incarnations are distinguishable by reading it.
3. **Names are per fab.** The timeline query is already fab-scoped (`GetResourceTimelineQuery`), so two
   fabs' `Line4_Temp` do not merge.
4. **Names are case-sensitive** (`VariableName`), and so is the pivot. No normalisation is added.

## 6. Out of scope

1. **Contract changes of any kind** — §3.
2. **Backfilling rows already written under the guid.** No production deployment exists (spec 206
   §Assumptions (1)); dev/CI rows are disposable. No EF migration, no repair.
3. **`device` / `kiosk`.** The issue cites a "same, smaller" split. On inspection **there is no split**:
   each kind has exactly one contract (`DeviceRegisteredV1`, `KioskEnrolledV1`), both already pivot on
   `ClientId` via hand-tweaks, and Identity routes by `{clientId}` (`DELETE /devices/{clientId}`,
   `DELETE /kiosks/{clientId}`). `RegisteredClientIdentifier` is carried but never pivoted on. One kind,
   one identifier space — the defect this spec fixes does not exist there. Not in this issue; **no
   follow-up recommended** unless a future device/kiosk contract is convention-mapped on its guid
   (at which point the US1 table test in `V1ResourceMapTests` would force a decision anyway).
4. **The `["SystemVariables"] = Variable` namespace entry** becomes reachable by no contract once all
   four variable contracts are hand-tweaked. Left in place: it is the fallback a future SystemVariables
   contract would land on, and pruning it is spec 206 §Out of scope (4)'s cosmetic class.
5. **The audit UI** — renders `resourceIdentifier` verbatim; no frontend file changes.

## 7. Behaviour classification

**Behaviour-changing → Phase 4a RED.** The pivot a reader queries by changes for three contracts.
The three existing `MappingCase` rows in `V1ResourceMapTests` pin the guid today; flipping their
expectation to `Name` (with `MustNotBeIdentifier` = the guid) must be observed failing before the
map changes.

## 8. Independent end-to-end test procedure

1. Unit: `dotnet test tests/AuditObservability.Application.Tests --filter FullyQualifiedName~V1ResourceMapTests`
   — red before T020, green after.
2. Live (phase 5): boot the stack; as an admin define `Spec322_Probe`, set its value, archive it;
   `GET /audit/variable/Spec322_Probe` (gateway, fab header) → three rows, all `resourceIdentifier`
   `Spec322_Probe`; `GET /audit/variable/<its guid>` → empty. Quote both responses in the PR.

## 9. Latency budget

**N/A.** AuditObservability is off the event→overlay path (constitution §IV); the change is a
dictionary entry consulted at audit-write time.
