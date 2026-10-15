using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole EventIngestion route table as it stands (spec 365, #2930, FR-002): every
/// route, verb, name, summary, tag, authorization policy, <c>AllowAnonymous</c>, declared
/// response and the handler's name and parameter list, in registration order. The Map methods
/// and the handlers behind them are being split to fit ADR-0084's thirty lines, and the OpenAPI
/// document and the generated clients are derived from exactly this metadata, so none of it may
/// move.
///
/// <para>
/// The baseline was captured on <c>c5cde739</c>, before any production edit, and is a literal so
/// a change shows up as a line diff rather than as two renderings that happen to agree. It
/// contains no absolute path and nothing ordered by hash. Attribute names within one parameter
/// are sorted because reflection does not promise an order.
/// </para>
/// </summary>
public class EventIngestionRouteTableCharacterisationTests
{
    private const string Baseline = """
        /events/manual
          handler IngestManual(body: SmartSentinelEye.EventIngestion.Api.Requests.IngestManualEventRequest [FromBodyAttribute], services: SmartSentinelEye.EventIngestion.Api.EventsEndpoints+IngestManualServices [AsParametersAttribute], http: Microsoft.AspNetCore.Http.HttpContext [], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Events
          accepts SmartSentinelEye.EventIngestion.Api.Requests.IngestManualEventRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.events.write roles= schemes=
          name IngestManualEvent
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Ingest an event into the resolved fab. Omit fabId when you belong to exactly one; name it when you belong to several (ADR-0114). The fab is never taken from the request unchecked (spec 018 FR-006). Stores the event before answering, so a 201 means it is readable at the Location (spec 020 FR-001). A declared discovery source's unregistered kind answers 202 and is held for review, not stored (spec 317, #2325). Required scope: sse.events.write
          produces 201 System.Guid [application/json]
          produces 202 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 429 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 503 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /events/webhook/{integrationName}
          handler IngestWebhook(integrationName: System.String [], body: SmartSentinelEye.EventIngestion.Api.Requests.IngestWebhookEventRequest [FromBodyAttribute], fabId: System.String [FromQueryAttribute], request: Microsoft.AspNetCore.Http.HttpRequest [], limiter: SmartSentinelEye.EventIngestion.Application.Ingress.IngestWriteLimiter [FromServicesAttribute], scopeFactory: Microsoft.Extensions.DependencyInjection.IServiceScopeFactory [FromServicesAttribute], integrations: SmartSentinelEye.EventIngestion.Domain.WebhookIntegration.IWebhookIntegrationRepository [FromServicesAttribute], storage: SmartSentinelEye.EventIngestion.Application.Ingress.IFabStorageReadiness [FromServicesAttribute], clock: SmartSentinelEye.Shared.Kernel.IClock [FromServicesAttribute], loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory [FromServicesAttribute], cache: Microsoft.Extensions.Caching.Memory.IMemoryCache [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags Events
          accepts SmartSentinelEye.EventIngestion.Api.Requests.IngestWebhookEventRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          allow-anonymous
          name IngestWebhookEvent
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Ingest an event delivered by a registered webhook integration. No OIDC scope: the caller presents the integration's own Authorization: Bearer token and the handler validates it — by default a SHA-256 match against the stored hash, or, once the integration is rotated, a Keycloak JWT that must carry sse.events.write, an azp matching the integration's client and the group /fabs/{fabId}. In both modes the delivery's fabId must be the integration's own, and every refusal collapses to one 401 so the answer never reveals which integrations exist. A declared discovery source's unregistered kind answers 202 and is held for review (spec 317, #2325).
          produces 201 System.Guid [application/json]
          produces 202 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 429 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 503 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /events/
          handler ListEvents(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute,NullableAttribute], user: System.Security.Claims.ClaimsPrincipal [NullableAttribute], fabId: System.String [FromQueryAttribute], source: System.String [FromQueryAttribute], deviceId: System.String [FromQueryAttribute], kind: System.String [FromQueryAttribute], occurredAfter: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], occurredBefore: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], ingestedAfter: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], ingestedBefore: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], pageSize: System.Nullable`1<System.Int32> [FromQueryAttribute], cursor: System.String [FromQueryAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.ListEventsQueryHandler [FromServicesAttribute,NullableAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.events.read roles= schemes=
          tags EventsRead
          other System.Runtime.CompilerServices.NullableContextAttribute
          name ListEvents
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List events from the fabs you hold. Required scope: sse.events.read
          produces 200 SmartSentinelEye.EventIngestion.Application.DTOs.EventPageDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /events/{eventId:guid}
          handler GetEvent(eventId: System.Guid [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.GetEventQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.events.read roles= schemes=
          tags EventsRead
          name GetEvent
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Read one event by its identifier, within the fabs you hold. Required scope: sse.events.read
          produces 200 SmartSentinelEye.EventIngestion.Application.DTOs.EventDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /events/dead-letters
          handler ListDeadLetters(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], limit: System.Nullable`1<System.Int32> [FromQueryAttribute], reason: System.String [FromQueryAttribute,NullableAttribute], state: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.ListDeadLettersQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.events.read roles= schemes=
          tags EventsRead
          name ListDeadLetters
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary List rejected or held deliveries from the fabs you hold. A delivery whose plant could not be established from its address is returned to nobody (spec 018 FR-011). Optional ?reason= (ParseFailure | Refused | UnknownEventType) and ?state= (Held | Promoted) narrow the listing (spec 317, #2325, FR-008). Required scope: sse.events.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.EventIngestion.Application.DTOs.DeadLetterDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-sources/
          handler Declare(body: SmartSentinelEye.EventIngestion.Api.Requests.DeclareSourceModeRequest [FromBodyAttribute], services: SmartSentinelEye.EventIngestion.Api.EventSourcesEndpoints+DeclareSourceModeServices [AsParametersAttribute], request: Microsoft.AspNetCore.Http.HttpRequest [], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags EventIngestion
          accepts SmartSentinelEye.EventIngestion.Api.Requests.DeclareSourceModeRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.events.types.write roles= schemes=
          summary Declare an admission mode (strict or discovery) for a source in the resolved fab. Omit fabId when you belong to exactly one; name it when you belong to several (ADR-0114). Honours Idempotency-Key (ADR-0142). Reuses the event-type write scope, so an event source cannot decide its own policing (FR-013). Required scope: sse.events.types.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-sources/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.ListSourceModesQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          tags EventIngestion
          authorize policy=sse.events.read roles= schemes=
          summary List the declared source modes of the fabs you hold. An undeclared source is open: unknown kinds are stored as they always were. A declared discovery source holds them; a declared strict source refuses them. An undeclared source is not listed. Required scope: sse.events.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.EventIngestion.Application.DTOs.SourceModeDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-sources/{source}/mode
          handler Change(source: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], body: SmartSentinelEye.EventIngestion.Api.Requests.ChangeSourceModeRequest [FromBodyAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Commands.Handlers.ChangeSourceModeCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods PUT
          tags EventIngestion
          accepts SmartSentinelEye.EventIngestion.Api.Requests.ChangeSourceModeRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.events.types.write roles= schemes=
          summary Change a declared source's mode in the resolved fab. Requires If-Match with the version from GET /event-sources. Required scope: sse.events.types.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-sources/{source}
          handler Undeclare(source: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Commands.Handlers.UndeclareSourceModeCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods DELETE
          tags EventIngestion
          authorize policy=sse.events.types.write roles= schemes=
          summary Return a declared source to undeclared in the resolved fab (spec 317, #2325) — the way back from strict or discovery. Requires If-Match with the version from GET /event-sources. Reuses the event-type write scope, so an event source cannot undeclare its own policing. Required scope: sse.events.types.write
          produces 204 System.Void []
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-types/
          handler Register(body: SmartSentinelEye.EventIngestion.Api.Requests.RegisterEventTypeRequest [FromBodyAttribute], services: SmartSentinelEye.EventIngestion.Api.EventTypesEndpoints+RegisterEventTypeServices [AsParametersAttribute], request: Microsoft.AspNetCore.Http.HttpRequest [], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          tags EventIngestion
          accepts SmartSentinelEye.EventIngestion.Api.Requests.RegisterEventTypeRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          authorize policy=sse.events.types.write roles= schemes=
          summary Declare an event type the resolved fab expects. Omit fabId when you belong to exactly one; name it when you belong to several (ADR-0114). Honours Idempotency-Key (ADR-0142). A distinct write scope from event ingest, so an event source cannot declare which event types are legitimate (FR-010). Also promotes that fab's held rows of this kind from a declared discovery pair's quarantine (spec 317, #2325). Required scope: sse.events.types.write
          produces 201 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 422 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-types/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.ListEventTypesQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          tags EventIngestion
          authorize policy=sse.events.read roles= schemes=
          summary List the registered event types of the fabs you hold. Retired entries are excluded. Required scope: sse.events.read
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.EventIngestion.Application.DTOs.RegisteredEventTypeDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /event-types/{kind}
          handler Retire(kind: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Commands.Handlers.RetireEventTypeCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods DELETE
          tags EventIngestion
          authorize policy=sse.events.types.write roles= schemes=
          summary Retire a registered event type, releasing its name for re-registration. Omit fabId when you belong to exactly one; name it when you belong to several (ADR-0114) — a multi-fab caller who names none is refused rather than having a fab guessed for them. Requires If-Match with the version from GET /event-types. Required scope: sse.events.types.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /webhook-integrations/
          handler Register(body: SmartSentinelEye.EventIngestion.Api.Requests.RegisterWebhookIntegrationRequest [FromBodyAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Commands.Handlers.RegisterWebhookIntegrationCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods POST
          authorize policy=sse.webhooks.write roles= schemes=
          tags EventIngestion
          accepts SmartSentinelEye.EventIngestion.Api.Requests.RegisterWebhookIntegrationRequest optional=False [application/json]
          other Microsoft.AspNetCore.Http.Metadata.DisableCookieRedirectMetadata
          summary Register a webhook integration for the resolved fab. Omit fabId when you belong to exactly one; name it when you belong to several (ADR-0114). The integration's fab decides which plant its deliveries may name. Required scope: sse.webhooks.write
          produces 201 SmartSentinelEye.EventIngestion.Api.WebhookIntegrationsEndpoints+RegisteredWebhookResponse [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /webhook-integrations/
          handler List(fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], includeRevoked: System.Nullable`1<System.Boolean> [FromQueryAttribute], handler: SmartSentinelEye.EventIngestion.Application.Queries.Handlers.ListWebhookIntegrationsQueryHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.webhooks.write roles= schemes=
          tags EventIngestion
          summary List the webhook integrations of the fabs you hold. Required scope: sse.webhooks.write
          produces 200 System.Collections.Generic.IReadOnlyList`1<SmartSentinelEye.EventIngestion.Application.DTOs.WebhookIntegrationDto> [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /webhook-integrations/{name}
          handler Revoke(name: System.String [], request: Microsoft.AspNetCore.Http.HttpRequest [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], fabId: System.String [FromQueryAttribute,NullableAttribute], handler: SmartSentinelEye.EventIngestion.Application.Commands.Handlers.RevokeWebhookIntegrationCommandHandler [FromServicesAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods DELETE
          authorize policy=sse.webhooks.write roles= schemes=
          tags EventIngestion
          summary Revoke a webhook integration. Requires If-Match with the version from GET /webhook-integrations. Required scope: sse.webhooks.write
          produces 200 System.Guid [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 409 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 428 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_event_ingestion_route_table_is_unchanged()
    {
        await using var host = new EventIngestionEndpointHost();

        var rendered = Normalise(string.Join("\n", host.Endpoints.Select(Render)));

        rendered.ShouldBe(Normalise(Baseline));
    }

    [Fact]
    public async Task The_event_ingestion_route_table_has_fifteen_routes()
    {
        await using var host = new EventIngestionEndpointHost();

        host.Endpoints.Count().ShouldBe(15);
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Trim();

    private static string Render(RouteEndpoint endpoint)
    {
        StringBuilder lines = new();
        lines.Append(endpoint.RoutePattern.RawText).Append('\n');

        foreach (var item in endpoint.Metadata.Where(IsContract))
        {
            lines.Append("  ").Append(item switch
            {
                MethodInfo method => "handler " + method.Name + "(" + string.Join(", ", method.GetParameters().Select(RenderParameter)) + ")",
                IHttpMethodMetadata methods => "methods " + string.Join(",", methods.HttpMethods),
                IEndpointNameMetadata name => "name " + name.EndpointName,
                IEndpointSummaryMetadata summary => "summary " + summary.Summary,
                ITagsMetadata tags => "tags " + string.Join(",", tags.Tags),
                IAllowAnonymous => "allow-anonymous",
                IAuthorizeData authorize => $"authorize policy={authorize.Policy} roles={authorize.Roles} schemes={authorize.AuthenticationSchemes}",
                IProducesResponseTypeMetadata produces =>
                    $"produces {produces.StatusCode} {RenderType(produces.Type)} [{string.Join(",", produces.ContentTypes)}]",
                IAcceptsMetadata accepts =>
                    $"accepts {RenderType(accepts.RequestType)} optional={accepts.IsOptional} [{string.Join(",", accepts.ContentTypes)}]",
                _ => "other " + RenderType(item.GetType()),
            }).Append('\n');
        }

        return lines.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Two kinds of metadata are compiler or binder artefacts rather than contract: the async
    /// state-machine marker (it follows whether a handler body awaits) and the per-parameter
    /// binding record (it is derived from the parameter list, which is rendered in full).
    /// </summary>
    private static bool IsContract(object item) =>
        item.GetType().Name != "ParameterBindingMetadata" && item is not AsyncStateMachineAttribute;

    private static string RenderParameter(ParameterInfo parameter)
    {
        var attributes = parameter.GetCustomAttributes(false)
            .Select(attribute => attribute.GetType().Name)
            .Order(StringComparer.Ordinal);
        var text = $"{parameter.Name}: {RenderType(parameter.ParameterType)}";
        if (parameter.HasDefaultValue)
        {
            text += $" = '{parameter.DefaultValue}'";
        }

        return text + " [" + string.Join(",", attributes) + "]";
    }

    private static string RenderType(Type? type)
    {
        if (type is null)
        {
            return "-";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName!;

        return definition + "<" + string.Join(",", type.GetGenericArguments().Select(RenderType)) + ">";
    }
}
