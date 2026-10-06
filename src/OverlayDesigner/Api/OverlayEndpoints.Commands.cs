using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.OverlayDesigner.Api.Requests;
using SmartSentinelEye.OverlayDesigner.Application.Commands;
using SmartSentinelEye.OverlayDesigner.Application.Commands.Handlers;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Api;

/// <summary>Command (write) handlers for <see cref="OverlayEndpoints"/>.</summary>
public static partial class OverlayEndpoints
{
    /// <summary>
    /// Parses one wire-shape element into the domain value object. Called in
    /// a loop inside the caller's single <c>try</c> (spec 150; spec 300
    /// #2349 widened it to a kind switch), so the first bad element in the
    /// set 400s the whole set naming its field — the existing
    /// single-element behaviour, reproduced rather than re-plumbed.
    /// </summary>
    private static OverlayElement ParseElement(ElementRequest request)
    {
        Ensure.That(request).IsNotNull();

        NormalizedPosition position = NormalizedPosition.From(request.NormalizedX, request.NormalizedY);
        NormalizedSize size = NormalizedSize.From(request.NormalizedWidth, request.NormalizedHeight);
        OverlayColor color = ParseColor(request.Color);
        ElementKind kind = ParseKind(request.Kind);

        return kind == ElementKind.Text
            ? ParseTextElement(request, position, size, color)
            : ParseShapeElement(kind, request, position, size, color);
    }

    /// <summary>
    /// <see cref="OverlayColor.From"/>'s own guard names its parameter
    /// <c>value</c> (<c>Ensure.That</c>'s default), which is correct for the
    /// value object's own tests (T005) but wrong at this trust boundary —
    /// FR-003 requires the problem detail to name <c>color</c>. Re-thrown
    /// here rather than changed at the source, so the domain-level
    /// expectation stays exactly what T005 pins.
    /// </summary>
    private static OverlayColor ParseColor(string color)
    {
        try
        {
            return OverlayColor.From(color);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(
                $"color must be a six- or eight-digit hex colour, e.g. #RRGGBB or #RRGGBBAA. Got '{color}'.",
                nameof(color));
        }
    }

    /// <summary>Same re-naming as <see cref="ParseColor"/>, for <see cref="ElementKind.From"/> (FR-002/FR-003).</summary>
    private static ElementKind ParseKind(string kind)
    {
        try
        {
            return ElementKind.From(kind);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"kind: unknown ElementKind '{kind}'.", nameof(kind));
        }
    }

    /// <summary>
    /// A <c>Box</c>/<c>Ellipse</c> carries neither <c>text</c> nor
    /// <c>fontSizePx</c> (FR-004) — guarded here, naming the offending
    /// field, because <see cref="OverlayElement.Box"/>/<see cref="OverlayElement.Ellipse"/>
    /// take no such arguments to silently drop.
    /// </summary>
    private static OverlayElement ParseShapeElement(
        ElementKind kind, ElementRequest request, NormalizedPosition position, NormalizedSize size, OverlayColor color)
    {
        RequireNoTextFields(kind, request.Text, request.FontSizePx);

        return kind == ElementKind.Box
            ? OverlayElement.Box(position, size, color)
            : OverlayElement.Ellipse(position, size, color);
    }

    private static void RequireNoTextFields(ElementKind kind, string? text, int? fontSizePx)
    {
        if (text is not null)
        {
            throw new ArgumentException($"text must be absent for a {kind} element.", nameof(text));
        }
        if (fontSizePx is not null)
        {
            throw new ArgumentException($"fontSizePx must be absent for a {kind} element.", nameof(fontSizePx));
        }
    }

    /// <summary>
    /// <paramref name="request"/>'s <c>Text</c>/<c>FontSizePx</c> are
    /// nullable at the wire boundary (absent for <c>Box</c>/<c>Ellipse</c>).
    /// A missing value here defaults to the type's zero value rather than
    /// being guarded directly, so the factory's own guards
    /// (<see cref="TextContent.From"/>) are the single place that rejects it
    /// — naming <c>text</c> or <c>fontSizePx</c> exactly as they would for a
    /// present-but-invalid value.
    /// </summary>
    private static OverlayElement ParseTextElement(
        ElementRequest request, NormalizedPosition position, NormalizedSize size, OverlayColor color)
    {
        string text = request.Text ?? string.Empty;
        int fontSizePx = request.FontSizePx ?? 0;
        return OverlayElement.TextElement(text, fontSizePx, position, size, color);
    }

    private static async Task<IResult> CreateDraft(
        [FromBody] CreateOverlayRequest body,
        [AsParameters] CreateOverlayServices services,
        HttpContext http,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(http).IsNotNull();

        CreateOverlayDraftCommandHandler handler = services.Handler;

        if (!IdempotencyHeaders.TryRead(http.Request, out Option<IdempotencyKey> key, out IResult? keyProblem))
        {
            return keyProblem;
        }

        OverlayName name;
        List<OverlayElement> elements;
        try
        {
            Ensure.That(body.Elements).IsNotNull();
            name = OverlayName.From(body.Name);
            elements = [.. body.Elements.Select(ParseElement)];
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();

        return await IdempotentRequest.ExecuteCreateAsync(
            new IdempotentExecution(
                key.Map(supplied => IdempotencyScope.For(
                    supplied,
                    CreateEndpoint,
                    actingOperator.Value.ToString(),
                    Option<string>.None,
                    IdempotencyFingerprint.Of(body))),
                services.Idempotency,
                services.Clock),
            identifier => $"/overlays/{identifier}",
            async token => (await handler.HandleAsync(
                    new CreateOverlayDraftCommand(name, elements, actingOperator), token)).Match(
                onSuccess: identifier => Result<Guid, IResult>.Success(identifier.Value),
                onFailure: error => Result<Guid, IResult>.Failure(error.ToProblem())),
            cancellationToken);
    }

    /// <summary>
    /// Bundled with <c>[AsParameters]</c> so the handler keeps a readable
    /// signature (ADR-0084).
    /// </summary>
    private sealed record CreateOverlayServices(
        [FromServices] CreateOverlayDraftCommandHandler Handler,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);

    private static async Task<IResult> Publish(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] PublishRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out OverlayRevisionNumber number,
            out IResult? problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<OverlayRevisionNumber, PublishRevisionError> result = await handler
            .HandleAsync(
                new PublishRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: published => Results.Ok(published.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Archive(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] ArchiveRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out OverlayRevisionNumber number,
            out IResult? problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<OverlayRevisionNumber, ArchiveRevisionError> result = await handler
            .HandleAsync(
                new ArchiveRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: archived => Results.Ok(archived.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> BranchDraft(
        Guid overlayIdentifier,
        HttpRequest request,
        [FromServices] BranchDraftRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<OverlayRevisionNumber, BranchDraftRevisionError> result = await handler
            .HandleAsync(
                new BranchDraftRevisionCommand(OverlayIdentifier.From(overlayIdentifier), actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: branched => Results.Created(
                $"/overlays/{overlayIdentifier}/revisions/{branched.Value}", branched.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> EditDraft(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromBody] EditDraftRequest body,
        [FromServices] EditDraftRevisionCommandHandler handler,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        OverlayRevisionNumber number;
        List<OverlayElement> elements;
        try
        {
            Ensure.That(body.Elements).IsNotNull();
            number = OverlayRevisionNumber.From(revisionNumber);
            elements = [.. body.Elements.Select(ParseElement)];
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        Result<OverlayRevisionNumber, EditDraftRevisionError> result = await handler
            .HandleAsync(
                new EditDraftRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, elements, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: edited => Results.Ok(edited.Value),
            onFailure: error => error.ToProblem());
    }

    private static async Task<IResult> Revert(
        Guid overlayIdentifier,
        int revisionNumber,
        HttpRequest request,
        [FromServices] RevertRevisionCommandHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        if (overlayIdentifier == Guid.Empty)
        {
            return Results.Problem(
                title: "OVERLAY_INVALID_INPUT",
                detail: "overlayIdentifier must be a non-empty Guid.",
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (!BoundaryParse.TryParse(
            () => OverlayRevisionNumber.From(revisionNumber),
            "OVERLAY_INVALID_INPUT",
            out OverlayRevisionNumber number,
            out IResult? problem))
        {
            return problem;
        }

        if (!ConcurrencyHeaders.TryReadExpectedVersion(request, out int expectedVersion, out IResult? precondition))
        {
            return precondition;
        }

        OperatorIdentifier actingOperator = user.ToOperatorIdentifier();
        Result<OverlayRevisionNumber, RevertRevisionError> result = await handler
            .HandleAsync(
                new RevertRevisionCommand(OverlayIdentifier.From(overlayIdentifier), number, actingOperator, expectedVersion),
                cancellationToken);

        return result.Match<IResult>(
            onSuccess: reverted => Results.Ok(reverted.Value),
            onFailure: error => error.ToProblem());
    }
}
