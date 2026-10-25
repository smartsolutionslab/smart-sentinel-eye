using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartSentinelEye.Identity.Api.Requests;
using SmartSentinelEye.Identity.Application.Commands;
using SmartSentinelEye.Identity.Application.Commands.Handlers;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.Queries;
using SmartSentinelEye.Identity.Application.Queries.Handlers;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.ServiceDefaults.Authorization;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Identity.Api;

/// <summary>
/// Endpoint for the hard-cut webhook migration (spec 008 US5 /
/// FR-014). Admins call this to lift a spec-006 webhook integration
/// onto a Keycloak service-account client; subsequent calls roll
/// the secret.
/// </summary>
public static class WebhookRotationEndpoints
{
    /// <summary>Route identity an idempotency key is scoped to (ADR-0142).</summary>
    private const string RotateEndpoint = "POST /webhook-integrations/{name}/rotate";

    public static IEndpointRouteBuilder MapWebhookRotationEndpoints(this IEndpointRouteBuilder app)
    {
        Ensure.That(app).IsNotNull();

        var group = app.MapGroup("/webhook-integrations")
            .RequireAuthorization(Scope.Sse.Webhooks.Write)
            .WithTags("IdentityWebhookRotation");

        group.MapPost("/{name}/rotate", Rotate)
            .WithName("RotateWebhookClient")
            .WithSummary("Rotate a webhook integration's bearer onto a Keycloak JWT. Send If-Match with the version from GET /webhook-integrations to rotate an existing client, or If-None-Match: * to create one. Required scope: sse.webhooks.write")
            .Produces<WebhookClientCredentialsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // Same scope as the rotation rather than a new sse.webhooks.read: this
        // list exists only to supply the If-Match the rotation demands, so
        // anyone who may rotate must be able to read it. A separate read scope
        // would let a principal hold the write scope its own description
        // promises and still be unable to rotate anything.
        group.MapGet("/", List)
            .WithName("ListWebhookClients")
            .WithSummary("List webhook service-account clients and the version each must be rotated at. Required scope: sse.webhooks.write")
            .Produces<IReadOnlyList<RegisteredClientSummaryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> List(
        [FromServices] IFabAuthorizationGuard fabGuard,
        [FromServices] ListWebhookClientsQueryHandler handler,
        ClaimsPrincipal user,
        CancellationToken cancellationToken,
        [FromQuery] string fabId = "")
    {
        var fabsResolution =
            await IdentityFabResolution.ResolveReadFabsAsync(
                new FabReadRequest(user, fabId),
                fabGuard,
                "WEBHOOK_INVALID_INPUT",
                cancellationToken);
        if (fabsResolution.IsFailure)
        {
            return fabsResolution.Error;
        }

        var fabs = fabsResolution.Value;

        var result =
            await handler.HandleAsync(new ListWebhookClientsQuery(fabs), cancellationToken);

        return result.Match<IResult>(
            onSuccess: Results.Ok,
            onFailure: error => error.ToProblem());
    }

    [SuppressMessage(
        "Major Code Smell",
        "S107:Methods should not have too many parameters",
        Justification = "ADR-0084 (2026-10-16 amendment): a route handler's parameters are its HTTP contract, "
            + "pinned by the route-table characterisation baseline; S107 does not apply to them.")]
    private static async Task<IResult> Rotate(
        string name,
        [FromBody] RotateWebhookClientRequest body,
        HttpRequest request,
        [AsParameters] RotateWebhookServices services,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Ensure.That(body).IsNotNull();
        Ensure.That(request).IsNotNull();

        if (!IdempotencyHeaders.TryRead(request, out var key, out var keyProblem))
        {
            return keyProblem;
        }

        if (!BoundaryParse.TryParse(() => FabIdentifier.From(body.FabId), "WEBHOOK_INVALID_INPUT", out var fab, out var invalid))
        {
            return invalid;
        }

        // The guard runs before the precondition is parsed, per
        // IFabAuthorizationGuard's contract that it is called right after model
        // binding. Reading If-Match first would answer a caller who may not
        // touch this fab at all with 428 and an invitation to retry, instead of
        // the 403 they have earned.
        await services.FabGuard.EnsureAccessAsync(user, fab.Value, cancellationToken);

        // This endpoint upserts, so the caller states which operation it means:
        // If-None-Match: * to create the client, If-Match: "N" to rotate the
        // one at version N. See ConcurrencyHeaders for why a lone If-Match
        // cannot carry both.
        if (!ConcurrencyHeaders.TryReadUpsertPrecondition(request, out var expectedVersion, out var precondition))
        {
            return precondition;
        }

        var actingOperator = user.ToOperatorIdentifier();

        // **A replay never reaches the precondition, and that is the point of
        // applying idempotency here.** The header is still parsed above — a
        // malformed or missing one is a 400 or 428 either way — but it is
        // *enforced* by the command handler, comparing the expected version to
        // the aggregate's, and a replay returns without calling the handler at
        // all. That matters because a retry carries the same If-Match it sent
        // the first time, while the first attempt already bumped the version:
        // re-running the command would answer 412 for a rotation that succeeded,
        // and the caller would have lost a secret it can no longer obtain. The
        // key proves this is the same request, which is a stronger statement
        // than the version it was written against.
        //
        // Rotation is also where a blind retry does real damage rather than
        // merely reporting badly: rotating twice invalidates the secret the
        // first attempt already delivered. If-Match happens to prevent that
        // today, but only because the version moved — it guards against
        // concurrent writers, not against a duplicate of one writer's request.
        // The fingerprint folds in the route `{name}` and the upsert
        // precondition, not only the body: a key reused across *create* and
        // *rotate* of the same integration — or across two different
        // integrations — is two different operations, not one replayed onto
        // the other (spec 302 / #2424).
        return await IdempotentRequest.ExecuteWithVersionAsync(
            new IdempotentExecution(
                RotateScope(key, actingOperator, fab, RotateFingerprint(body, name, expectedVersion)),
                services.Idempotency,
                services.Clock),
            async token => RotationOutcome(await services.Handler.HandleAsync(
                new RotateWebhookClientCommand(name, fab, actingOperator, expectedVersion),
                token)),
            (rotated, token) => ReplayRotationAsync(rotated, name, services.Replay, token),
            cancellationToken);
    }

    private static IdempotentOutcome RotationOutcome(Result<WebhookClientCredentialsDto, RotateWebhookClientError> result) =>
        result.Match(
            onSuccess: dto => IdempotentOutcome.CreatedAt(
                dto.RegisteredClientIdentifier,
                AggregateVersion.From(dto.Version),
                Results.Ok(dto)),
            onFailure: error => IdempotentOutcome.NothingCreated(error.ToProblem()));

    private static IdempotencyFingerprint RotateFingerprint(
        RotateWebhookClientRequest body,
        string name,
        Option<int> expectedVersion) =>
        IdempotencyFingerprint.Of(
            body,
            name,
            expectedVersion.Match(
                some: version => version.ToString(CultureInfo.InvariantCulture),
                none: () => "*"));

    private static Option<IdempotencyScope> RotateScope(
        Option<IdempotencyKey> key,
        OperatorIdentifier actingOperator,
        FabIdentifier fab,
        IdempotencyFingerprint fingerprint) =>
        key.Map(supplied => IdempotencyScope.For(
            supplied,
            RotateEndpoint,
            actingOperator.Value.ToString(),
            Option<string>.Some(fab.Value),
            fingerprint));

    private static async Task<IResult> ReplayRotationAsync(
        IdempotentReplay answered,
        string name,
        ReplayRegisteredClientQueryHandler replay,
        CancellationToken cancellationToken)
    {
        var (rotated, answeredAt) = answered;

        // Spec 454: a key completed before the version was recorded cannot prove
        // the client has not moved on, so it fails closed rather than replaying.
        if (!answeredAt.HasValue)
        {
            return new ReplayedKeySuperseded(rotated).ToProblem();
        }

        var replayed =
            await replay.HandleAsync(
                new ReplayRegisteredClientQuery(RegisteredClientIdentifier.From(rotated), answeredAt),
                cancellationToken);

        // The integration name comes from the route the retry repeated.
        return replayed.Match<IResult>(
            onSuccess: registered => Results.Ok(
                registered.ToWebhookClientCredentialsDto(name)),
            onFailure: error => error.ToProblem());
    }

    /// <summary>
    /// The rotation endpoint's collaborators, bundled with
    /// <c>[AsParameters]</c> so the handler keeps a readable signature (ADR-0084).
    /// </summary>
    private sealed record RotateWebhookServices(
        [FromServices] IFabAuthorizationGuard FabGuard,
        [FromServices] RotateWebhookClientCommandHandler Handler,
        [FromServices] ReplayRegisteredClientQueryHandler Replay,
        [FromServices] IIdempotencyStore Idempotency,
        [FromServices] TimeProvider Clock);
}
