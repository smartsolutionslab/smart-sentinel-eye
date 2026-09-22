# Verification — Spec 210 (#2431)

Phase 5 (ADR-0037), executed in worktree `D:/Github/sse-2431`, branch
`2431-audit-date-clock-mismatch`, HEAD `46bb6b5e`, 2026-09-22.

## Method chosen, and why

This is a client-side string-formatting fix confined to
`apps/management-web/src/features/audit/AuditPage.tsx` — no backend, no
contract, no migration (spec §*Out of scope*). The audit page's data
fetching does not require a live backend to reach the filter UI: the
existing `AuditPage.test.tsx` already renders the real page against a
mocked `useSearchAuditQuery`. Booting the full Aspire stack would add
~10 containers and Keycloak sign-in for a defect that is entirely about
how a query-string value is built before it ever reaches the wire.

**Chosen approach: exercise the real production code path in Node
(vitest, jsdom), stubbing only the terminal `fetch` call.** Concretely,
a disposable test (not committed) rendered the real, unmocked `AuditPage`
against the real Redux store, the real `auditApi.searchAudit` RTK Query
endpoint, and the real `gatewayBaseQuery`/`fetchBaseQuery` from
`apps/shared/src/api/gateway.ts` — with `VITE_API_GATEWAY_URL` set to
`http://gateway.local` so the gateway origin resolves the way Aspire
injects it in a real dev boot (ADR-0106). Only `global.fetch` was
replaced, to capture the `Request` object `fetchBaseQuery` builds instead
of letting it leave the process. Everything upstream of that one
substitution — `toInstant`, `toQuery`, `definedParams`, and
`fetchBaseQuery`'s own `Request` construction (Node's `undici`, which
implements the same WHATWG URL percent-encoding algorithm every
Chromium-based browser uses) — is genuine, unmodified production code.
The captured `Request.url` is therefore the same string a real browser's
network stack would have produced for the same input.

This is the "lighter-weight approach ... driven with a non-UTC `TZ` env
var" the task anticipated, and it produces the same decisive artefact
the manual procedure asks for: **the actual request URL/query string**,
not merely the request-argument object the unit tests already assert on.

## Before-state evidence

**Reproduced, not merely quoted**, by checking out this spec's own T001
commit (`3aa9c64e` — tests added, fix not yet applied) into a disposable
worktree and running the real `AuditPage.test.tsx` there unmodified
(dependencies linked from this worktree's `node_modules`, no
`package.json`/lockfile change between the two commits — verified with
`git diff 3aa9c64e HEAD -- package.json pnpm-lock.yaml apps/**/package.json`,
empty). The worktree was removed immediately after capturing output.

The machine's own zone is `Europe/Berlin` (`Intl.DateTimeFormat().resolvedOptions().timeZone`
→ `Europe/Berlin`, `getTimezoneOffset()` → `-120`), which is itself
evidence for the zone-independent case below: the received value did not
depend on it.

**Zone-independent case** (`Applying a Since filter sends an ISO instant,
not a bare local wall clock`), run in the machine's ambient zone (no `TZ`
override) — red:

```
AssertionError: expected last "vi.fn()" call to have been called with [ ObjectContaining{…} ]

- Expected
+ Received

  [
    {
-     "since": "2026-09-17T06:00:00.000Z",
+     "pageSize": 50,
+     "since": "2026-09-17T08:00",
    },
  ]

 ❯ src/features/audit/AuditPage.test.tsx:131:24
```

**Berlin case** (`TZ=Europe/Berlin`) — red, same raw value:

```
- Expected
+ Received

  [
    {
-     "since": "2026-09-17T06:00:00.000Z",
+     "pageSize": 50,
+     "since": "2026-09-17T08:00",
    },
  ]
 ❯ src/features/audit/AuditPage.test.tsx:145:24
```

**New York case** (`TZ=America/New_York`) — red, same raw value:

```
- Expected
+ Received

  [
    {
-     "since": "2026-09-17T12:00:00.000Z",
+     "pageSize": 50,
+     "since": "2026-09-17T08:00",
    },
  ]
 ❯ src/features/audit/AuditPage.test.tsx:157:24
```

All three received values are the identical, untransformed
`"2026-09-17T08:00"` — the bare wall clock, confirming spec §*The failure,
restated with the numbers checked*: the sent value does not vary with
the browser's zone before the fix, only the *meaning* the server assigns
it does. Full run at this commit: **6 failed | 7 passed (13)** — the two
green new cases are `Clearing the filters sends no date bounds` and `The
non-date filters are sent verbatim`, exactly the two tasks.md flagged as
"may legitimately be green" (T001, done-when clause).

**The equivalent request URL fragment**, derived from the same raw value
via `URLSearchParams` (the query-string encoder itself is unchanged by
this fix — only the value handed to it is):

```
node -e "console.log(new URLSearchParams({pageSize:50, since:'2026-09-17T08:00'}).toString())"
→ pageSize=50&since=2026-09-17T08%3A00
```

This is exactly the artefact spec §*Independent end-to-end test
procedure* step 5 names: `since=2026-09-17T08%3A00` — no `Z`, no offset.

**US2 label case**, also red at this commit (labels still read `Since` /
`Until`):

```
TestingLibraryElementError: Unable to find a label with the text of: /Since \(your local time\)/i
 ❯ src/features/audit/AuditPage.test.tsx:228:19
```

## After-state evidence

Run against this worktree's actual `HEAD` (`46bb6b5e`, both T002 and
T003b applied), via the disposable harness described above, with
`process.env.TZ` pinned per case and `Since` set to `2026-09-17T08:00`:

```
CAPTURED_URL_BERLIN=http://gateway.local/audit-observability/audit?pageSize=50&since=2026-09-17T06%3A00%3A00.000Z
CAPTURED_URL_NY=http://gateway.local/audit-observability/audit?pageSize=50&since=2026-09-17T12%3A00%3A00.000Z
```

Both assertions passed:

```
✓ sends since as an ISO instant with Z when the browser zone is Europe/Berlin
✓ sends since as an ISO instant with Z when the browser zone is America/New_York
```

**The contrast that matters**: the query string now carries
`since=2026-09-17T06%3A00%3A00.000Z` / `...T12%3A00%3A00.000Z` — an
explicit `%3A00.000Z` where before it carried `2026-09-17T08%3A00` with
no seconds, no milliseconds, and no `Z`. The shift is in opposite
directions for the two zones (Berlin −2h, New York +4h from the typed
`08:00`), which is what rules out "shifted by a constant" and confirms
an actual zone-aware conversion (spec §*US1 — the mirror case*).

The full suite in the actual worktree, run immediately before and after
this harness (which was never committed — it existed only for this
observation and was deleted afterward):

```
Test Files  38 passed (38)
     Tests  313 passed (313)
```

`pnpm --filter @smart-sentinel-eye/management-web typecheck` and
`pnpm lint` (workspace-wide, `--max-warnings 0`) both pass clean.
`git status --porcelain` is empty — no test-harness artefact was left
behind.

## Zone-independent invariant, re-confirmed

The Berlin/New York pair alone would not distinguish "converted
correctly" from "shifted by a constant"; recording both signs (spec
§*US1 — the mirror case*) is why both are captured above rather than
just one. CI's frontend job runs on `ubuntu-latest`, i.e. UTC.

The before-state evidence above was captured in the machine's ambient
zone, `Europe/Berlin` — reproduced here instead of merely inferred:
`3aa9c64e` (T001, tests added, fix not yet applied) checked out into a
disposable worktree, `TZ=UTC` set explicitly, same unmodified
`AuditPage.test.tsx`:

```
FAIL  src/features/audit/AuditPage.test.tsx > AuditPage > Applying a Since filter sends an ISO instant, not a bare local wall clock
AssertionError: expected last "vi.fn()" call to have been called with [ ObjectContaining{…} ]
- Expected
+ Received
  [
    {
-     "since": "2026-09-17T08:00:00.000Z",
+     "pageSize": 50,
+     "since": "2026-09-17T08:00",
    },
  ]
```

Confirms directly, rather than by inference, that `"2026-09-17T08:00"`
equals an ISO instant in no zone, UTC included — the zone-independent
case fails in CI's own zone, not only in Berlin and New York. The
disposable worktree was removed immediately after capturing this
output; `git status --porcelain` on this worktree was empty afterward.

## Latency-budget impact

**N/A.** Per `spec.md` §*Latency-budget impact*: the audit search is an
operator-initiated `GET` through the gateway, not on the
`event arrival → overlay rendered` path, and touches none of
constitution §IV's six legs. No figure is cited — there is nothing on
that budget for this change to have moved.

## What was not re-verified here

- **Authorization** (spec §*US1 — auth*): out of scope for this fix by
  design — no scope, claim, or endpoint is touched, and this spec does
  not reach the server. Not re-tested.
- **`ResourceTimelineInput`/`getResourceTimeline`**: confirmed still
  unconsumed (`useGetResourceTimelineQuery` exported, no caller) — the
  latent twin remains latent, untouched, as scoped.

## Housekeeping

The disposable pre-fix worktree used to capture the before-state output,
and the uncommitted after-state test harness, were both removed after
use; this spec's diff remains exactly the three files listed in its
`spec.md` §*File contention* plus the `specs/` directory.
