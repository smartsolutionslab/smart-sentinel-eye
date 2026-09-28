# Tasks 281

- [ ] T001 Write the failing unit fact: a subject holding `berlin` refused for a stream in `munich`.
      Run; confirm red for the right reason (admitted when it should be refused).
- [ ] T002 `WhepAuthSubject`: add `Fabs`.
- [ ] T003 `WhepAuthValidator.ValidateAsync`: populate `Fabs` via `FabClaims.AssignedFabs(principal)`.
- [ ] T004 `AuthorizeWhepErrors.cs`: add `FabNotAuthorized` (403).
- [ ] T005 `AuthorizeWhepCommandHandler.HandleAsync`: the check — stream has a fab not in `subject.Fabs`,
      or no fab at all — placed before the stream-state check.
- [ ] T006 Update all 12 existing `WhepAuthSubject(...)` call sites in
      `AuthorizeWhepCommandHandlerTests.cs` with an explicit `Fabs` argument; the
      offline-stream test needs `["munich"]` specifically.
- [ ] T007 New unit fact: null-fab stream refused regardless of the subject's fabs.
- [ ] T008 New unit fact: multi-fab subject admitted for a stream in either of their fabs (the
      over-correction guard — a naive single-fab compare would wrongly refuse this).
- [ ] T009 Green: full `AuthorizeWhepCommandHandlerTests`.
- [ ] T010 New integration facts in `WhepAuthIntegrationTests.cs`: cross-fab refusal (real
      `wall-berlin` token against a real munich stream) and same-fab admission (control).
- [ ] T011 Live run against the Aspire fixture; capture output.
- [ ] T012 `dotnet build -c Release` — confirm no other `WhepAuthSubject` call site was missed.
- [ ] T013 `backend-reviewer` + `security-reviewer`. Fix or refuse in writing.
- [ ] T014 PR, citing ADR-0161.
