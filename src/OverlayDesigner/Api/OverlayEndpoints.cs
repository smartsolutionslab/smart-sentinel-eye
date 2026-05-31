using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.OverlayDesigner.Application.DTOs;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>
/// Minimal-API endpoint group for OverlayDesigner (ADR-0070), covering
/// the overlay draft/revision lifecycle: create, read, list, publish,
/// archive, branch, edit, revert. The handlers are split across partial
/// files by message kind — <c>OverlayEndpoints.Commands.cs</c> and
/// <c>OverlayEndpoints.Queries.cs</c> — mirroring the Application layout.
/// </summary>
public static partial class OverlayEndpoints
{
    public static IEndpointRouteBuilder MapOverlayEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/overlays")
            .WithTags("Overlays");

        group.MapPost("/", CreateDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("CreateOverlayDraft")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{overlayIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Overlays.Read)
            .WithName("GetOverlay")
            .Produces<OverlayDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Overlays.Read)
            .WithName("ListOverlays")
            .Produces<ListOverlaysResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/publish", Publish)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("PublishOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("ArchiveOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{overlayIdentifier:guid}/draft", BranchDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("BranchDraftOverlayRevision")
            .Produces<int>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}", EditDraft)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("EditDraftOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{overlayIdentifier:guid}/revisions/{revisionNumber:int}/revert", Revert)
            .RequireAuthorization(Scope.Sse.Overlays.Write)
            .WithName("RevertOverlayRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}

/// <summary>
/// Envelope shape returned by <c>GET /overlays</c>. Either
/// <see cref="Chains"/> (admin view) or <see cref="Published"/>
/// (binding picker) is populated depending on the state filter; the
/// other is empty.
/// </summary>
public sealed record ListOverlaysResponse(
    IReadOnlyList<OverlayDto> Chains,
    IReadOnlyList<PublishedOverlayDto> Published);
