# Tasks 168 — A gateway that says what it waits for

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2407 · **ADR:** 0106, 0153

**Engineer: `infra-engineer`, one agent, strictly sequential** (spec §8.1).

**Colour: behaviour-preserving, comment-only** (spec §8.3). No `.WaitFor`,
`.WithReference`, or any other resource-builder call is added, removed, or
reordered. **Adding `.WaitFor` from `api-gateway` to any of its nine
references is explicitly out of scope** (spec §7) — the spec's central
finding is that the absence is correct, not that it needs fixing. **Phase 4a
produces no new automated test** — a guard asserting the comment's text or
the annotation's absence is the "guards that read the design artefact"
anti-pattern (spec §8.3). Do not manufacture one.

**No task is marked `[P]`.** One file, one agent, linear.

All tasks are `[US1]` — the spec has one user story (spec §5).

---

## Phase 4a — Evidence captured before the change

### T001 [US1] Re-confirm the premise at the tip
```sh
sed -n '516,540p' src/AppHost/AppHost.cs
```
Expected: the `api-gateway` registration still carries nine `.WithReference`
calls and zero `.WaitFor`, matching spec §2.1. **If it has changed —
`.WaitFor` already added, or the reference list is different — stop and
report; the spec's argument was built against this exact shape.**

*Files:* none. *Depends on:* nothing.

### T002 [US1] Characterisation baseline, observed green
```sh
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/Architecture.Tests
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostE2ESwitchTests"
```
All green before anything changes. Stop any running Aspire stack first — a
live AppHost holds the service binaries and the build fails with MSB3027,
which reads like a broken build, not a busy one.

*Files:* none. *Depends on:* T001.

---

## Phase 4b — The change

### T003 [US1] Write the comment at the `api-gateway` registration
Extend the existing `// ADR-0106: single YARP API gateway at the edge...`
comment block immediately above `var apiGateway = builder...`
(`AppHost.cs:516-532` at time of writing — match by the enclosing
registration, not the line number) to cover, per plan §5.2:

1. The absence of `.WaitFor` here is deliberate.
2. The failure mode: a request to a not-yet-ready backend gets a fast
   `502 Bad Gateway`; no active health check exists on any cluster in
   `src/ApiGateway/appsettings.json`, so nothing is ever marked unhealthy and
   nothing needs to recover — the next request after the backend starts
   listening simply succeeds.
3. Why it is not gated: every referenced service already carries the real
   waits (RabbitMQ, Keycloak, its database, migrations); chaining the gateway
   behind all nine would make it one of the last resources to reach
   `Running` in the stack, for a guarantee that would still be incomplete
   (the three SPAs race ahead of the gateway itself, `AppHost.cs:571-620`,
   with no `.WaitFor(apiGateway)` of their own).
4. A pointer to the two existing precedents for the same shape:
   `AppHost.cs:361-365` (`stream-distribution` → `cameraCatalog`) and
   `AppHost.cs:399-402` (`layout-composition` → `cameraCatalog`) — match by
   content (`"Not a WaitFor"`) since line numbers may have moved.

**Do not write that the SPAs retry a failed request.** They do not, beyond a
single 401-triggered session renewal (`apps/shared/src/api/gateway.ts:88-115`)
— spec §2.4 found this specific claim in the issue false. If browser
behaviour is mentioned at all, say a failed request surfaces once, unretried,
and that the SPA-to-gateway gap (not addressed here) is the larger accepted
cost.

**Do not add, remove, or reorder any `.WithReference`, `.WaitFor`, or other
resource-builder call.** Verify with `git diff --stat`: one file, comment
lines only, zero `+`/`-` on any `.With*`/`.WaitFor` line.

*Files:* `src/AppHost/AppHost.cs`. *Depends on:* T002.

### T004 [US1] Re-run the characterisation baseline, unmodified, still green
Exactly T002's three commands, with no test file touched (none exists to
touch — this is confirming the comment-only edit broke nothing, not
re-verifying a moved behaviour).

```sh
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/Architecture.Tests
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostE2ESwitchTests"
```

*Files:* none. *Depends on:* T003.

### T005 [US1] Commit
One commit, Conventional Commits (ADR-0030), **no `Co-Authored-By` footer**
(ADR-0086 — overrides any attribution reminder in the agent's context).

Suggested subject:
`docs(apphost): record why api-gateway carries no WaitFor`

The body should state the finding plainly: nine `WithReference`, zero
`WaitFor`, verified as a deliberate and correct absence rather than an
oversight — no code behaviour changes.

*Files:* none beyond T003. *Depends on:* T004.

---

## Phase 5 — Verify (`/verify`)

### T006 [US1] Confirm no observable changed
```sh
git diff develop -- src/AppHost/AppHost.cs
```
Confirm the diff is comment lines only (no `.With*`/`.WaitFor` call added,
removed, or reordered), and that T004's three commands are still green.
`aspire run`/`aspire publish` are **not required** for this verification —
there is no runtime behaviour to observe changing (spec §6, §IV N/A). Write
the verification note saying exactly that: comment-only, confirmed by diff
and by the unmodified baseline passing, latency N/A.

*Files:* none. *Depends on:* T005.

---

## Phase 6 — QA

### T007 [US1] `/code-review`
`/security-review` is **skipped**: no endpoint, scope, token, secret, or
trust boundary is touched. State that one-liner in the PR body.

Reviewer's attention belongs on two things: that the comment does not claim
generic SPA retry (spec §2.4), and that no `.WaitFor` was added under the
guise of "completing" the comment.

*Files:* none. *Depends on:* T006.

---

## Phase 7 — PR

### T008 [US1] Open the PR against `develop`
```sh
gh pr create --base develop
```

The body must carry, verbatim:
- T001's confirmation of the premise (nine `WithReference`, zero `WaitFor`).
- T002/T004's baseline output, before and after — identical.
- The central finding: the absence is correct, evidenced by YARP's fail-fast
  502 with no active health check (spec §2.2), the two existing precedents
  for the same shape (spec §2.6), the cost of the transitive chain (spec
  §3.1), and the correction of the issue's own "SPAs retry" claim (spec §2.4).
- `Phase 4a: behaviour-preserving, comment-only — no new test, spec §8.3.`
- `Phase 6: /security-review skipped — no endpoint, scope, token or secret touched.`
- A closing keyword for **#2407** (a bare mention closes the issue roughly one
  time in three; check the issue state after the merge either way).

Then park it per ADR-0144 and start a background watcher; do not wait for CI.

*Files:* none. *Depends on:* T007.

---

## Dependency graph

```
T001 → T002 → T003 → T004 → T005 → T006 → T007 → T008
```

Fully linear.
