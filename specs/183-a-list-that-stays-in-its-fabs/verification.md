# Verification 183 — A list that stays in its fabs (#2281)

**Latency: N/A.** Identity administrative read paths, not on the event→overlay
path constitution §IV budgets.

## Phase 4a — red (ADR-0139), quoted from commit `f771f1fe`

This is a content-disclosure defect, not a status-code one — all three
endpoints answer `200` before and after the fix. The red is a Dresden
`clientId` present in a Munich-scoped response body:

```
A_munich_operator_listing_kiosks_without_a_fabId_does_not_see_a_dresden_kiosk [FAIL]
Shouldly.ShouldAssertException : ClientIds(rows)
    should not contain
"s183-wall-01a0b4df93a97719b6ab455ff735d002"
    but was actually
["s183-wall-01a0b4df93a97719b6ab455ff735d002", "s183-wall-...", ...]
Additional Info:
    a dresden client must never appear in a munich-scoped list with no fabId
    — this is the disclosure #2281 exists to close

A_munich_operator_listing_devices_without_a_fabId_does_not_see_a_dresden_device [FAIL]
Shouldly.ShouldAssertException : ClientIds(rows)
    should not contain
"plc-s183-01a0b4df92997856923aa08087434fd3"
    but was actually
["plc-s183-01a0b4df92997856923aa08087434fd3", "plc-s183-...", ...]

A_munich_operator_listing_webhook_integrations_without_a_fabId_does_not_see_a_dresden_integration [FAIL]
Shouldly.ShouldAssertException : ClientIds(rows)
    should not contain
"webhook-s183-..."
    but was actually
["webhook-s183-...", ...]
```

I5/I6 (explicit `fabId` behaviour, unchanged by this fix) passed throughout,
as expected.

**A stronger red than the plan anticipated.** `plan.md` §8 expected I4 (the
multi-fab over-narrowing guard) to pass *vacuously* before the fix — it
didn't. Because nothing filtered at all when `fabId` was omitted, a
multi-fab caller (`op-multi`, holding munich + dresden) saw **every** fab's
rows, including berlin's — not just the union of their own two:

```
A_multi_fab_operator_listing_webhook_integrations_without_a_fabId_sees_both_their_fabs_and_no_others [FAIL]
Shouldly.ShouldAssertException : ClientIds(rows)
    should not contain
"webhook-s183-01a0b4df7c8c7c0b9a811fcbfa087ce2"
    but was actually
["webhook-s183-01a0b4df7c8c7c0b9a811fcbfa087ce2", "webhook-s183-...", "webhook-s183-..."]
Additional Info:
    a fab the caller does not hold must stay excluded even for a multi-fab caller
```

(Same shape for kiosks and devices.) This is not part of the phase-4 gate —
the stop-and-diagnose rule is scoped to I1-I3 — but it's stronger evidence
of the disclosure's severity than the spec predicted, and confirms the fix
must narrow to the caller's *set*, not merely add any filter.

Application-level red: 13× `CS9174` (`Cannot initialize type
'Option<FabIdentifier>' with a collection expression because the type is
not constructible`) building `Identity.Application.Tests` against unfixed
`src/` — one per updated query construction, a valid compile-time red for a
signature change (same pattern as spec 180's T003).

## Phase 5 — verification, independently reproduced

Re-run at the final commit (`70a9fe2a`), by the orchestrator rather than
taken on the engineers' reports:

```
Test run for SmartSentinelEye.Integration.Tests.dll (CrossFabListIntegrationTests, filtered)
Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 15 s
```

All 8 (I1-I6 across three endpoints, plus the two multi-fab variants folded
into that count) pass against a freshly-booted `AspireFixture` stack
(ADR-0103) — real tokens minted from Aspire's proxied Keycloak, real HTTP
calls, real Postgres rows. `RegisteredClientConcurrencyIntegrationTests`
(forbidden to touch, run only) — 7/7, confirming the untouched list calls
that already send `?fabId=` keep working unchanged.

Also independently re-verified at the final commit: `dotnet build -c
Release` — 0 errors; `Identity.Application.Tests` — 68/68;
`Architecture.Tests` — 401/401 (`HandlerDeconstructionTests`,
`ConcurrencyConflictDeclarationTests`, `PrimitiveBoundaryTests` included).

**EF translation of `fabs.Contains(client.Fab)`** — confirmed empirically,
not assumed: I1-I3 prove the filter genuinely *excludes* Dresden rows (not
a no-op), and I4's three variants prove a 2-element `fabs` list correctly
returns rows from both held fabs while excluding a third. EF Core throws on
an untranslatable `Where` clause rather than silently falling back to
client-evaluation, so a real, exception-free pass against Postgres is
conclusive. The `Fab` column carries the same `HasConversion` mapping this
codebase already uses in ~18 other handlers (backend-reviewer spot-checked
2 independently and confirmed the count).

## Phase 6 — review, findings closed

Both `security-reviewer` and `backend-reviewer` ran (security review
explicitly required — a confidentiality defect, and the third member of
this defect class after #2240/spec 180 and #2280/spec 182, both merged).
**No blockers from either.**

**Should-fix, closed:**

- **spec.md §7 A3 undercounted which seeded principals hold no fab group**
  (security-reviewer): said exactly one (`service-account-event-ingestion`);
  three do (`service-account-migration-runner`,
  `service-account-identity-admin`, `service-account-event-ingestion` too).
  The conclusion — no live principal is affected by the zero-fab 403 — still
  holds, but for a different and stronger reason: all three carry only
  `sse-identity` + `sse-audience` as default client scopes, and
  `sse-identity` grants no permission, so none of them can pass the scope
  policy on any of the three endpoints at all — they're refused before
  `IdentityFabResolution` is ever reached. Corrected in spec.md.
- **Two OpenAPI `.WithSummary` strings still advertised the removed "optional
  filter" behaviour** (backend-reviewer) — the XML doc comments nearby were
  already corrected, these weren't, and this string is the one an API
  consumer actually reads. Reworded to match the established six-adopter
  wording (`CameraEndpoints.cs`'s "List X in your fabs. Omit fabId to span
  all of them; name one to narrow.").
- **A dangling spec-requirement citation** (backend-reviewer) —
  `IdentityFabResolution.cs` cited "spec 183 FR-001/FR-002", copied verbatim
  from `EventIngestionFabResolution.cs` where those IDs genuinely exist for
  spec 018; spec 183 uses `AS-*`/`SC-*` and has no FRs. Corrected to
  `AS-1`/`AS-4`.
- **Attribution drift in the same comment** (backend-reviewer) — credited
  both `CameraEndpoints` and `EventIngestionFabResolution` equally for the
  all-or-nothing-parsing defect; the defect was CameraCatalog's,
  EventIngestion only carried the fix forward. Reworded.
- **I1/I2/I3's `DistinctFabs` assertion depended on test-ordering side
  effects** (both reviewers, independently) — those three tests only ever
  enrolled a Dresden client, so the assertion that the response's distinct
  fabs equal exactly `{"munich"}` relied on some *other* test in the
  collection having already created a Munich row. On a clean CI volume with
  unlucky ordering (xUnit doesn't guarantee order across the collection),
  this could go red for a reason unrelated to the security property. Fixed:
  each of I1/I2/I3 now explicitly creates its own Munich control row first,
  mirroring I6's already-correct approach.
- **A self-contradicting doc comment** (backend-reviewer) — said the two
  asserted properties were "true today for the wrong reason", when they're
  actually false today, which is why they go red. Corrected.

**Accepted in writing, not fixed:**

- **Neither new branch in `IdentityFabResolution` (the per-entry-skip path,
  the zero-usable-fabs 400) has direct test coverage** (backend-reviewer).
  Mirrors `EventIngestionFabResolution`'s identical, pre-existing gap; both
  branches fail closed; `src/Identity/Api/` sits outside the ADR-0065
  coverage gates (Domain ≥90%, Application ≥80%, Shared ≥90% — Api is
  neither). Not introduced by this change, not newly risky.
- **The `Ensure.That(source).IsNotNull()` guard `plan.md` originally
  specified was omitted** (backend-reviewer) — correctly: `source` was
  never guarded before this change, and adding one now would be a drive-by
  guard (ADR-0036). Only the new `fabs` parameter is guarded.
- **The integration tests authenticate via the legacy `sse.management`
  bundle** (security-reviewer) rather than the granular per-endpoint scopes
  — pre-existing test-fixture behaviour (`AspireFixture.Auth.cs`), not
  introduced here. Confirmed this doesn't mask anything: fab resolution
  reads the `groups` claim independently of any `sse.*` permission scope,
  and no scope registration changed in this diff.
- **Privilege inheritance for hand-created Keycloak accounts**
  (security-reviewer) — any account holding an Identity list scope but no
  `/fabs/*` group membership now gets `403` instead of every fab's rows.
  Correct, fail-closed direction, and confirmed no *seeded* account is
  affected (every realm client carrying an Identity scope also carries
  `sse-groups` as a default client scope). Worth a line for fab operators in
  the PR body, not a code change.
- **The 400's detail can be technically imprecise on one unreachable
  path** (security-reviewer) — inherited verbatim from
  `EventIngestionFabResolution`, unreachable against the seeded realm (every
  group is a valid `FabIdentifier`), and fixing it here alone would create
  the exact copy-drift the shared-file's own doc comment warns against.
- **SC-005's "non-empty" claim rests on the resolver's behaviour, not the
  type alone** (backend-reviewer) — `IReadOnlyList<FabIdentifier>` enforces
  non-optional, not non-empty; an empty list is representable and fails
  closed (zero rows) rather than being rejected by the guard. Harmless,
  noted for precision.

## Not re-audited, correctly out of scope

`#2240` (kiosk/device disable, merged) and `#2280` (webhook rotation,
merged) — confirmed both `Disable` handlers and `WebhookRotationEndpoints.Rotate`
are byte-identical to `develop` throughout this branch's history. No realm
edit, no migration, no `Shared.Contracts` change, no `RegisteredClientSummaryDto`
change, no `src/ServiceDefaults/` change (`FabResolution` used, never
modified — ADR-0114's read half, not its write/inference half).
