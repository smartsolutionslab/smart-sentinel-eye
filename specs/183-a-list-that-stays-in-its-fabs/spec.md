# Spec 183 — A list that stays in its fabs

**Issue:** [#2281](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2281)
— **already on Project #13 (title "Smart Sentinel Eye"), status Todo**, labels
`bug` + `agent:ready`, no `agent:blocked`. Verified 2026-09-18 by a GraphQL read
of the issue's own `projectItems` (project number **13**, Status field value
**Todo**), not by a board dump — Projects v2 has its own rate limit and two dumps
exhaust it. **No `item-add` needed.**

**Spec number 183 confirmed.** `git ls-tree -d origin/develop -- specs/` ends at
**181**; **182** is claimed by the unmerged branch `2280-webhook-rotation-fab-check`
(`specs/182-a-rotation-that-cannot-cross-a-fab`, PR #2455). 183 is the first free
number against develop *and* every live sibling branch.

**ADRs referenced:** ADR-0114 (fab resolution — the `FabResolution` decision
table, read half), ADR-0044 (per-context `FabIdentifier`), ADR-0070 (Minimal
APIs), ADR-0047 / ADR-0089 (`Result<T, Error>` + `ApiError`), ADR-0105
(`Ensure.That`), ADR-0141 (`Option<T>` for absences — and why one disappears
here), ADR-0103 (integration via the Aspire fixture), ADR-0052 / ADR-0053 /
ADR-0054 (test framework, naming, builders), ADR-0139 + constitution §Testing
(observed red), ADR-0036 (smallest change), ADR-0037 / ADR-0144 (the lane).

**Constitution:** §VIII — *"**Authorization** is enforced by scope checks at
every endpoint, plus fab-group membership."* Three read endpoints check the scope
and never consult the membership. This spec does not amend that sentence; it
makes it true of them.

**Latency budget (§IV): N/A.** Identity is an administrative read path. None of
the six legs of `event arrival → overlay rendered` is touched, and no leg's state
in §IV's table changes.

---

## 1. The defect, confirmed by content rather than by line number

The issue cites four locations. Every one is real; three line numbers have
drifted by one to four lines. Confirmed on `develop` at `10c96740`, 2026-09-18:

| Cited | Actual | Content |
|---|---|---|
| `KiosksEndpoints.cs:84-86` | **`src/Identity/Api/KiosksEndpoints.cs:85-89`** | `if (string.IsNullOrWhiteSpace(fabId)) { fab = Option<FabIdentifier>.None; }` |
| `DevicesEndpoints.cs:84-86` | **`src/Identity/Api/DevicesEndpoints.cs:85-89`** | identical |
| `WebhookRotationEndpoints.cs:75-77` | **`src/Identity/Api/WebhookRotationEndpoints.cs:74-78`** | identical |
| `RegisteredClientProjection.cs:27-31` | **`src/Identity/Application/Queries/Handlers/RegisteredClientProjection.cs:27-31`** | `if (fab.HasValue) { … query = query.Where(client => client.Fab == wanted); }` |

The two halves compose into the disclosure: an omitted `fabId` becomes
`Option.None`, `Option.None` skips the `Where`, and the projection returns every
fab's rows. **Neither half is wrong on its own** — an optional filter that filters
when set is unremarkable — which is exactly why this survived three endpoints and
two specs of adjacent review.

**None of the three `List` handlers reads the caller's fab claim.** They do take
`IFabAuthorizationGuard`, but call it *only* inside the `else` branch, against a
fab the caller typed. With no `fabId` the guard is never invoked at all.

**What is disclosed.** `RegisteredClientSummaryDto` carries
`RegisteredClientIdentifier`, `Version`, `ClientId`, `Kind`, `Fab`,
`RegisteredAt`, `RegisteredBy`, `DisabledAt`, `LastRotatedAt` — for every fab.
The client secret is not in it and never was. What leaks is the **target list**
(#2240's `DELETE /kiosks/{clientId}` needed a `clientId` to aim at) and the
**version oracle** (#2280's rotation needs the `Version` for `If-Match`).

**Reachability.** `RequireScopeExtensions.LegacyManagementBundle` makes
`sse.management` satisfy every `sse.*` policy except `sse.events.publish`, and
`smart-sentinel-eye-web` carries `sse.management` as a default client scope. So
**every seeded operator in the realm can already call all three routes** — no
specially-scoped principal is required to reproduce this.

**The precedent Identity never adopted.** `FabResolution.ResolveForReadAsync`
(`src/ServiceDefaults/Authorization/FabResolution.cs:94-120`) exists for exactly
this and is used at **six** call sites in five other contexts:
`CameraCatalog/Api/CameraEndpoints.cs:491`, `StreamDistribution/Api/StreamEndpoints.cs:323`,
`EventIngestion/Api/EventIngestionFabResolution.cs:79`,
`LayoutComposition/Api/LayoutEndpoints.Commands.cs:90`,
`Automation/Api/RulesEndpoints.cs:239`, `SystemVariables/Api/SystemVariableEndpoints.cs:503`.
**Identity has zero.**

## 2. Scope

**In scope:** the three Identity `List` endpoints, the three list queries, and
the one shared projection they all run through.

**Out of scope, explicitly:** the disable paths (#2240, spec 180 — merged) and
the webhook rotation path (#2280, spec 182 — PR #2455, unmerged). No re-audit of
either, and no folding in. Nothing in `src/ServiceDefaults/` is modified —
`FabResolution` is *used*, exactly as the other six sites use it.

**This is one slice, not three.** Investigated rather than assumed: the three
endpoints share `RegisteredClientProjection.ListAsync` — one filter, one place —
and their three query records are the same shape with a different `ClientKind`.
Splitting them would ship a release in which two thirds of the disclosure is
live, and the second and third PRs would re-touch the very file the first
changed. The `[P]` markers in `tasks.md` reflect that: there is almost no
parallelism here, and that is a property of the code sharing, not a failure to
decompose.

## 3. User stories

### US1 (P1) — A list is confined to the fabs the caller holds

*As an operator holding `sse.identity.kiosks.read` / `.devices.read` /
`sse.webhooks.write` in some fabs, when I list clients without naming a fab I see
every client in **my** fabs and none from any other; and when I name one of my
fabs I see only that one.*

Independently shippable and independently observable: two `curl`s against the
running stack — one showing another fab's `clientId` in the body today, one
showing it absent afterwards. Nothing else in the repo waits on it.

**This is the whole of P1. There is no P2.**

## 4. Acceptance scenarios (Gherkin)

Written for `/kiosks`. **Each holds identically for `/devices` and for
`/webhook-integrations`**, substituting the endpoint's own `ClientKind` and its
own scope (`sse.identity.devices.read`, `sse.webhooks.write`).

### AS-1 — happy path: an omitted fab means *my* fabs, not all fabs

```gherkin
Given a kiosk "wall-munich" enrolled in fab "munich"
  And a kiosk "wall-dresden" enrolled in fab "dresden"
  And an operator whose groups claim contains only "/fabs/munich"
When they send GET /kiosks with no fabId
Then the response is 200
  And the body contains "wall-munich"
  And the body does not contain "wall-dresden"
  And every row's fab is "munich"
```

### AS-2 — the disclosure, stated as the thing that must stop

```gherkin
Given a kiosk "wall-dresden" enrolled in fab "dresden"
  And an operator whose groups claim contains only "/fabs/munich"
When they send GET /kiosks with no fabId
Then no row in the body has fab "dresden"
  And no row in the body has clientId "wall-dresden"
```

**AS-2 is the red.** Today the response is `200` with `wall-dresden` in it. The
phase-4a evidence is that `clientId` present in a Munich-scoped body — not a
missing `Where`, not a count, not an absent guard call.

### AS-3 — a multi-fab caller still spans all of theirs (the fix must not over-narrow)

```gherkin
Given kiosks enrolled in "munich", "dresden" and "berlin"
  And an operator whose groups claim contains "/fabs/munich" and "/fabs/dresden"
When they send GET /kiosks with no fabId
Then the body contains the munich kiosk and the dresden kiosk
  And the body contains no berlin kiosk
```

A fix that resolved a *single* fab would break this, and the realm seeds
`op-multi@smart-sentinel-eye.test` (`/fabs/munich` + `/fabs/dresden`) precisely so
it cannot be broken silently. This is the deliberate read/write asymmetry
`FabResolution` documents: *"Unlike a write, nothing has to be chosen: a
multi-fab caller listing sees all of theirs."*

### AS-4 — naming a fab still behaves exactly as today

```gherkin
Given an operator whose groups claim contains only "/fabs/munich"
When they send GET /kiosks?fabId=munich
Then the response is 200 and contains only munich rows

When they send GET /kiosks?fabId=dresden
Then the response is 403 with title "RESOURCE_FAB_NOT_AUTHORIZED"
```

### AS-5 — bad request, **including one deliberate status change**

```gherkin
Given an operator in fab "munich"
When they send GET /kiosks?fabId=
Then the response is 200 and is treated as "no fab named" (unchanged —
  IsNullOrWhiteSpace has always collapsed blank to omitted)
```

```gherkin
Given an operator in fab "munich"
When they send GET /kiosks?fabId=NOT%20A%20FAB
Then the response is 403 with title "RESOURCE_FAB_NOT_AUTHORIZED"
```

**Today that second case is `400 KIOSK_INVALID_INPUT`, and after this change it
is `403`.** Deliberate, not incidental: `ResolveForReadAsync` hands the raw string
to `EnsureAccessAsync` before anything parses it, which is what
`CameraEndpoints.List:390-394` and all five other adopters already do — they pass
`fabId` through untouched. Keeping a pre-parse would make Identity the only
context where a malformed fab name is distinguishable from a well-formed one the
caller does not hold, which is a (weak) oracle and a gratuitous divergence. The
change is recorded here so phase 6 meets it as a decision rather than a defect.

### AS-6 — auth

```gherkin
Given a caller with no bearer token
When they send GET /kiosks
Then the response is 401
```

```gherkin
Given a caller authenticated without sse.identity.kiosks.read
  And without the legacy sse.management bundle
When they send GET /kiosks
Then the response is 403, from the scope policy, before any fab is read
```

```gherkin
Given a caller holding the scope but belonging to no fab group at all
When they send GET /kiosks with no fabId
Then the response is 403 with title "RESOURCE_FAB_NOT_AUTHORIZED"
  And the detail is "Caller is not a member of any fab."
```

The last row is a **behaviour change from `200` + every fab's rows**, and is the
existing, deliberate contract of `ResolveForReadAsync`: *"an empty answer would
read as 'there is nothing here' when what is true is 'you are not configured to
see anything'"*. It is already covered by `FabResolutionTests` in
`tests/ServiceDefaults.Tests/Authorization/`; no new integration test is owed for
it (§7 A3 measures who this can affect).

### AS-7 — unchanged behaviour that must stay unchanged

```gherkin
Given no kiosk exists in fab "munich"
When a munich operator sends GET /kiosks with no fabId
Then the response is 200 with an empty array (not 404, not 403)
```

```gherkin
Given a device and a webhook client exist in fab "munich"
When a munich operator sends GET /kiosks with no fabId
Then neither appears — the ClientKind filter is unchanged and still applies first
```

```gherkin
Given a webhook client in fab "munich" at version 3
When a munich admin sends GET /webhook-integrations with no fabId
Then the row carries Version 3, so the If-Match rotation flow still works
```

## 5. Independent end-to-end test procedure

Run against a **freshly-booted** Aspire stack. A persistent `dotnet run` serves
whatever binaries it loaded at boot, so a stack older than this change's commit
shows the pre-fix behaviour no matter what is on disk — check the AppHost
process's start time against the commit before trusting any manual result.

1. Mint tokens from Aspire's **proxied** Keycloak endpoint (never the container's
   mapped port — a mismatched issuer 401s everything):

   ```sh
   # management-web carries all four sse.identity.* scopes plus sse-groups as
   # default scopes and enables the direct access grant.
   mint() { curl -sk -d grant_type=password -d client_id=management-web \
     -d "username=$1" -d "password=$2" \
     "$KC/realms/smart-sentinel-eye/protocol/openid-connect/token" | \
     sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p'; }
   MUNICH=$(mint admin@munich.test Admin1234)
   DRESDEN=$(mint op-dresden@dresden.test Operator1234)
   MULTI=$(mint op-multi@smart-sentinel-eye.test Operator1234)
   ```

2. As **dresden**, enrol a throwaway kiosk:
   `POST $IDENTITY/kiosks/enroll?fabId=dresden` with
   `{"clientId":"s183-wall-<guid>"}` → expect `201`.
3. **Before the fix**, as **munich**: `GET $IDENTITY/kiosks` (no `fabId`) →
   observe `200` **with `s183-wall-<guid>` and `"fab":"dresden"` in the body**.
   *That is the disclosure, observed rather than argued.*
4. **After the fix**, repeat → `200`, and `s183-wall-<guid>` is **absent**; every
   row reads `"fab":"munich"`.
5. As **multi**: `GET $IDENTITY/kiosks` → the dresden kiosk **is** present
   alongside munich's, and no berlin row is. (AS-3 — the over-narrowing check.)
6. As **munich**: `GET $IDENTITY/kiosks?fabId=dresden` → `403
   RESOURCE_FAB_NOT_AUTHORIZED` (unchanged); `?fabId=munich` → munich rows only.
7. Repeat 2-6 for `GET /devices` (create via `POST /devices/register?fabId=dresden`)
   and for `GET /webhook-integrations` (create via
   `POST /webhook-integrations/<name>/rotate` with `If-None-Match: *` and
   `{"fabId":"dresden"}`).

Realm facts this rests on, read from
`src/AppHost/Realms/smart-sentinel-eye-realm.json`: groups `/fabs/munich`,
`/fabs/dresden`, `/fabs/berlin`, `/fabs/hamburg`; `admin@munich.test` holds munich
only; `op-dresden@dresden.test` holds dresden only; `op-multi@smart-sentinel-eye.test`
holds munich **and** dresden (`Operator1234`); `op-berlin@berlin.test` holds berlin
only. **No realm edit is needed** — which matters, because a realm edit requires
deleting the Keycloak volume and a mere restart leaves the old realm in place
while the stack still looks perfectly healthy.

## 6. Locked tech choices (no new ones)

| Concern | Choice | Source |
|---|---|---|
| Fab resolution for a read | `FabResolution.ResolveForReadAsync` | ADR-0114's table, read half; 6 existing call sites |
| Per-context binding | an Identity-local `ResolveReadFabsAsync` wrapper | `EventIngestionFabResolution` — one copy shared by three endpoint files |
| Fab type | Identity's own `FabIdentifier` | ADR-0044; `ServiceDefaults` returns `string`, the caller parses |
| Filter | `fabs.Contains(client.Fab)`, **unconditional** | `ListCamerasQueryHandler:46`, `ListStreamsQueryHandler:37` |
| Refusal for a fab you lack | `403 RESOURCE_FAB_NOT_AUTHORIZED` via the global handler | `FabAuthorizationExceptionHandler` |
| Refusal for no fab membership | `403`, detail "Caller is not a member of any fab." | same handler, `ForNoFabMembership` |
| API style | Minimal APIs | ADR-0070 |
| Errors | `Result<T, Error>` + `ApiError` | ADR-0047, ADR-0089 |
| Guards | `Ensure.That(...)` | ADR-0105 |
| Tests | xUnit + Shouldly + Moq; integration on `AspireFixture` | ADR-0052, ADR-0103 |

## 7. Assumptions, marked

- **A1 — adopting `ResolveForReadAsync` here needs no new ADR, and the reasoning
  is not "six sites already do it".** ADR-0114's Consequences confine *inference*
  — the write half, which **selects** a fab the caller did not name — to Automation
  and (by its 2026-08-05 amendment) SystemVariables, and say extending that is a
  new decision. The read half selects nothing: it **narrows** a query from "every
  fab" to "the fabs this caller already holds", and can only ever remove rows. It
  cannot grant access, so it cannot be the licence ADR-0114 withholds. The issue's
  own "Done looks like" names this helper. **Marked as an assumption anyway**,
  because ADR-0114's text does not draw the write/read distinction in so many
  words: if phase 4 or 6 concludes an ADR *is* owed, that is a **blocked** outcome
  (ADR-0144 — the lane implements decisions, it does not make them), not something
  to decide in passing. A follow-up to record the read half's scope explicitly in
  ADR-0114 is worth filing either way; it is not this issue.
- **A2 — no caller breaks.** Measured, not assumed, with two patterns each over
  `apps/`, `tests/` and `src/`: **zero** frontend callers of `GET /kiosks`,
  `GET /devices` or Identity's `GET /webhook-integrations` (no RTK Query endpoint,
  no Playwright spec), and **zero** service-to-service callers. The only HTTP
  callers in the repo are `RegisteredClientConcurrencyIntegrationTests:214` and
  `:230`, and **both already send `?fabId=`**, so both keep working unchanged.
  (`EventIngestion`'s `/webhook-integrations` hits are a different context on a
  different service and are untouched.)
- **A3 — the zero-fab 403 affects no live principal.** Corrected during phase 6
  review, which found this originally undercounted: **three** seeded principals
  hold no fab group, not one —
  `service-account-migration-runner`, `service-account-identity-admin` and
  `service-account-event-ingestion` (`src/AppHost/Realms/smart-sentinel-eye-realm.json`).
  The conclusion still holds, for a different and stronger reason than first
  written: all three carry only `sse-identity` + `sse-audience` as default client
  scopes, and `sse-identity` grants no permission — so none of them can pass the
  scope policy on `/kiosks`, `/devices` or `/webhook-integrations` at all. They
  are refused before `IdentityFabResolution` is ever reached; the zero-fab 403 is
  unreachable for them specifically because the request never gets that far.
- **A4 — the response body shape is unchanged.** `RegisteredClientSummaryDto` is
  not touched. Fewer rows, same rows.
- **A5 — `fabs.Contains(client.Fab)` translates.** Identity's `Fab` column carries
  the identical `HasConversion(fab => fab.Value, value => FabIdentifier.From(value))`
  shape as CameraCatalog's, where the same predicate runs in production. Phase 4
  proves it on the real stack rather than resting on the comparison.

## 8. Success criteria

- **SC-001** — AS-2 holds over real HTTP against the real stack: a Dresden
  `clientId` is **in** the Munich-scoped body before the fix and **absent** after,
  on all three endpoints.
- **SC-002** — that test is observed **failing** against unfixed code and the
  failure is quoted verbatim in the PR (ADR-0139). A green-on-arrival test here is
  a phase-4 failure, not a shortcut.
- **SC-003** — AS-3 passes: `op-multi` still sees both their fabs. The fix
  narrows to the caller's fabs, not to one fab.
- **SC-004** — AS-4, AS-5's blank case and AS-7 are unchanged; the `ClientKind`
  filter still applies first and the webhook `Version` still rides the row.
- **SC-005** — after the change there is **no representable "unfiltered" state**:
  the three query records carry a non-optional, non-empty
  `IReadOnlyList<FabIdentifier>`, so a future endpoint cannot reintroduce the
  defect by forgetting a branch. Structural, in the sense
  `ICameraRepository.GetWithinFabAsync`'s doc comment means it — *"a caller cannot
  forget the check"*.
- **SC-006** — `Architecture.Tests` and the full `Identity.*` suites stay green;
  the two existing `?fabId=`-sending integration callers are not edited.
