# Tasks 281

- [x] T001 Write the failing unit fact: a subject holding `berlin` refused for a stream in `munich`.
      Run; confirm red for the right reason (admitted when it should be refused).
- [x] T002 `WhepAuthSubject`: add `Fabs`.
- [x] T003 `WhepAuthValidator.ValidateAsync`: populate `Fabs` via `FabClaims.AssignedFabs(principal)`.
- [x] T004 `AuthorizeWhepErrors.cs`: add `FabNotAuthorized` (403).
- [x] T005 `AuthorizeWhepCommandHandler.HandleAsync`: the check — stream has a fab not in `subject.Fabs`,
      or no fab at all — placed before the stream-state check.
- [x] T006 Update all 12 existing `WhepAuthSubject(...)` call sites in
      `AuthorizeWhepCommandHandlerTests.cs` with an explicit `Fabs` argument; the
      offline-stream test needs `["munich"]` specifically.
- [x] T007 New unit fact: null-fab stream refused regardless of the subject's fabs. Added after
      phase-6 review (backend-reviewer S2) via the same reflection-based `Unattributed()` pattern
      `StreamFabAttributionTests.cs` already establishes.
- [x] T008 New unit fact: multi-fab subject admitted for a stream in either of their fabs (the
      over-correction guard — a naive single-fab compare would wrongly refuse this).
- [x] T009 Green: full `AuthorizeWhepCommandHandlerTests` (70/70, including two more facts added at
      phase 6: check-ordering and the null-fab case above).
- [x] T010 New integration facts in `WhepAuthIntegrationTests.cs`: cross-fab refusal (real
      `wall-berlin` token against a real munich stream), same-fab admission (control), and
      null-fab-attributed refusal (direct SQL). Both cross-fab and null-fab facts assert the
      problem `title`, not status alone (security-reviewer finding #4).
- [x] T011 Live run against the Aspire fixture; capture output. Run three times total across the
      session (client-choice bug found and fixed on the first live run; final confirmation after
      phase-6 fixes).
- [x] T012 `dotnet build -c Release` — confirm no other `WhepAuthSubject` call site was missed.
- [x] T013 `backend-reviewer` + `security-reviewer`. Both found real issues; all should-fix and nit
      items addressed except one (the orphaned-MediaMTX-path residual gap, security finding #1) —
      filed as #2658, a separate human decision per both reviewers' own recommendation, not fixed
      in this PR. #2659 (unauthenticated MediaMTX control API) also filed, unrelated pre-existing
      finding surfaced while checking adjacent WHEP paths.
- [ ] T014 PR, citing ADR-0161.
