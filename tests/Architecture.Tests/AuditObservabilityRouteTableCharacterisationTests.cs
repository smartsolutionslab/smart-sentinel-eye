using Microsoft.AspNetCore.Builder;
using SmartSentinelEye.AuditObservability.Api;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Pins the whole AuditObservability route table as it stands (spec 400, #2974): every route, verb,
/// name, summary, tag, authorization policy, declared response and the handler's name and parameter
/// list, in registration order. ADR-0084's S107 suppression goes onto two of these handlers, and the
/// OpenAPI document and the generated clients are derived from exactly this metadata, so none of it
/// may move. The baseline was captured on <c>f950cb7c</c>, before any production edit, and is a
/// literal so a change shows up as a line diff.
/// </summary>
public class AuditObservabilityRouteTableCharacterisationTests
{
    private const string Baseline = """
        /audit/
          handler Search(fabId: System.String [FromQueryAttribute], actor: System.Nullable`1<System.Guid> [FromQueryAttribute], actorUsername: System.String [FromQueryAttribute], eventKind: System.String [FromQueryAttribute], resourceKind: System.String [FromQueryAttribute], resourceIdentifier: System.String [FromQueryAttribute], since: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], until: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], pageSize: System.Nullable`1<System.Int32> [FromQueryAttribute], cursor: System.String [FromQueryAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute,NullableAttribute], handler: SmartSentinelEye.AuditObservability.Application.Queries.Handlers.SearchAuditQueryHandler [FromServicesAttribute,NullableAttribute], user: System.Security.Claims.ClaimsPrincipal [NullableAttribute], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.audit.read roles= schemes=
          tags Audit
          other System.Runtime.CompilerServices.NullableContextAttribute
          name SearchAudit
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Cross-cutting audit search. Required scope: sse.audit.read
          produces 200 SmartSentinelEye.AuditObservability.Application.DTOs.AuditPageDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /audit/{resourceKind}/{resourceIdentifier}
          handler GetTimeline(resourceKind: System.String [], resourceIdentifier: System.String [], fabId: System.String [FromQueryAttribute], since: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], until: System.Nullable`1<System.DateTimeOffset> [FromQueryAttribute], pageSize: System.Nullable`1<System.Int32> [FromQueryAttribute], cursor: System.String [FromQueryAttribute,NullableAttribute], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.AuditObservability.Application.Queries.Handlers.GetResourceTimelineQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.audit.read roles= schemes=
          tags Audit
          name GetResourceAuditTimeline
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Per-resource audit timeline. Required scope: sse.audit.read
          produces 200 SmartSentinelEye.AuditObservability.Application.DTOs.AuditPageDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        /audit/{auditIdentifier:guid}
          handler GetSingle(auditIdentifier: System.Guid [], fabGuard: SmartSentinelEye.ServiceDefaults.Authorization.IFabAuthorizationGuard [FromServicesAttribute], handler: SmartSentinelEye.AuditObservability.Application.Queries.Handlers.GetAuditEventQueryHandler [FromServicesAttribute], user: System.Security.Claims.ClaimsPrincipal [], cancellationToken: System.Threading.CancellationToken [])
          methods GET
          authorize policy=sse.audit.read roles= schemes=
          tags Audit
          name GetAuditEvent
          other Microsoft.AspNetCore.Routing.RouteNameMetadata
          summary Single audit row + full payload. Required scope: sse.audit.read
          produces 200 SmartSentinelEye.AuditObservability.Application.DTOs.AuditRowDto [application/json]
          produces 401 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 400 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 403 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          produces 404 Microsoft.AspNetCore.Mvc.ProblemDetails [application/problem+json]
          other Microsoft.AspNetCore.Routing.RouteEndpointBuilder+RouteDiagnosticsMetadata
        """;

    [Fact]
    public async Task The_audit_observability_route_table_is_unchanged()
    {
        await using var app = WebApplication.CreateBuilder([]).Build();
        app.MapAuditEndpoints();

        var rendered = RouteTableRendering.RenderAll(InProcessEndpointDriver.Endpoints(app));

        rendered.ShouldBe(RouteTableRendering.Normalise(Baseline));
    }
}
