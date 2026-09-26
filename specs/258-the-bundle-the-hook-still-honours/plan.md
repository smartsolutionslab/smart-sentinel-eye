# Plan — Spec 258, the bundle the hook still honours

**Spec:** `specs/258-the-bundle-the-hook-still-honours/spec.md`
**Issue:** #2486 (partial) · **Branch:** `fix/2486-whep-legacy-bundle-cleanup` · **Phase-4a colour:** RED
**Assignment:** phase 4a `test-writer`; phase 4b `backend-engineer`.

---

## 1. Shape of the change

| Usual section | This spec |
|---|---|
| Bounded context | **StreamDistribution** (Application + Api), plus tests in `StreamDistribution.Application.Tests` and `Architecture.Tests` |
| Entities / value objects | **None added or changed.** `WhepAuthSubject.Scopes`, `MediaMtxPath`, `AuthorizeWhepError.Forbidden` are reused as-is |
| Invariant | The hook admits a read/playback only if the validated subject's scopes contain `RequiredScope` (`sse.streams.read`). No other scope substitutes for it |
| Domain → integration event | **None.** |
| Persistence / migration | **None.** |
| API surface | **No route, status or Produces change.** Only the `.WithSummary` text of `POST /streams/authorize` changes |
| Boundary rules | Unaffected. Application keeps re-spelling the scope literal (ADR-0051) rather than referencing `ServiceDefaults`; no cross-context reference is added |

### Files

| # | File | Phase | Change |
|---|---|---|---|
| F1 | `tests/StreamDistribution.Application.Tests/Commands/AuthorizeWhepCommandHandlerTests.cs` | 4a | SC-1 red (invert `:44-63`), SC-2 control, fixture repair `:171`, repoint `:213-237`, rename `:133` — see §2 |
| F2 | `tests/Architecture.Tests/EndpointScopeDeclarationTests.cs` | 4a | rewrite A8b `:821-854` (SC-7 red); reword class doc `:255-267` |
| F3 | `src/StreamDistribution/Application/Commands/Handlers/AuthorizeWhepCommandHandler.cs` | 4b | delete `LegacyManagementBundle` (`:31`) and the `&& !…Contains(LegacyManagementBundle…)` clause (`:80`); rewrite the `RequiredScope` doc (`:15-28`) |
| F4 | `src/StreamDistribution/Api/StreamEndpoints.cs` | 4b | summary `:67-79` — drop the whole bundle clause (see §3 correction) |
| F5 | `src/StreamDistribution/Application/Commands/AuthorizeWhepCommand.cs` | 4b | doc `:9-11` — "checks the `sse.management` scope" → "checks the `sse.streams.read` scope". **Addition beyond the orchestrator's brief, flagged:** comment-only, one line, same class of edit as spec 200's F8 (a comment naming the exact check this PR changes; leaving it makes the command's doc assert the scope the handler now refuses). Veto-able without affecting anything else |

F1/F2 are **test files** and therefore phase-4a work: ADR-0139 forbids the 4b
engineer editing tests, and three of F1's edits are needed for the suite to be
correct after 4b.

---

## 2. F1 in detail — why five edits in one test file, and which colour each is

| Fact | Today | Edit | Colour before → after |
|---|---|---|---|
| `Authorize_with_a_grandfathered_management_token_returns_success` (`:39-63`) | asserts bundle-only `["openid","sse.management"]` → `IsSuccess` | **Replace** with `Authorize_with_only_the_legacy_management_bundle_returns_Forbidden`: same subject, `Read` on any path, asserts `IsFailure` and `Error` is `AuthorizeWhepError.Forbidden` | **RED → green (SC-1)** |
| *new* `Authorize_with_the_consoles_granular_token_returns_success` | — | subject = management-web's granular set (every `sse.*.read`/`.write` the realm grants it, incl. `sse.streams.read`, **no** bundle), `Read` → `result.Value == path` | green → green (SC-2) |
| `Authorize_for_an_Offline_stream_returns_StreamUnavailable` (`:171`) | subject `["sse.management"]` | subject → `AKioskPersona`. **Fixture repair, assertion untouched.** Without it this fact turns red after 4b for the wrong reason (`Forbidden` precedes the stream check) and 4b may not fix it | green → green (SC-6) |
| `Authorize_a_publish_with_the_grandfathered_bundle_is_refused` (`:213-237`) | uses the bundle as "the broadest token that reaches this hook" | subject → the SC-2 granular set; rename `Authorize_a_publish_with_the_consoles_broadest_token_is_refused`; doc updated. Assertion (`ActionNotPermitted`) untouched | green → green (SC-4) |
| `Authorize_with_a_token_granting_neither_the_read_scope_nor_the_bundle_returns_Forbidden` (`:133`) | name implies the bundle would admit | rename `Authorize_with_a_token_without_the_read_scope_returns_Forbidden`; body untouched | green → green |

**The inversion of a green test is the specification, not an accommodation**
— the same stance spec 200 T004 took for `RequireScopePolicyTests`. It is done
by the test-writer and quoted red; the engineer never touches it.

**Declare the SC-2 scope set as a `private static readonly string[]`** beside
`AKioskPersona`, written out (Application tests do not reach into the realm or
another context — the existing `AKioskPersona` doc gives the reason). It need
not be the exact twenty; it must contain `sse.streams.read`, several write
scopes, and **not** `sse.management`. Its doc says it mirrors
`management-web`'s defaults at realm `:157-188`.

---

## 3. F2 and F4 — the surface

### A8b rewrite (SC-7)

Rename `The_whep_hook_summary_names_the_two_scopes_its_handler_accepts` →
`The_whep_hook_summary_names_the_one_scope_its_handler_accepts`.

1. Keep: `summary.ShouldContain(PrivateConstant(typeof(AuthorizeWhepCommandHandler), "RequiredScope"), Case.Sensitive, …)`.
2. Replace the second assertion with `summary.ShouldNotContain(RequireScopeExtensions.LegacyManagementBundle, Case.Sensitive, …)` — message: the handler no longer accepts the bundle (#2486), and a summary naming a scope the check refuses is A5's "worse than silence".
3. Doc comment: say the sentence is pinned to the one constant; that the absence check keeps a compile reference to `RequireScopeExtensions.LegacyManagementBundle` **on purpose**, and that when #2486's remainder deletes that const, this assertion is deleted with it (nothing will hold the scope to be named). Drop the `AdminPolicy` sentence from the message (`:852-853`) — it described a path to the old sentence going false.

**Why reference the const rather than type `"sse.management"`:** the class doc
(`:422-425`) records that no scope string is typed into this file; and the
compile break it produces later is a signpost pointing at the one line to
delete, not a trap — the trap #2486 hit was an *undocumented* reference.

### Class doc `:255-267`

Today's last two sentences — *"Only `POST /streams/authorize` says so, because
only there is the check hand-rolled … That is `KioskScopeParityTests`'
territory"* — become false (no summary names the bundle). Replace with: the
policy composition still holds for every policy-routed scoped endpoint (the
first four sentences stay verbatim — **still true**, `AddScopePolicies` is
unchanged); no summary says so; the one hand-rolled check,
`POST /streams/authorize`, no longer accepts the bundle (#2486), so the gap is
confined to the policy-routed endpoints and closes when #2486's remainder
withdraws it from `AddScopePolicies`. Keep the `KioskScopeParityTests`
pointer.

### F4 — correction to the orchestrator's brief

The brief proposed keeping *"every scoped endpoint accepts the bundle too,
through its policy"* in the hook's summary. **Drop it with the rest of the
bundle clause.** Two reasons:

1. It names `sse.management`, so SC-7's absence assertion would fail on it —
   and SC-7 is right to: an OpenAPI summary for MediaMTX integrators should
   describe *this* operation, not another endpoint family's policy.
2. The fact it states still lives where it belongs — the architecture test's
   class doc and `RequireScopeExtensions`' own doc.

Target text for the replaced span (`:72-74`): *"…audience, then requires
sse.streams.read on it. A missing or invalid token is 401; …"* — the
remainder of the summary (401/403 rules, action handling, 429) unchanged.
`No OIDC scope:` must stay (A8).

### F3 — the handler doc

Replace `:15-28` with: watching a stream is a read, so the gate asks for the
read scope **and nothing else**; the literal is repeated rather than
referenced because Application stays ASP.NET-free (ADR-0051). Keep the spec
041 paragraph (history of why the kiosk can pass). Add one sentence: the
legacy bundle is not accepted here (#2486) even while `AddScopePolicies` still
honours it — a hand-rolled check does not inherit the policy's grandfathering.

---

## 4. Why no new integration test

Considered and rejected. A bundle-only token can no longer be minted from any
realm client (spec 200 US1), so the only way to get one is to plant a
throwaway Keycloak client granting `sse.management` (the
`EventTypeRegistryAuthorizationIntegrationTests` / `RealmProbe` pattern). What
that would add over SC-1 is that `WhepAuthValidator` passes scopes through
unchanged — which the existing `WhepAuthIntegrationTests` already exercise on
the admitted path. The decision under change is entirely in the handler, and
SC-1 runs the real handler. Phase 5 runs the `Whep` integration tests as the
over-correction guard (SC-8).

---

## 5. Sequencing

```
4a (test-writer)                           4b (backend-engineer)
F1 handler tests  [P] ─┐
F2 arch test      [P] ─┴─► verbatim run ─► F3 handler, F4 summary, F5 command doc
```

F1 and F2 are disjoint files in disjoint test projects → `[P]`. F3/F4/F5 are
three small edits in one context; one engineer, not parallelised (F4's text
and F3's doc should agree).

**Expected phase-4a output:** red = SC-1 (handler) and SC-7 (arch). Everything
else green. **A red anywhere else is an escalation** — in particular a red
SC-2 would mean the granular set was mis-built, and a red Offline-stream fact
would mean the fixture repair was skipped.

---

## 6. Risks

| Risk | Handling |
|---|---|
| The Offline-stream fact goes red after 4b for the wrong reason | §2: repaired in 4a; the expected-output list names it |
| A reviewer reads the PR as "US2 done" and closes #2486 | No closing keyword; T012 comments the remainder on the issue |
| Parked-PR contention on `EndpointScopeDeclarationTests.cs` (2285 lines, many specs touch it — pinned counts at `:358-360`) | This change moves neither pinned count; rebase after every merge (ADR-0087) |
| The WHEP hook and the policies now disagree on the bundle | Intended, strictly narrower, documented in the class doc and spec *Scope* |
| Someone re-grants the bundle before #2486's remainder lands | Video path now refuses it; `LegacyBundleGrantTests` still fails the build for any client granting it |

## 7. What this plan will not do

- Touch `RequireScopeExtensions.cs`, `RequireScopePolicyTests.cs`, the realm JSON, `AuthenticationDefaults.cs`, `RulesEndpoints.cs`, or the two `CrossFab*IntegrationTests` comments (#2486 remainder).
- Touch `.claude/agents/*.md`, README `:409`, `LayoutLifecycleHub.cs:16`, `useLayoutLifecycle.ts:37`: they describe the **policy** grandfather, which is still true.
- Write or amend an ADR; weaken a gate.
