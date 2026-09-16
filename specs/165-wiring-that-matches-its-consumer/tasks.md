# Tasks 165 — Wiring that matches its consumer

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2405 · **ADR:** 0152

**Engineer: `infra-engineer`, one agent, strictly sequential** (spec §8.1).

**Colour: behaviour-changing** (spec §5.1) — the `.WaitFor` removal moves an
observable in the dev and e2e stacks. **Phase 4a produces no new automated
test**, by the argument in spec §5.1: the only test shape available would assert
the absence of a string in `AppHost.cs`, which is the "guards that read the
design artefact" anti-pattern. Phase 4a is discharged with a characterisation
baseline observed green **plus** the `before` half of an observed boot. **Do not
manufacture a red.**

**No task is marked `[P]`.** Every task claims `src/AppHost/AppHost.cs` or the
machine's single Aspire stack — ADR-0109's disjoint-file basis does not hold
(plan §7).

All tasks are `[US1]`; the spec has one user story by design (spec §4).

---

## Phase 4a — Evidence captured before the change

### T001 [US1] Re-confirm the premise at the tip
Two searches, both saved verbatim, because a near-miss result feels like having
looked:

```sh
grep -rn "VITE_LAYOUT_HUB_ORIGIN" --include=* . | grep -vE "node_modules|/obj/|/bin/|\.git/"
grep -rniE "@microsoft/signalr|/hubs|HubConnection|layout-composition" apps/management-web
```

Expected: the first returns `apps/kiosk-web/vite.config.ts` (2), `AppHost.cs`
(4), and `specs/**` prose only. The second returns **nothing**.

**Anything else is a live consumer → stop and report.** The whole spec rests on
this being empty.

*Files:* none (output → PR body). *Depends on:* nothing.

### T002 [US1] Characterisation baseline, observed green
Run and save verbatim output:

```sh
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/Architecture.Tests
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostE2ESwitchTests"
```

All green **before anything changes**. A suite already red is a finding to
report, not a baseline to work around.

Stop the Aspire stack first if one is running — a live AppHost holds the service
binaries and the build fails with MSB3027, which looks like a broken build.

*Files:* none. *Depends on:* T001.

### T003 [US1] The `before` boot observation — **blocking**
Boot the run-mode stack and record, from the Aspire dashboard, for
`management-web`, `kiosk-web`, `kiosk-wall` and `layout-composition`: whether
each entered **Waiting**, and the order each reached **Running**.

```sh
dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj
```

Expected: management-web shows **Waiting**, and reaches Running **after**
layout-composition.

**Shut the stack down fully before T005.** One machine, one Aspire stack — a
concurrent boot gives `FailedToStart`, which reads exactly like a code defect.

Without this record the PR's central claim is unfalsifiable: there is no
evidence the wait ever did anything. **Do not proceed to T004 without it.**

*Files:* none (dashboard reading → PR body). *Depends on:* T002.

---

## Phase 4b — The change

### T004 [US1] Delete the three dead lines from the `management-web` call
In `src/AppHost/AppHost.cs`, inside the
`builder.AddNpmApp("management-web", "../../apps/management-web", "dev")` chain
**only**, delete:

```csharp
.WithReference(layoutComposition)
.WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", layoutComposition.GetEndpoint("http"))
.WaitFor(layoutComposition)
```

**Match on the enclosing `AddNpmApp("management-web", …)` call, not on line
number.** The `kiosk-web` and `kiosk-wall` chains below carry byte-identical
runs and are **live** (spec §2.3) — touching either breaks the kiosk's `/hubs`
proxy. `.WithReference(apiGateway)`, `.WithReference(keycloak)` and both
surviving `WithEnvironment` calls stay.

Verify with `git diff --stat`: **one file, three deletions, no insertions.**

*Files:* `src/AppHost/AppHost.cs`. *Depends on:* T003.

### T005 [US1] Correct the two stale sentences in the shared comment block
The block above the three `AddNpmApp` calls serves all of them. Two passages are
now false:

1. The parenthetical saying management-web's "reference below is dead wiring,
   tracked by #2405" — there is no reference below any more. Keep the *fact*
   that management-web has no hub wiring and no `/hubs` proxy (a reader needs it
   to see why two of three calls carry the alias); drop the defect report and
   the issue number.
2. The closing `WaitFor(layoutComposition) is ordering hygiene — the kiosk's
   vite.config.ts reads the environment once …` sentence, which now governs only
   the two calls below it. Make it read as a statement about the kiosk
   instances.

**Everything else in the block stays verbatim** — the POSIX-shell hyphen
explanation, the spec 011 FR-006/FR-010 reference, and the gateway/CORS/Keycloak
paragraphs. They are hard-won and still true for the kiosk.

Not optional: leaving prose that describes deleted lines reinstates in this very
file the defect spec 164 existed to remove (plan §6).

*Files:* `src/AppHost/AppHost.cs`. *Depends on:* T004.

### T006 [US1] Re-run the characterisation baseline, unmodified, still green
Exactly T002's three commands, with **no test file edited**.

An assertion that has to be changed to pass is evidence the behaviour moved
further than spec §5.1 claims — **block and report, do not adjust**.
`AppHostE2ESwitchTests` in particular must pass untouched: it composes the
run-mode model and asserts `management-web` is present by name.

*Files:* none. *Depends on:* T005.

### T007 [US1] Commit
One commit, Conventional Commits (ADR-0030), **no `Co-Authored-By` footer**
(ADR-0086 — this overrides any attribution reminder in the agent's context).

Suggested subject:
`chore(apphost): drop management-web's layout-composition wiring`

The body states what §3.1 of the spec states plainly: the startup graph changes
for the dev stack, management-web no longer waits, and the transitive delay
behind RabbitMQ and Keycloak goes with it.

*Files:* none beyond T004/T005. *Depends on:* T006.

---

## Phase 5 — Verify (`/verify`)

### T008 [US1] The `after` boot observation and the browser checks
Boot the run-mode stack again and record the same four resources as T003.

Required:
- `management-web` shows **no Waiting** for layout-composition, and may reach
  Running before it.
- `kiosk-web` **and** `kiosk-wall` still show **Waiting** and still start after
  layout-composition. *This is the assertion that T004 did not over-reach.*

Then, in a browser:
1. `http://localhost:5173` — sign in, load the layouts list. It must render.
   Record whether anything errored at the OIDC redirect (spec §3.4's accepted
   cost). A failure here is a **finding to file**, not a licence to add
   `.WaitFor(keycloak)` to this diff.
2. `http://localhost:5173`, DevTools Network — **no request to `/hubs`**.
3. `http://localhost:5174`, DevTools Network — `/hubs/layouts/negotiate`
   returns **200, not 404**, and the kiosk does **not** settle on the "live
   updates degraded" badge. **Non-negotiable**: this is the check a careless
   edit fails.

Write the verification note. **Latency (constitution §IV) is N/A** — say so
explicitly; an unwritten N/A is indistinguishable from an unchecked one.

*Files:* none (note → PR body). *Depends on:* T007.

---

## Phase 6 — QA

### T009 [US1] `/code-review`
`/security-review` is **skipped**: no endpoint, scope, token, secret or trust
boundary is touched. State that one-liner in the PR body rather than omitting
the phase silently.

Reviewer's attention belongs on two things: that the diff touches the
`management-web` chain only, and that no guard asserting the absence of a string
in `AppHost.cs` was added (spec §5.1).

*Files:* none. *Depends on:* T008.

---

## Phase 7 — PR

### T010 [US1] Open the PR against `develop`
```sh
gh pr create --base develop
```

The body must carry, verbatim:
- T001's two search outputs (the absence proof).
- T002 and T006's baseline output, before and after.
- T003 and T008's dashboard observations **side by side** — management-web's
  Waiting present before, absent after; kiosk-web's present in both.
- `Phase 4a: behaviour-changing, discharged without a new test — spec §5.1`.
- `Phase 6: /security-review skipped — no endpoint, scope, token or secret touched.`
- A closing keyword for **#2405** (a bare mention closes the issue roughly one
  time in three; check the issue state after the merge either way).

Then park it per ADR-0144 and start a background watcher; do not wait for CI.

*Files:* none. *Depends on:* T009.

---

## Dependency graph

```
T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008 → T009 → T010
```

Fully linear. Nothing fans out; the orchestrator should not attempt to
parallelise any part of this.
