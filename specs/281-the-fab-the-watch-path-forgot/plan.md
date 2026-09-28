# Plan 281

## Sequence

1. Write the failing unit fact(s) first (ADR-0139) against the current handler: a cross-fab subject
   admitted when it should be refused. Run, capture red.
2. Implement: `WhepAuthSubject.Fabs` → `WhepAuthValidator` populates it → `AuthorizeWhepErrors` gets
   `FabNotAuthorized` → `AuthorizeWhepCommandHandler` checks it.
3. Update all 12 existing `WhepAuthSubject` construction sites in
   `AuthorizeWhepCommandHandlerTests.cs` — each gets an explicit `Fabs` argument. The one test with a
   fab-bearing stream (`Authorize_for_an_Offline_stream_returns_StreamUnavailable`, built via
   `StreamBuilder` whose default fab is `munich`) needs `Fabs: ["munich"]` specifically, or it starts
   failing on the new fab check instead of proving what it's meant to prove.
4. Green: full `AuthorizeWhepCommandHandlerTests` suite.
5. Integration: new fact(s) in `WhepAuthIntegrationTests.cs` with a genuinely cross-fab principal
   (`wall-berlin` against a munich stream) and a null-fab stream. Run live (Aspire fixture).
6. `backend-reviewer` + `security-reviewer` passes. Fix or refuse in writing.
7. PR.

## Verification

- `dotnet test tests/StreamDistribution.Application.Tests` — unit, fast, no stack.
- `dotnet test tests/Integration.Tests` filtered to `WhepAuthIntegrationTests` — live Aspire stack,
  real Keycloak tokens.
- `dotnet build -c Release` — full solution, confirms no other call site of `WhepAuthSubject` was missed.
