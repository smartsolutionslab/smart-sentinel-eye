# Spec 230 — The orphan the vocabulary keeps

**Issue:** [#2503](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2503)
— "ResourceKind.Webhook is now unproducible — remove from the closed vocabulary or keep".
**Branch:** `chore/2503-resourcekind-webhook-decision`
**Created:** 2026-09-24
**Status:** Phases 1-3 complete — awaiting phase 4
**Lane:** autonomous (ADR-0144)
**Colour:** **behaviour-preserving — characterisation, observed green** (ADR-0144 phase 4a)

**Decision:** **Keep** `ResourceKind.Webhook` in `ResourceKind.All`. The doc comment
recording that it is unproducible already exists (spec 206 T008, commit `5ceb49ce`),
so this spec changes **no source file**. It records the decision and pins it.

**ADRs this spec is bound by:**

- **ADR-0144**: the autonomous lane may not make an architecture decision or write
  an ADR. This spec makes neither. It applies the existing rule that
  `ResourceKind.All` is a closed public vocabulary (spec 009 FR-009) to a value
  that has no producer, and picks the option that changes nothing a caller can see.
- **ADR-0036**: smallest possible change. Here that is no change.
- **ADR-0139**: the testing obligations. Nothing changes, so the obligation is
  characterisation: the tests that pin `webhook` pass before and after.

---

## 0. Premise check

### 0.1 `ResourceKind.Webhook` is unproducible: **true**

Every write of a `ResourceKind` into an audit row goes through `V1ResourceMap`,
either by `Conventions.NamespaceToResource` or by `Conventions.HandTweaks`
(`src/AuditObservability/Application/EventHandlers/V1ResourceMap.Conventions.cs`).

- `NamespaceToResource` (`:26-38`) maps to Camera, Stream, Layout, Overlay,
  Variable, Rule, Event and Device. It never maps to Webhook.
- `BuildHandTweaks` (`:55-83`) registers seven compiler-bound entries: Device,
  Kiosk, WebhookIntegration, Overlay (twice), Event and Variable. None is Webhook.
- Across `src/`, `ResourceKind.Webhook` is referenced only at its definition
  (`ResourceKind.cs:39`) and its place in `All` (`:56`). No other code constructs
  a `ResourceKind`. `new ResourceKind(` appears only in the static members, and
  `ResourceKind.From(` is called only on read paths: the EF value converter
  (`AuditEventConfiguration.cs:79`) and the two query handlers.

Nothing produced it historically either. The two deleted hand-tweaks named types
through `Type.GetType(string)`, and those types (`EventIngestion.WebhookIntegrationRegisteredV1`
and `...RevokedV1`) never existed, so the lookup returned null and the entries were never
registered. No persisted row should carry `resource_kind = 'webhook'`. This is
inferred from the code, not queried from a database.

### 0.2 No conflict with #2504: **confirmed, with one correction to the issue's framing**

#2504 (spec 226, worktree `D:/Github/sse-2504`, branch
`2504-dead-entries-no-contract-needs`, not yet a PR) edits
`V1ResourceMap.cs`'s `IdentifierPropertyNames` and `NamespaceToResource`'s
`"Automation"` entry. This spec edits **neither file**, so there is no textual
overlap in `V1ResourceMap.Conventions.cs`.

The correction is that #2504 **also touches `ResourceKind.cs`**. Spec 226 FR-005 / D2
adds a doc comment to `ResourceKind.Rule`, modelled on `Webhook`'s. Because this spec
changes no source file, that is still no conflict. If a later phase *did* edit the
`Webhook` comment, it would be three lines above #2504's `Rule` edit, and whichever
branch merged second would need a rebase. That is one more reason to leave the file
alone (§2).

### 0.3 `Webhook` is **not** the only unproducible kind: **new finding**

`NamespaceToResource["Automation"] = Rule` is the only producer of `ResourceKind.Rule`,
and `src/Shared.Contracts/` has **no `Automation` namespace** (folders: AuditObservability,
CameraCatalog, EventIngestion, Identity, LayoutComposition, OverlayDesigner,
StreamDistribution, SystemVariables). So `Rule` is **already** unproducible on
`develop` today. #2504 does not cause this. It removes a dead entry, and spec 226 D2
reached the same **keep** decision for `Rule` independently, citing `Webhook` as precedent.

The vocabulary therefore has two unproducible members, not one. That matters for §1.

---

## 1. Does anything depend on `All` being exactly the producible set?

**No.** Searched `src/`, `tests/`, `apps/` and `specs/`:

| Consumer | What it relies on | Producibility-dependent? |
|---|---|---|
| `GetResourceTimelineQueryHandler.cs:24` | `value ∈ All`, else `AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND` (400) | No, only membership |
| `SearchAuditQueryHandler.cs:31` | `resourceKind` filter `∈ All`, else `InvalidResourceKind` (400) | No, only membership |
| `AuditEventConfiguration.cs:79` | stored string round-trips through `From` | No. It depends on the reverse: every **stored** value must stay in `All` |
| `ResourceKindTests.cs:15,42` | `"webhook"` accepted, `All.Count == 11` | Pins membership, which is the characterisation this spec needs |
| `V1ResourceMapTests.cs` | every `IIntegrationEvent` has a mapping row | Direction is contract→kind; no test asserts kind→producer |
| `AuditEndpoints.cs:120-122` | *"that code is part of the contract"* | Pins the 400 **code**, not which kinds exist |
| `apps/shared/src/api/audit.api.ts`, `AuditPage.tsx` | `resourceKind: string`, free text | No enumeration of kinds on the client |
| Specs 206, 226 | both record *keep* as the default and file removal as a separate question | Precedent for keep |

No test, doc or client asserts or needs "every member of `All` has a writer". Since
`Rule` has been unproducible without anyone noticing (§0.3), the system does not
treat producibility as an invariant.

---

## 2. Decision

**Keep `ResourceKind.Webhook` in `ResourceKind.All`. Change nothing.**

Reasons, strongest first:

1. **Removal is a breaking change that no caller needs.** It would move
   `GET /audit/webhook/{id}` and `GET /audit?resourceKind=webhook` from 200-empty to
   400. Those are **two** endpoints, not the one the issue names. No caller, test or
   contract benefits from the change.
2. **Nothing depends on exact producibility** (§1). The fact that could have argued
   for removal is not there.
3. **Removing only `Webhook` would be inconsistent.** `Rule` is in the same state
   (§0.3), and spec 226 has already decided to keep it. Removing one orphan and
   keeping the other would make the vocabulary's rule depend on which spec happened
   to look.
4. **Removal adds a read-path hazard.** The EF converter calls `ResourceKind.From` on
   every materialised row, and `From` throws for an unlisted value. If any row were
   ever stored as `webhook`, removal would make that row unreadable. §0.1 argues that
   no such row exists. Keeping the value removes the need to prove it.
5. **Keep can be reversed. Remove cannot, in practice.** Keep can become remove later,
   with a real reason and an ADR. Once a caller has seen the 400, undoing the removal
   is a second API change.
6. **The record already exists.** `ResourceKind.cs:27-38` states that the value is
   unproducible, where the live path is, and why it stays. Rewording it would conflict
   with #2504's adjacent `Rule` comment (§0.2) and add nothing.

**What would reverse this decision:** a consumer that enumerates `All` to offer
choices, such as a dropdown built from an endpoint that lists kinds. An offered option
that can never match a row is a UX defect. That consumer does not exist today:
`AuditPage.tsx` takes free text. If one is built, the correct fix is an ADR on whether
the vocabulary describes the **API surface** or the **data**, decided for `Webhook`
and `Rule` together.

### Rejected

- **Remove `Webhook` from `All`.** See §2 (1)-(5). It would also make this a
  behaviour-changing spec (red first, ADR-0139) whose only motivation is dead-code
  hygiene.
- **Reword or extend the existing `Webhook` comment**, for example to mention `Rule`
  or this decision. This is churn in a file #2504 is editing, and the current comment
  is accurate. The link from the code to this decision belongs in the PR body, not the
  code (CLAUDE.md §Coding behaviour, no drive-by comments).
- **Add a "kind → producer" architecture test** with `Webhook`/`Rule` as allowed
  exceptions. This is speculative (ADR-0036): no rule needs it until a consumer
  enumerates kinds. Revisit it with the ADR described above.

---

## 3. User story

### US1 (P1): The remove-or-keep question has an answer on record

**As** a maintainer reading `ResourceKind.All`,
**I want** the status of its unproducible members decided and recorded, not left open,
**so that** I do not remove them by accident, and do not re-open the question without
the facts it was decided on.

**Acceptance scenarios** (Gherkin). All of them describe behaviour that exists today
and must still hold after this spec:

```gherkin
Scenario: webhook is still a member of the closed vocabulary (happy path)
  Given the ResourceKind vocabulary
  When ResourceKind.From("webhook") is called
  Then it returns ResourceKind.Webhook
  And ResourceKind.All has exactly 11 members

Scenario: a timeline request for the orphan kind answers empty, not 400
  Given no audit row has resource kind "webhook"
  When an authorised caller requests GET /audit/webhook/any-identifier for their fab
  Then the response is 200 with an empty page

Scenario: an unknown kind is still rejected (bad request)
  When an authorised caller requests GET /audit/banana/any-identifier
  Then the response is 400 with code "AUDIT_TIMELINE_UNKNOWN_RESOURCE_KIND"

Scenario: auth is unchanged (auth)
  When an unauthenticated caller requests GET /audit/webhook/any-identifier
  Then the response is 401
```

There is no conflict scenario. The endpoint is a read, and nothing here writes.

### Independent test procedure

1. `dotnet test tests/AuditObservability.Domain.Tests --filter "FullyQualifiedName~ResourceKindTests"`.
   The result must be green, including `Accepts_every_member_of_the_v1_vocabulary(member: "webhook")`
   and `All_returns_the_full_vocabulary`.
2. `git diff origin/develop -- src tests` is empty.

The two HTTP scenarios need no new test. Both handlers decide 200 vs 400 purely on
`value ∈ ResourceKind.All` (§1), and step 1 pins that membership. The 400 and 401
paths are already covered by specs 206, 209 and 215.

---

## 4. Requirements

- **FR-001**: `ResourceKind.All` stays exactly as it is on `develop`, 11 members,
  including `Webhook`.
- **FR-002**: No file under `src/` or `tests/` changes.
- **FR-003**: The decision and its reasoning are recorded here, and summarised in a
  comment on #2503 that links this spec when the issue is closed.

## 5. Success criteria

- **SC-1**: `ResourceKindTests` is green before the (empty) change and after it, with
  the output quoted in the PR body.
- **SC-2**: The PR diff touches only `specs/230-the-orphan-the-vocabulary-keeps/`.

## 6. Latency budget impact

**N/A.** The audit read path is not on the event→overlay path (constitution §IV).
No leg is affected.

## 7. Out of scope

1. **`ResourceKind.Rule`**: #2504 / spec 226 D2 owns its comment. Its unproducibility
   is recorded here (§0.3) only as evidence for this decision.
2. **Whether Automation *should* produce `rule` audit rows.** Automation owns
   no integration event of its own (spec 206 §*Out of scope* (4)), so rule changes are
   not audited under kind `rule`. This was not re-verified beyond the namespace listing
   in §0.3. That may be a real audit-coverage gap, but it is a
   feature question, not a vocabulary one. **Recommend filing** it if the audit trail
   is meant to cover rule edits.
3. **An ADR on "API vocabulary vs data vocabulary"**: only needed when the §2
   reversal trigger occurs.
