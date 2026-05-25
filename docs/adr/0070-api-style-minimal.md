# ADR-0070: API Style — Minimal APIs Only

**Status:** Accepted
**Date:** 2026-05-25

## Context

Each context's `.Api` project exposes HTTP endpoints to the frontend
apps and external integrators. ASP.NET Core supports three styles:
controllers, minimal APIs, and Wolverine's HTTP endpoint generation.

## Decision

**Minimal APIs only.** Organize routes via
`Map<Context>Endpoints(this IEndpointRouteBuilder)` extension methods
in each `.Api` project (Yumney pattern).

```csharp
// CameraCatalog.Api/CameraEndpoints.cs
public static class CameraEndpoints
{
    public static IEndpointRouteBuilder MapCameraCatalogEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/cameras")
            .WithTags("Cameras")
            .RequireAuthorization("CameraCatalog.Read");

        group.MapPost("/", Register)
            .WithName("RegisterCamera")
            .Produces<CameraId>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}", GetById);
        group.MapDelete("/{id:guid}", Decommission);

        static async Task<IResult> Register(
            RegisterCameraRequest request,
            ICommandHandler<RegisterCamera, Result<CameraId, RegisterCameraError>> handler,
            CancellationToken ct)
        {
            var (name, url) = request;
            var result = await handler.HandleAsync(new RegisterCamera(name, url), ct);
            return result.ToCreated(id => $"/cameras/{id}");
        }

        return app;
    }
}
```

- **Endpoint handlers are static local functions** inside the
  registration method. Keeps routing and handler-binding code colocated.
- **OpenAPI metadata declared in-line** via `.Produces<>`,
  `.WithName`, `.WithTags`, `.RequireAuthorization`.
- **Request DTOs use the custom `Deconstruct(...)` convention**
  (ADR-0069) to convert to typed value objects at the endpoint
  boundary.

## Consequences

- **Positive:** less boilerplate than controllers.
- **Positive:** routing and binding visible in one place.
- **Negative:** very large API surfaces (hundreds of endpoints in
  one file) become unwieldy; mitigate by splitting per resource.

## Alternatives Considered

- **Controllers** — historical default; more code per endpoint.
- **Wolverine HTTP endpoint generation** — convention-based; couples
  Api tightly to Wolverine; harder to grep for routes.
