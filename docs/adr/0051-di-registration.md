# ADR-0051: DI Registration — Per-Context Extension Methods

**Status:** Accepted
**Date:** 2026-05-25

## Context

Each context's Infrastructure and Api projects need to register their
internal services into the DI container. The registration code can
live in the AppHost (centralized, opaque), in each project (explicit,
greppable), or be assembly-scanned (magic, hidden).

## Decision

**Each context's Infrastructure and Api project exposes its own
`Add<ContextName>{Infrastructure,Api}` extension methods** on
`IServiceCollection`.

```csharp
// CameraCatalog.Infrastructure/CameraCatalogInfrastructureModule.cs
public static class CameraCatalogInfrastructureModule
{
    public static IServiceCollection AddCameraCatalogInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<ICameraRepository, CameraRepository>();
        services.AddSingleton<ICameraHealthProbe, CameraHealthProbe>();
        return services;
    }
}

// CameraCatalog.Api/Program.cs
builder.Services
    .AddCameraCatalogInfrastructure()
    .AddCameraCatalogApi();
```

- **No assembly scanning** (no Scrutor) for application code.
- **No attribute-based registration** (`[Service]`,
  `[Repository]`).
- **Wolverine's own handler discovery is the framework-provided
  exception** — it scans for handlers by convention. Acceptable
  because it is bounded to the framework's responsibility.
- Inside each module method, registrations are grouped by sub-feature
  with brief comments where the grouping isn't obvious.

## Consequences

- **Positive:** every service registration is greppable. `git grep
  AddScoped<ICameraRepository` jumps to the line.
- **Positive:** each context owns its DI graph; the AppHost stays
  a composition root, not a god configuration file.
- **Negative:** small ceremony when adding a new service. Acceptable.

## Alternatives Considered

- **Assembly scanning (Scrutor)** — convention-based; opaque to
  newcomers and to grep.
- **Attribute-based** (`[Service(Scoped)]`) — Spring-style; non-
  idiomatic in .NET; tooling weaker.
- **All registrations in AppHost** — composition root becomes a god
  object.
