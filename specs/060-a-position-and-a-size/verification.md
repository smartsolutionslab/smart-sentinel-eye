# Verification: 060 — a position and a size

**Feature**: 060 | **Issue**: #2051 | **Branch**: `refactor/2051-a-position-and-a-size`
| **HEAD at observation**: `b23b905` | **Date**: 2026-09-04

Phase 5 (ADR-0037). Phase 4b already ran the suites, so the question here is not
"do the tests pass". For a behaviour-preserving refactor the question is **did
anything observable actually change**, and the only way to answer it is to ask
the running system.

Everything below was observed on **Windows 11, .NET 10.0.400, Aspire run mode**
against a live AppHost (`dotnet run --project src/AppHost -c Release
--launch-profile http`) attached to the persistent dev containers
(`postgres-18bcf406`, `keycloak-18bcf406`, …). Endpoints as Aspire proxied them
for that run: overlay-designer `http://localhost:5288`, layout-composition
`http://localhost:5245`, camera-catalog `http://localhost:5183`, Keycloak
`https://localhost:10756`. Tokens were minted from the **proxied** Keycloak
endpoint via the password grant (`admin` / `sse.management`).

## Result

Four of the five observable surfaces are **identical**: the request body, the
response body, the SignalR frame, and the four database columns. The fifth — the
`400` `detail` for an out-of-range **extent** — **changed**, and §3 below is that
finding. It was declared in advance (research R2) and it is larger than every
artefact describing it says. `spec.md` FR-007 and the User Story 2 preamble still
assert that no observable change exists at all.

Nothing was fixed here. Phase 5 records; the discrepancy is a decision for a
human.

---

## 1. The wire shape is byte-identical

`LabelRequest`, `OverlayDto` and `Shared.Contracts` are untouched by the diff —

```sh
git diff origin/develop...HEAD --stat -- src/OverlayDesigner/Api/Requests \
  src/OverlayDesigner/Application/DTOs src/Shared.Contracts
# (no output)
```

— and so is every frontend reader. Ten files under `apps/` read
`normalizedX`/`normalizedY`/`normalizedWidth`/`normalizedHeight`
(`apps/shared/src/api/overlays.schema.ts`, `apps/shared/src/realtime/layoutHub.ts`,
`apps/kiosk-web/src/features/cell/CellPage.tsx`,
`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx`, …); none
appears in the diff.

That is the diff argument. Here is the running system.

**Request sent** (`POST /overlays`, four deliberately distinct values so a
transposition could not hide):

```json
{
  "name": "Vfy060-012352",
  "label": {
    "text": "Production Line 1",
    "normalizedX": 0.125,
    "normalizedY": 0.25,
    "normalizedWidth": 0.375,
    "normalizedHeight": 0.5,
    "fontSizePx": 48
  }
}
```

**Accepted**:

```
HTTP/1.1 201 Created
Location: /overlays/01a06995-fe77-73c6-82ae-a2ab380f42ef
"01a06995-fe77-73c6-82ae-a2ab380f42ef"
```

**Read back** (`GET /overlays/01a06995-fe77-73c6-82ae-a2ab380f42ef`):

```json
{"overlayIdentifier":"01a06995-fe77-73c6-82ae-a2ab380f42ef","version":0,"name":"Vfy060-012352","createdAt":"2026-09-03T23:23:52.822901+00:00","createdBy":"4ddca022-4c73-40ab-add9-df7571afaacf","revisions":[{"revisionIdentifier":"01a06995-fe79-7d9f-91dd-79bfbfb7594b","revisionNumber":1,"state":"Draft","text":"Production Line 1","normalizedX":0.125,"normalizedY":0.25,"normalizedWidth":0.375,"normalizedHeight":0.5,"fontSizePx":48,"createdAt":"2026-09-03T23:23:52.822901+00:00","createdBy":"4ddca022-4c73-40ab-add9-df7571afaacf","publishedAt":null,"archivedAt":null}]}
```

Four field names, camelCase, unchanged; four values, each on its own field,
unchanged. `GET /overlays?page=1&pageSize=1&search=Vfy060` (the list projection,
a second call site of the same handler shape) returned the same field names and
values.

## 2. The database is unchanged

Asked of the live Postgres, not of the model:

```sh
docker exec -e PGPASSWORD=… postgres-18bcf406 psql -U postgres -d overlay-designer-db \
  -c "select column_name, data_type, numeric_precision, numeric_scale, is_nullable, ordinal_position
      from information_schema.columns where table_name='overlay_revisions' order by ordinal_position;"
```

```
    column_name     |        data_type         | numeric_precision | numeric_scale | is_nullable | ordinal_position
--------------------+--------------------------+-------------------+---------------+-------------+------------------
 revision_id        | uuid                     |                   |               | NO          |                1
 revision_number    | integer                  |                32 |             0 | NO          |                2
 state              | character varying        |                   |               | NO          |                3
 label_text         | character varying        |                   |               | NO          |                4
 label_x            | numeric                  |                   |               | NO          |                5
 label_y            | numeric                  |                   |               | NO          |                6
 label_width        | numeric                  |                   |               | NO          |                7
 label_height       | numeric                  |                   |               | NO          |                8
 label_font_size_px | integer                  |                32 |             0 | NO          |                9
 created_at         | timestamp with time zone |                   |               | NO          |               10
 created_by         | uuid                     |                   |               | NO          |               11
 published_at       | timestamp with time zone |                   |               | YES         |               12
 archived_at        | timestamp with time zone |                   |               | YES         |               13
 overlay_id         | uuid                     |                   |               | NO          |               14
```

Four columns, same names, `numeric`, `NOT NULL`, same ordinal positions — **not**
the `Position_X` / nullable shape an unconfigured owned reference would have
produced (#2022).

The point that makes this decisive: **this schema predates the refactor.** No
migration was added (`git diff --stat` over
`src/OverlayDesigner/Infrastructure/Persistence/Migrations` is empty), and the
history in the live database still ends where it did:

```
              MigrationId
---------------------------------------
 20260903094843_AddIdempotencyKey
 20260527135708_InitialOverlayDesigner
```

So the branch's model wrote and read through the columns created by
`20260527135708_InitialOverlayDesigner`. Independently confirmed against the
model:

```sh
dotnet ef migrations has-pending-model-changes \
  --project src/OverlayDesigner/Infrastructure/…Infrastructure.csproj \
  --startup-project src/MigrationRunner/…MigrationRunner.csproj \
  --context OverlayDesignerDbContext
# No changes have been made to the model since the last migration.
```

The persisted rows for the overlay driven above:

```
 revision_number |   state   |    label_text     | label_x | label_y | label_width | label_height | label_font_size_px
-----------------+-----------+-------------------+---------+---------+-------------+--------------+--------------------
               1 | Archived  | Production Line 1 |   0.125 |    0.25 |       0.375 |          0.5 |                 48
               2 | Published | Production Line 2 |  0.0625 |    0.75 |         0.9 |        0.125 |                 24
```

## 3. The 400 — **one of the four messages changed**

Four live requests, each with one field out of range. Bodies quoted verbatim.

```
--- normalizedY = 2 ---
HTTP/1.1 400 Bad Request
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"OVERLAY_INVALID_INPUT","status":400,"detail":"normalizedY must be in [0, 1]; got 2. (Parameter 'normalizedY')","traceId":"00-ecd69d8c0939382d0787ca6a7f248085-b1e8e258b1d6e35b-01"}

--- normalizedX = -1 ---
{"…","title":"OVERLAY_INVALID_INPUT","status":400,"detail":"normalizedX must be in [0, 1]; got -1. (Parameter 'normalizedX')","…"}

--- normalizedY = 1.01 ---
{"…","title":"OVERLAY_INVALID_INPUT","status":400,"detail":"normalizedY must be in [0, 1]; got 1,01. (Parameter 'normalizedY')","…"}

--- normalizedWidth = 0 ---
{"…","title":"OVERLAY_INVALID_INPUT","status":400,"detail":"normalizedWidth: must be in (0, 1]. (Parameter 'normalizedWidth')","…"}
```

**Culture**: `1,01`, with a comma. This host is comma-decimal and
`decimal.ToString()` follows the API process's culture, exactly as phase 4a
found. `OverlayGeometryValidationIntegrationTests` asserts only whole-number
cases for that reason, and it is right to.

**The two coordinates are byte-identical.** `EnsuredValue<T>.InRange(0m, 1m)`
formats `$"{parameter} must be in [{minimum}, {maximum}]; got {value}."`, which
reproduces `develop`'s `Label.EnsureNormalized` character for character, and the
`(Parameter '…')` suffix and `ArgumentException` type survive.

**The two extents did not.** `develop`'s `Label.EnsurePositiveNormalized` throws
`$"{parameter} must be in (0, 1]; got {value}."`, so the same request on
`develop` produces:

```
normalizedWidth must be in (0, 1]; got 0. (Parameter 'normalizedWidth')
```

against the branch's

```
normalizedWidth: must be in (0, 1]. (Parameter 'normalizedWidth')
```

Two differences, not one: a colon replaces a space, **and the offending value is
no longer echoed**. A caller who could previously read what they sent out of the
`detail` now cannot.

**Why this is a finding and not merely a note.** The delta was declared —
research R2 rules "accept the colon", `tasks.md` T502 specifies the literal
`"must be in (0, 1]."`, and the implementation follows T502 faithfully. But every
prose description of it is narrower than the thing itself:

| Artefact | What it says | Actual |
|---|---|---|
| `spec.md` FR-007 | message text for **each of the four** cases MUST be unchanged | changed for two |
| `spec.md` US2 preamble | "Nothing an operator, a kiosk, or an HTTP client can observe changes" | an HTTP client can observe this |
| `spec.md` US2 AS4 | "the same detail text as before" | not the same, for extents |
| `spec.md` assumptions | FR-007 "satisfied … for the extents by writing the `Satisfies` message to match" | the message does not match |
| `plan.md` L138, `research.md` R2 | "the message gains a colon" / "a colon where the original had a space" | colon **plus** the dropped `; got {value}` |
| `tasks.md` T502 | the literal `"must be in (0, 1]."` | implemented as written |

FR-007 was never amended in `spec.md`; R2 amended it in `research.md` only, and
described a smaller change than it authorised. This is the shape of defect this
repository keeps having to correct — a record that describes something narrower
than what happened — so it is written down rather than waved through.

The contract-visible parts are unchanged: status `400`, `title`
`OVERLAY_INVALID_INPUT`, `paramName`, exception type, and the accept/reject
boundary (`0` still refused, `(0, 1]` still enforced; `0`/`1` bounds still
accepted).

**Provenance**: the branch's four bodies were observed live. `develop`'s message
was **read from source** (`git show origin/develop:…/Label.cs`), not observed
from a running `develop` stack. It is a literal interpolation with no branching,
so the reading is unambiguous — but it is a reading.

## 4. The SignalR frame still carries all four

Observed by hand, not through the test's `HubConnection`: a camera was
registered, a 1×1 layout referencing the overlay was created and published (so
this fab is among those told about it — spec 017 FR-010/FR-011), and a raw
WebSocket client negotiated and connected to `layout-composition`'s
`/hubs/layouts` with the admin bearer token and the JSON protocol handshake.
Revision 3 was then branched and published over HTTP.

```
NEGOTIATE OK connectionId=diw6nZA1HnH6XwfDlOCWPA
WS OPEN, handshake sent
MSG {}
…
FRAME target=OverlayRevisionArchived
FRAME payload={"overlay":"01a06995-fe77-73c6-82ae-a2ab380f42ef","revisionNumber":2,"archivedAt":"2026-09-03T23:27:07.7041339+00:00"}
FRAME target=OverlayRevisionPublished
FRAME payload={"overlay":"01a06995-fe77-73c6-82ae-a2ab380f42ef","revisionNumber":3,"name":"Vfy060-012352","text":"Production Line 2","normalizedX":0.0625,"normalizedY":0.75,"normalizedWidth":0.9,"normalizedHeight":0.125,"fontSizePx":24,"publishedAt":"2026-09-03T23:27:07.7041339+00:00"}
```

All four names present, camelCase, values equal to what revision 2 held — which
they had to be, because revision 3 is a *branched copy* of it. That makes this
frame a second, independent observation of §6.

---

## 5. The private EF materialization constructor — is the `null!` window reachable?

```csharp
private Label(string text, int fontSizePx) : this(text, null!, null!, fontSizePx)
```

**A real read path does traverse it, and it closes.** Every `GET` above is an EF
materialization (`OverlayQuerySource` hands out `dbContext.Overlays.AsNoTracking()`
and the query handler reads `revision.Label.Position.X` etc.). A `Label` escaping
with a null `Position` would have surfaced as a `NullReferenceException` in the
projection; instead the four numbers came back. Position and Size are populated
by the time anything reads them.

**Could they not be?** Not from persisted state. The two navigations are mapped
as table-splitting owned references with `Navigation(…).IsRequired()` onto four
`NOT NULL` columns, and the database itself refuses to create a row that would
materialize a null:

```
ERROR:  null value in column "label_x" of relation "overlay_revisions" violates not-null constraint
DETAIL:  Failing row contains (01a06995-fe79-7d9f-91dd-79bfbfb7594b, 1, Archived, Production Line 1, null, 0.25, …)
```

The remaining reachability would be a *code* path calling the private
constructor. It is `private`, has no call site other than EF's materializer, and
`Label.From` guards both arguments with `Ensure.That(position).IsNotNull()`. I
could not construct a case that observes the window, and I do not claim one
cannot exist — a future mapping change that made a column nullable, or an owned
type configured without `IsRequired()`, would reopen it, and nothing fails the
build if that happens.

## 6. `Revision.Branch`'s two-level `with { }` copy — watched, over real SQL

The sequence, each step a live HTTP call with the `If-Match` precondition
ADR-0113 requires:

| Step | Request | Response |
|---|---|---|
| publish rev 1 | `POST /overlays/{id}/revisions/1/publish`, `If-Match: "0"` | `200`, `1` |
| archive rev 1 | `POST /overlays/{id}/revisions/1/archive`, `If-Match: "1"` | `200`, `1` |
| **branch** | `POST /overlays/{id}/draft`, `If-Match: "2"` | `201`, `2` |
| edit rev 2 | `PATCH /overlays/{id}/revisions/2`, `If-Match: "3"` | `200`, `2` |
| publish rev 2 | `POST /overlays/{id}/revisions/2/publish`, `If-Match: "4"` | `200`, `2` |

The read immediately after the branch, before any edit — this is the step that
proves the copy reached `Position` and `Size` rather than sharing the archived
revision's instances:

```json
{"revisionIdentifier":"01a06996-7880-7030-8412-927695852aeb","revisionNumber":2,"state":"Draft","text":"Production Line 1","normalizedX":0.125,"normalizedY":0.25,"normalizedWidth":0.375,"normalizedHeight":0.5,"fontSizePx":48,…}
```

No EF re-keying error, geometry carried across intact, and revision 1's own row
unchanged (`0.125 / 0.25 / 0.375 / 0.5`, still `Archived`). The subsequent edit
and publish moved revision 2 to `0.0625 / 0.75 / 0.9 / 0.125` without disturbing
revision 1. A third branch (rev 3, §4) repeated the copy a second time.

## 7. Coverage

`scripts/coverage-check.ps1` needs PowerShell 7, which this machine does not
have (`pwsh: command not found`), so the relevant slice was reproduced by hand
using the script's own method — per-project `--collect:"XPlat Code Coverage"`,
merged with `reportgenerator`, line-rate read off the merged Cobertura. The three
non-integration test projects that reference `OverlayDesigner.Domain` are
`OverlayDesigner.Domain.Tests`, `OverlayDesigner.Application.Tests` and
`Architecture.Tests`; all three were run.

**Both of 4b's figures reproduce exactly.** The `develop` baseline was measured
in a throwaway `git worktree` at `origin/develop` (since removed):

| | `SmartSentinelEye.OverlayDesigner.Domain` line-rate |
|---|---|
| `origin/develop` | `0.9241071428571429` → **92.41%** |
| `b23b905` | `0.9144144144144144` → **91.44%** |

Gate is ≥ 90% (ADR-0065): **passes**, with 1.44 points of margin.
`OverlayDesigner.Application` reads `0.908235294117647` → **90.82%** against a
≥ 80% gate.

**The entire loss is the private EF constructor**, confirmed rather than
repeated — the only lines with `hits="0"` in the merged report for that package
are `Label.cs` **36** and **38**, which are the `: this(text, null!, null!,
fontSizePx)` initializer and its closing brace. Note the irony worth recording:
those two lines *are* executed on the live stack (§5), by the very suite that is
excluded from coverage.

Unit suites, `-c Release --no-build`, matching 4b's report:

```
Passed! - Failed: 0, Passed:  81 - SmartSentinelEye.OverlayDesigner.Domain.Tests.dll
Passed! - Failed: 0, Passed:  42 - SmartSentinelEye.OverlayDesigner.Application.Tests.dll
Passed! - Failed: 0, Passed: 113 - SmartSentinelEye.Architecture.Tests.dll
Passed! - Failed: 0, Passed:  79 - SmartSentinelEye.Shared.Kernel.Tests.dll
```

`dotnet build -c Release` over the solution: **0 Warning(s), 0 Error(s)**.

## Latency (constitution §IV)

**N/A — not on the event-to-overlay path.** `OverlayDesigner` is the authoring
path: an operator writes a label, and the six legs in §IV run from a plant-floor
event to a rendered overlay. The one place this change touches something the
runtime path carries is `OverlayRevisionPublishedV1` / the SignalR frame, and
both are `Shared.Contracts` wire shapes left byte-identical (§1, §4) — the
refactor stops at the domain model behind them. No leg's budget is affected, so
no figure is cited and none is claimed.

## What was not covered

- **`develop`'s live `400` was not observed.** §3's comparison rests on reading
  a literal interpolation in `origin/develop`'s `Label.cs`, not on booting
  `develop`. Unambiguous, but a reading.
- **No browser.** Neither `management-web` nor `kiosk-web` was driven. The
  frontend argument is (a) the ten reader files are untouched by the diff and
  (b) the JSON and the hub frame they parse are unchanged — not "an operator
  saw a label render".
- **The integration suite was not re-run at phase 5.** 4b ran it (22 green).
  Here the same ground was covered by hand against a run-mode stack instead,
  which is a different and in places stronger observation, but it is not a
  second green suite.
- **Single runs.** Nothing here is a measurement, so nothing was repeated. The
  "run it twice" rule applies to figures and there are none.
- **Not exercised**: `revert`, the `Idempotency-Key` path on `POST /overlays`,
  409 staleness, multi-fab scoping, and `ListOverlays` paging beyond one page.
  All are geometry-agnostic, but none was watched.
- **Residue left behind.** The dev database now holds one verification overlay
  (`01a06995-fe77-73c6-82ae-a2ab380f42ef`), one camera and one published layout,
  and one `aspire-container-network-tunnelproxy-*` container from this run
  survived shutdown. The persistent `-18bcf406` containers were not stopped.
  Nothing in the repository was changed by the run: the throwaway worktree and
  `artifacts/coverage-verify` were removed and `git status` is clean apart from
  this file.
