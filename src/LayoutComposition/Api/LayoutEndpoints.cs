using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartSentinelEye.LayoutComposition.Application.DTOs;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Api;

/// <summary>
/// Minimal-API endpoint group for LayoutComposition (ADR-0070), covering
/// the layout draft/revision lifecycle: create, read, list, publish,
/// archive, branch, edit, revert. The handlers are split across partial
/// files by message kind — <c>LayoutEndpoints.Commands.cs</c> and
/// <c>LayoutEndpoints.Queries.cs</c> — mirroring the Application layout.
/// </summary>
public static partial class LayoutEndpoints
{
    public static IEndpointRouteBuilder MapLayoutEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/layouts")
            .WithTags("Layouts");

        group.MapPost("/", CreateDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("CreateLayoutDraft")
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{layoutIdentifier:guid}", GetOne)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("GetLayout")
            .Produces<LayoutDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", List)
            .RequireAuthorization(Scope.Sse.Layouts.Read)
            .WithName("ListLayouts")
            .Produces<ListLayoutsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/publish", Publish)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("PublishRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/archive", Archive)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("ArchiveRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{layoutIdentifier:guid}/draft", BranchDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("BranchDraftRevision")
            .Produces<int>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}", EditDraft)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("EditDraftRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{layoutIdentifier:guid}/revisions/{revisionNumber:int}/revert", Revert)
            .RequireAuthorization(Scope.Sse.Layouts.Write)
            .WithName("RevertRevision")
            .Produces<int>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}

/// <summary>
/// Envelope shape returned by <c>GET /layouts</c>. Either
/// <see cref="Chains"/> (admin view) or <see cref="Published"/>
/// (kiosk picker) is populated depending on the state filter; the
/// other is empty.
/// </summary>
public sealed record ListLayoutsResponse(
    IReadOnlyList<LayoutDto> Chains,
    IReadOnlyList<PublishedLayoutDto> Published);
