# Spec 274: The names a route swallowed

**Issue:** [#2358](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2358)
*A variable named 'snapshot' can be defined and then never read back, because a literal
route shadows it*. **Lane:** supervised (ADR-0037), phases 1-3 in this pass.
**Branch:** `fix-2358-snapshot-route-shadowing`, cut from `origin/develop` at `1a51bd18`.

**Spec number.** 274. Checked 2026-09-27 against `origin/develop` (highest 272), every
remote branch (`git ls-tree <branch> specs/`, highest 272) and every `D:/Github/sse-*`
worktree (highest 273, `sse-2632`: `273-the-places-a-click-keeps`). Re-check before
opening the PR, because an unmerged branch can claim the same number.

**ADRs and constitution sections referenced:** ADR-0070 (Minimal APIs, route groups per
context), ADR-0106 (gateway: path-prefix routing, the budget's hot path is not HTTP),
ADR-0076 (real-time push is SignalR, replaceable), ADR-0145 (a kiosk's fab comes from its
wall; the snapshot read carries `fabId`), ADR-0115 (overlays are fab-neutral templates;
the snapshot resolves per fab), ADR-0114 (fab resolution on reads), ADR-0139 and
constitution §Testing (behaviour-changing, so red first), ADR-0052 / ADR-0103 (xUnit,
Shouldly, Aspire fixture), ADR-0087 (rebase-merge, each commit builds on its own).
Constitution §IV (see §6).

**ADR gap, flagged, not filled.** No ADR records a route convention for "a literal that
would otherwise share a segment with a parameter". The product owner chose the shape for
this context (§3). If `/-/` is meant to become a repo-wide convention, that is an ADR for
a human to write. This spec applies it to SystemVariables only.

---

## 1. The defect

`src/SystemVariables/Api/SystemVariableEndpoints.cs` maps, in one group `/system-variables`:

| Line | Verb | Template | Handler |
|---|---|---|---|
| 54 | GET | `/` | `List` |
| 67 | GET | `/snapshot` | `GetSnapshot` (literal) |
| 81 | GET | `/resolve` | `ResolveText` (literal, spec 148, **shipped**, #2341 closed 2026-09-14) |
| 97 | GET | `/{name}` | `GetOne` (unconstrained parameter) |

ASP.NET Core endpoint routing ranks a literal segment above a parameter segment, so
`GET /system-variables/snapshot` always reaches `GetSnapshot` and
`GET /system-variables/resolve` always reaches `ResolveText`. Both are legal
`VariableName`s: the grammar is `^[A-Za-z][A-Za-z0-9_]{0,63}$`
(`src/SystemVariables/Domain/Variable/VariableName.cs:8`) and nothing reserves them. A
variable named `snapshot` or `resolve` can be defined, set, archived and resolved into
labels, but never read back by name. The read gets `400` (`overlayIdentifier` missing, or
`text must not be empty`) instead of the variable.

Spec 148 shipped `/resolve` on the same shape on purpose, leaving the shadowing for this
issue to fix for both names (issue body, "Why it is filed now").

## 2. Decision (product owner, not re-decided here)

**Move the literal routes off the collision path.** Do not reserve variable names. Do not
move the parameterised read. Apply the same shape to `/snapshot` and `/resolve`.

## 3. The new routes

| Before | After |
|---|---|
| `GET /system-variables/snapshot?overlayIdentifier=&fabId=` | `GET /system-variables/-/snapshot?overlayIdentifier=&fabId=` |
| `GET /system-variables/resolve?text=&fabId=` | `GET /system-variables/-/resolve?text=&fabId=` |

Through the gateway (ADR-0106, `PathRemovePrefix: /system-variables`), the browser-facing
paths become `/system-variables/system-variables/-/snapshot` and `.../-/resolve`. The
gateway's `{**catch-all}` route needs no change.

**Why `-`.** There is no in-repo precedent. Every `*Endpoints.cs` was read (§7) and none
escapes a literal from a parameter. Other contexts avoid the collision with a typed
constraint (`{camera:guid}`, `{eventId:guid}`), which a string name cannot use. So the
choice is made against the grammar and external convention:

1. **`-` can never be a `VariableName`, now or after a grammar relaxation that stays
   identifier-shaped.** The grammar requires a leading letter and does not admit `-`
   anywhere. `_meta` passes only because of the leading-letter rule, since `_` is legal
   from position 2. A sibling segment that is outside the name alphabet stays safe if the
   first-character rule ever relaxes.
2. **It reserves one segment for all present and future non-name reads.** A later literal
   goes under `/-/` and cannot collide, even with a future `GET /{name}/<x>`, because the
   segment `-` is never a name.
3. **External precedent.** GitLab uses exactly this separator (`/<namespace>/<project>/-/issues`)
   to split routes from a user-named path space. `-` is an RFC 3986 unreserved character,
   so it needs no encoding in the RTK Query slice, the gateway or `HttpClient`.
4. **It implies nothing.** `_meta` suggests metadata. The snapshot and the preview are
   derived resolutions, not metadata about the collection.

**No compatibility alias.** Keeping `/snapshot` mapped would keep the shadowing, which is
the defect. After the change, `GET /system-variables/snapshot` is simply `GetOne("snapshot")`.

## 4. User stories

### US1 (P1): an operator reads back a variable whose name was a route

An operator who defined a variable named `snapshot` or `resolve` reads it by name like
any other variable.

**Acceptance scenarios**

```gherkin
Scenario: A variable named after a former literal is readable by name (happy)
  Given variables "snapshot" and "resolve" are Defined in fab "munich"
  When  an operator holding sse.variables.read sends GET /system-variables/snapshot?fabId=munich
  Then  the response is 200 with a VariableDto whose name is "snapshot" and an ETag header
  And   GET /system-variables/resolve?fabId=munich answers the same way for "resolve"

Scenario: The overlay snapshot answers on its new route (happy, moved)
  Given an overlay whose label is "T: {{temperature}}" is in the reverse index for "munich"
  When  the kiosk sends GET /system-variables/-/snapshot?overlayIdentifier=<id>&fabId=munich
  Then  the response is 200 with a ResolvedOverlaySnapshotDto, as before the move

Scenario: The resolve preview answers on its new route (happy, moved)
  When  management-web sends GET /system-variables/-/resolve?text={{temperature}}&fabId=munich
  Then  the response is 200 with a ResolvedTextPreviewDto, as before the move

Scenario: The old snapshot path no longer reaches the snapshot handler (conflict)
  Given no variable named "snapshot" is Defined in "munich"
  When  GET /system-variables/snapshot?overlayIdentifier=<id>&fabId=munich
  Then  the response is 404 VARIABLE_NOT_FOUND from GetOne, not a snapshot

Scenario: Bad requests on the moved routes keep their answers (bad request)
  When  GET /system-variables/-/snapshot with overlayIdentifier = Guid.Empty
  Then  400 VARIABLE_INVALID_INPUT
  When  GET /system-variables/-/resolve?text=
  Then  400 VARIABLE_INVALID_INPUT

Scenario: The moved routes keep the read scope (auth)
  Given a caller authenticated without sse.variables.read
  When  it sends GET /system-variables/-/snapshot or GET /system-variables/-/resolve
  Then  403, and the endpoint metadata still names Scope.Sse.Variables.Read
```

**Independent end-to-end test.** Boot the stack. As admin, `POST /system-variables`
`{name:"snapshot", type:"Text"}` with `?fabId=munich`, then
`GET /system-variables/snapshot?fabId=munich` and expect 200 with `name == "snapshot"`.
On develop today the same request answers 400 (`overlayIdentifier`). Then open a kiosk
wall with a placeholder label. The opening label must render resolved, which proves the
kiosk reached `/-/snapshot`. In management-web, type `{{temperature}}` into the overlay
editor. The preview must resolve, which proves `/-/resolve`.

## 5. Out of scope

- Reserving names, or migrating an existing variable named `snapshot`/`resolve`. None is
  needed: after the move such a variable becomes readable with no data change. That
  answers the issue's open question about already-deployed shadowed names.
- The other contexts (§7 found no instance of the same-verb shape).
- A repo-wide guard or ADR for the convention (§ ADR gap).
- Editing ADR-0115:23 and ADR-0145:41, which cite `GET /system-variables/snapshot`.
  They are decision records of their date. The plan lists them for the reviewer.
- Historical specs (005, 011, 067, 148, ...) that name the old paths.

## 6. Latency budget (constitution §IV)

**N/A. Not on the event-to-overlay path.** Neither moved handler sits between event
arrival and overlay render:

- A live label change goes through domain event → reverse index → resolve → integration
  event → SignalR push. The kiosk writes the push straight into the RTK Query cache with
  `upsertQueryData('getOverlaySnapshot', ...)` (`apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts:196-206`),
  **with no HTTP request**. ADR-0106 records that the budget's hot path is not HTTP
  request/response.
- `GET .../snapshot` is fetched only on tile mount and on cache invalidation, for cold
  load and reconnect reconciliation (`LayoutGrid.tsx:381`, `providesTags` `ALL`). That is
  the label before any live update arrives, an on-demand read.
- `GET .../resolve` serves the management-web editor preview and is never called by the wall.

**One caveat to cite in the PR.** `NFR_VariableResolutionLatencyTests` uses the snapshot
GET as its **measurement probe** for this context's share of the *Event → overlay state*
leg (its loop polls `OverlaySnapshotReadiness.ResolvedTextAsync`). The probe's URL moves
and the measured work does not. Endpoint routing resolves both templates with the same
DFA walk, so no change is expected. Phase 5 still compares that test's recorded figure
from the PR's CI run with the latest green `develop` run (TRX artifact).

## 7. Other contexts (the issue's "worth checking")

Every `MapGet/Post/Put/Patch/Delete` in `src/*/Api/*Endpoints.cs` was read. **No other
context has a literal and an unconstrained parameter competing for the same segment
under the same verb.** The near-misses and why they are not the defect:

| File:line | Shape | Why not shadowed |
|---|---|---|
| `EventIngestion/Api/EventsEndpoints.cs:88,97` | GET `/{eventId:guid}` + GET `/dead-letters` | `guid` constraint excludes the literal |
| `AuditObservability/Api/AuditEndpoints.cs:40,48` | GET `/{kind}/{id}` + GET `/{auditIdentifier:guid}` | different segment counts; guid constrained |
| `Identity/Api/DevicesEndpoints.cs:41,53` | POST `/register` + DELETE `/{clientId}` | different verbs, so the method policy selects before precedence |
| `Identity/Api/KiosksEndpoints.cs:41,53` | POST `/enroll` + DELETE `/{clientId}` | different verbs |
| `StreamDistribution/Api/StreamEndpoints.cs:34,63,92` | GET `/{cameraIdentifier:guid}` + POST `/authorize`, `/kiosk-latency` | constrained, and different verbs |

No follow-up issues are proposed.

## 8. Assumptions (marked, not buried)

- **A1: no consumer outside this repository calls either route.** Grep-verified in-repo.
  External integrators use webhooks and events, not these reads. If one exists, it breaks
  with 404/400 and there is no alias.
- **A2: version skew during rollout is tolerable.** A kiosk still running the previous
  bundle calls `/snapshot`, gets `404` from `GetOne`, and falls back to the published
  label text (`LayoutGrid.tsx:389`). Live pushes still upsert the cache, so the tile
  corrects itself on the first variable change or the next reload. That is degraded, not
  broken.
