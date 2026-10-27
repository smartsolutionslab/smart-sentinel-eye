# Quickstart — spec 465

Unit level (no stack):

```sh
dotnet test tests/SystemVariables.Infrastructure.Tests --filter "FullyQualifiedName~ReverseIndex"
dotnet test tests/SystemVariables.Application.Tests --filter "FullyQualifiedName~ReverseIndex"
dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~SystemVariablesRegistration"
```

Live (phase 5, SC-005) — needs the AppHost; stop it before any build (MSB3027):

1. Boot with `overlay-designer` held back (stop the resource right after the stack is healthy, then restart
   `system-variables`).
2. `GET /system-variables/-/snapshot?overlayIdentifier=<published>&fabId=<fab>` → `404 OVERLAY_NOT_IN_REVERSE_INDEX`.
3. Start `overlay-designer`. Within one capped delay (30 s) plus a request, repeat the call → `200`, resolved text.
4. `system-variables` structured log: Warning lines carrying `Attempt` and `RetryDelay`, then one
   `ReverseIndex seeded with N published overlays.` Information line, then no further seed requests.
