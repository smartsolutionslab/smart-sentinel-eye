# Phase 0 Research: Read a single camera, and correct one

**Feature**: `029-camera-read-edit` · **Spec**: [spec.md](./spec.md) · 2026-08-24

Six questions. **Two came back against the spec**: the payoff US1 claimed did not exist to be won, and correcting an address had a cross-context consequence the spec did not mention at all. Both were raised rather than absorbed, and **both were adopted at the Phase 2 gate** — the spec now carries the corrections.

---

## 1. Is there really a client-side over-fetch to remove?

**Decision: no. SC-001 and SC-002 describe an improvement to something that does not happen.**

Checked because SC-002 asserts a code path exists to delete, and a success
criterion that cannot fail is worse than no criterion.

```sh
grep -rn "useListCamerasQuery\|cameraIdentifier" apps/management-web/src --include=*.tsx | grep -v test
```

What is actually there:

| | |
|---|---|
| `CamerasPage.tsx` | one `useListCamerasQuery`, rendered straight into a `DataTable` |
| `CameraViewerPanel.tsx` | takes `cameraIdentifier` and `cameraName` **as props** from an already-loaded row |
| Single-camera route | **none** |
| Filter-to-find-one | **none** |

The app never filters a listing to find one camera because **it never asks a
single-camera question**. It renders the whole table and passes row data
downward. There is no over-fetch, because there is no fetch.

**Consequence**: US1 as written is motivated by a saving that is not currently
available. That does not make the endpoint wrong — it makes its *reason* wrong,
and the real reason is one story down:

> **US2 cannot exist without US1.** An edit requires the caller to quote a
> version (FR-004), and **no camera endpoint exposes a version today**. The
> read-one is a prerequisite of the edit, not a UI optimisation.

**ADOPTED at the Phase 2 gate.** All three were applied:

- **SC-001** — restate as a property of the endpoint: *"a single-camera request
  transfers one camera's data regardless of catalogue size"*. True and testable
  on the API alone.
- **SC-002** — currently vacuous. Either drop it, or restate forward-looking:
  *"a client can answer a single-camera question without retrieving the
  listing"* — a capability claim rather than a removal claim.
- **US1's "Why this priority"** — the honest justification is that US2 depends
  on it and FR-006 needs it, not that it stops an over-fetch.

**Alternatives considered**: building the single-camera UI in this feature, so
the saving becomes real. Rejected — it widens a backend feature into a frontend
one, and the endpoint is independently justified by US2 and FR-006. Worth
filing separately.

---

## 2. What happens to the stream when a camera's address changes?

**Decision: nothing, today — and that is a hole this feature would open.**

**This is the cross-context half the spec does not cover.** Verified:

```
CameraRegisteredV1 (carries Url)
  └─ CameraRegisteredIntegrationEventHandler
       └─ ProvisionStreamCommand(RtspSourceUrl)
            └─ rtsp.AddPathAsync(stream.Path, rtspSourceUrl)   ← MediaMTX pulls from here
```

And on the Stream aggregate:

```csharp
public StreamSourceUrl SourceUrl { get; private set; } = null!;   // set only in Provision
```

`SourceUrl` has a private setter assigned in exactly one place — `Provision`.
**No behaviour changes it.** So correcting a camera's address in the catalogue
leaves MediaMTX pulling the *old* one indefinitely: the catalogue says the
camera is at the new address, the SFU streams from the old, and nothing
reconciles them. The stream keeps working while pointing at hardware that may
no longer be there — a failure that looks like success.

That is precisely the shape of spec 028's FR-008 ("the stream follows the
camera into retirement"), and this feature needs its equivalent: **the stream
follows the camera's address.**

**Consequence**: FR-003 as written — *"an operator MUST be able to change a camera's address"* — was not implementable in isolation without shipping a known inconsistency.

**ADOPTED at the Phase 2 gate.** The spec now carries FR-013 (the stream is re-pointed), FR-013a (the catalogue change does not depend on the SFU being reachable) and FR-014 (a viewer's reference survives a correction), plus SC-007 and SC-008. Phases 3 and 4 of the plan ship together or not at all.

The requirements mirror spec 028 FR-008/FR-008a: the change is announced,
StreamDistribution re-points the path, and the catalogue change does **not**
depend on the SFU being reachable — announcement, not shared transaction.

**What the test must look at.** The obvious assertions all pass while the defect
is present: that the announcement was published, that the stream row's stored
source changed, that the endpoint returned 204. Only the SFU's own configured
source distinguishes a working re-point from a believed one. SC-007 says
"verified against the streaming layer itself" for exactly that reason.

**Alternatives considered**:

- **Forbid editing the address of a camera that has a stream.** Rejected: that
  is every registered camera, so it forbids the feature.
- **Let the startup reconciler fix it.** Rejected: it re-creates paths from
  `Stream.SourceUrl`, which is the stale value. It would faithfully restore the
  wrong address.
- **Retire-and-re-register instead of editing.** That is today's workaround and
  the thing the feature exists to replace; it also changes the identifier,
  which SC-004 explicitly forbids.

---

## 3. How is the version carried, and does anything expose one today?

**Decision: reuse `ConcurrencyHeaders`; put `Version` on the body as well as the ETag. No new mechanism.**

Nothing in CameraCatalog exposes a version — `CameraSummaryDto` carries
identifier, fab, name, address, registered-at and status, and no version. The
mechanism nevertheless exists at every other layer:

| Layer | State |
|---|---|
| `AggregateRoot<TIdentifier>.Version` | present; `Camera` inherits it |
| `CameraConfiguration` | `.IsConcurrencyToken()` already mapped |
| `ServiceDefaults.ConcurrencyHeaders` | `ETag(int)` for reads; `If-Match` parse for writes |
| Precedent | Automation, EventIngestion, Identity |

`ConcurrencyHeaders` already defines the failure codes this feature needs —
`IF_MATCH_REQUIRED` (**428 Precondition Required**, deliberately not a silent
fallback to no concurrency control) and `IF_MATCH_MALFORMED`.

Following `RuleDto`, the version goes **on the body as well as the ETag**, and
its stated reason applies here verbatim: *"also on the body so the list endpoint
hands every row a version without a per-row fetch."* So `CameraSummaryDto`
gains `Version` too — which means an operator can edit straight from the
listing without a read-one round-trip, and is the one place where this feature
does measurably reduce traffic.

**Alternatives considered**: ETag only. Rejected — it forces a per-row fetch
before any edit and diverges from three existing contexts for no gain.

---

## 4. Is a migration needed, and — the question spec 028 got wrong — is the schema the only place the rule lives?

**Decision: no migration. And the layer check matters more than the schema check.**

No new column: `Version` is already mapped, `Status` already persists, and
nothing here adds state.

The spec asks for this check specifically because **spec 028's research made
the mirror-image mistake** — it verified the partial unique index permitted name
reuse, concluded FR-006 needed no production code, and missed that
`ICameraRepository.ExistsByNameAsync` enforced the same rule one layer above
with no status filter. All three US2 tests failed on the first run that reached
them.

So, every layer that currently enforces "retired is terminal":

| Layer | Where | Relevant to this feature? |
|---|---|---|
| Domain | `Camera.Retire` early-returns when already `Decommissioned` | The pattern FR-005's guard must follow |
| Application (read) | `ListCamerasQueryHandler` filters retired from the default listing | Untouched — FR-002 reads by identifier, not via the listing |
| Infrastructure (read) | `CameraRepository.ExistsByNameAsync` excludes retired | Untouched — no name uniqueness check without rename (FR-012) |
| Schema | partial unique index `WHERE status <> 'Decommissioned'` | Untouched |

**FR-005's enforcement point is the aggregate, and only the aggregate.** There
is no existing edit path, so unlike spec 028 there is no second enforcement site
that could silently disagree — the risk here is the opposite one, that the guard
is put in the handler where a second caller would bypass it. It belongs on
`Camera`.

---

## 5. How is another fab's camera kept invisible?

**Decision: `GetWithinFabAsync`, already built by spec 028. Structural, not remembered.**

```csharp
// The fab is part of the predicate, not a check afterwards: another
// plant's camera is never materialised, so it cannot be leaked by a
// caller that forgets to compare (spec 028 FR-004).
```

Both operations use it. This satisfies FR-006 by construction rather than by
discipline: a handler that forgot to compare fabs cannot leak, because the row
never loads. FR-007's ordering (fab before every other precondition) follows
from the same call — the fab is *in* the lookup, so nothing can be evaluated
before it.

The remaining risk is not the lookup but the **response**: two refusals that
take different code paths can diverge in body or headers while sharing a status.
Hence SC-003's field-by-field comparison, and hence the edit must produce the
same refusal as the read for the same cause — including when `If-Match` is
absent, where the temptation is to answer 428 before discovering the camera is
not the caller's. **It must not**: a 428 for another fab's camera confirms the
camera exists. Fab first, always.

---

## 5a. Correction, found during implementation: the 428 was never an oracle

**tasks.md T020 and the contract both overstated the risk, and the test written
from them failed against correct code.**

The claim was that `PATCH` with no `If-Match` on another fab's camera must
answer **404**, because a `428 IF_MATCH_REQUIRED` would confirm that camera
exists. That is true only if the 428 is issued *after* the camera is looked up.
It is not. The endpoint validates the header immediately after resolving the
caller's own fab and **before any lookup**, so every identifier gets the same
428 — one that exists in Munich, one that never existed anywhere. Uniform, and
therefore not an oracle.

Forcing a 404 would mean loading the camera before validating the precondition:
more work for a malformed request, and a divergence from Automation,
EventIngestion and Identity, which all validate the header at exactly this
point.

**What the test asserts now** is the property FR-006 actually states —
indistinguishability — rather than a particular status code, in both
directions: without a precondition both answer 428 identically, and with a
well-formed one both answer 404 identically, the latter being the case that
*would* leak if the ordering ever drifted.

**A second correction, smaller.** SC-003's "field by field" cannot be taken
literally across two requests, because the problem `detail` echoes the
identifier the caller asked about and the two requests necessarily name
different cameras. That is the caller's own input reflected back. The
comparison normalises identifiers and trace fields and compares everything else
exactly, so it still fails on an extra field, a different title or status, or a
detail that mentions the fab — the things that would be a leak.

---

## 6. Does the audit trail need a new integration event?

**Decision: yes if question 2 is adopted — and then the architecture test enforces the rest.**

FR-011 requires the change to be audited naming the operator. Spec 028
established the route: a domain event → an integration `*V1` → the outbox →
`IntegrationEventAuditHandler`.

`Architecture.Tests` has `Every_integration_event_has_an_audit_handler`, which
caught exactly this gap in spec 028 before it shipped. Any new `*V1` here
inherits that guard, so the audit requirement is structurally enforced rather
than remembered.

Note the two requirements converge: the announcement question 2 needs for
StreamDistribution and the announcement FR-011 needs for audit are **the same
event**. That is why the cross-context phase is not additive cost — without it,
FR-011 needs an event anyway.

---

## Summary of what this research changes

| # | Finding | Status |
|---|---|---|
| 1 | No client-side over-fetch exists; SC-001/SC-002 misdescribe the payoff | **ADOPTED** — SC-001/SC-002 restated, US1 re-justified |
| 2 | An edited address leaves the SFU pulling the old one | **ADOPTED** — now FR-013/FR-013a/FR-014, SC-007/SC-008 |
| 3 | Concurrency infrastructure exists; add `Version` to both camera DTOs | Applied in design |
| 4 | No migration; FR-005 belongs on the aggregate alone | Applied in design |
| 5 | `GetWithinFabAsync` gives FR-006/FR-007 structurally | Applied in design |
| 6 | The audit event and the stream event are one event | Applied in design |

**Both were raised rather than absorbed**, because each changes what the spec says rather than only how it is built. **Finding 2 was adopted** at the Phase 2 gate and is now spec text. **Finding 1 was adopted too**: SC-001 and SC-002 now describe properties of the endpoint rather than a saving that is not available, and US1 is justified by what actually drives it — US2 needs a version, FR-006 needs something to refuse.
