using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.WebhookIntegrations;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Identity.Infrastructure.WebhookIntegrations;

namespace SmartSentinelEye.Identity.Infrastructure.Tests.WebhookIntegrations;

/// <summary>
/// Spec 318 (#2628), plan §2 — the mapping table from EventIngestion's
/// <c>GET /webhook-integrations?fabId={fab}&amp;includeRevoked=true</c> to
/// <see cref="WebhookIntegrationStatus"/>, and the fail-closed direction
/// (<c>Unverifiable</c>) for everything that is not a clean <c>200</c> with
/// the two fields the adapter reads.
///
/// <para>
/// A1 is the pinning test plan §2 calls for explicitly: without
/// <c>includeRevoked=true</c> on the request, a revoked row is filtered out
/// by <c>ListWebhookIntegrationsQueryHandler</c> and would read back as
/// <c>NotRegistered</c> — the defect in a new form.
/// </para>
/// </summary>
public class EventIngestionWebhookIntegrationStatusLookupTests
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Fact]
    public async Task The_request_asks_for_this_fab_with_revoked_rows_included()
    {
        StubHandler stub = new((_, _) => Rows());

        await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        RecordedRequest request = stub.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe("GET");
        request.PathAndQuery.ShouldContain("fabId=munich");
        request.PathAndQuery.ShouldContain("includeRevoked=true");
    }

    [Fact]
    public async Task A_matching_row_with_no_revokedAt_is_active()
    {
        StubHandler stub = new((_, _) => Rows(Row("a", revokedAt: null)));

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Active);
    }

    [Fact]
    public async Task A_matching_row_with_a_revokedAt_timestamp_is_revoked()
    {
        StubHandler stub = new((_, _) => Rows(Row("a", revokedAt: "2026-10-01T00:00:00Z")));

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Revoked);
    }

    [Fact]
    public async Task No_matching_row_including_an_empty_array_is_not_registered()
    {
        StubHandler stub = new((_, _) => Rows());

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.NotRegistered);
    }

    [Fact]
    public async Task A_row_whose_name_differs_only_by_suffix_is_not_registered()
    {
        StubHandler stub = new((_, _) => Rows(Row("a-2", revokedAt: null)));

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.NotRegistered);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_2xx_answer_is_unverifiable(HttpStatusCode refusal)
    {
        StubHandler stub = new((_, _) => "{\"error\":\"stub refusal\"}", refusal);

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_unverifiable()
    {
        StubHandler stub = new((_, _) => "not json");

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    [Fact]
    public async Task A_body_that_is_not_an_array_is_unverifiable()
    {
        StubHandler stub = new((_, _) => "{}");

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    [Fact]
    public async Task A_matching_row_missing_revokedAt_is_unverifiable()
    {
        StubHandler stub = new((_, _) => """[{"name":"a"}]""");

        WebhookIntegrationStatus status = await Lookup(stub).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    [Fact]
    public async Task A_transport_failure_is_unverifiable()
    {
        ThrowingHandler handler = new(new HttpRequestException("connection refused"));

        WebhookIntegrationStatus status =
            await Lookup(handler).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    [Fact]
    public async Task A_cancelled_callers_token_propagates()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();
        StubHandler stub = new((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Rows();
        });

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Lookup(stub).GetStatusAsync(Munich, "a", cts.Token));
    }

    /// <summary>
    /// A resilience-policy timeout looks like a <c>TaskCanceledException</c>
    /// too, but the caller's own token was never cancelled — this must not be
    /// mistaken for the case above and must answer <c>Unverifiable</c>, not
    /// propagate.
    /// </summary>
    [Fact]
    public async Task A_timeout_that_is_not_the_callers_cancellation_is_unverifiable()
    {
        ThrowingHandler handler = new(new TaskCanceledException("resilience timeout"));

        WebhookIntegrationStatus status =
            await Lookup(handler).GetStatusAsync(Munich, "a", CancellationToken.None);

        status.ShouldBe(WebhookIntegrationStatus.Unverifiable);
    }

    private static IWebhookIntegrationStatusLookup Lookup(HttpMessageHandler handler) =>
        new EventIngestionWebhookIntegrationStatusLookup(
            new HttpClient(handler) { BaseAddress = new Uri("http://event-ingestion") },
            NullLogger<EventIngestionWebhookIntegrationStatusLookup>.Instance);

    private static string Rows(params string[] rows) => $"[{string.Join(",", rows)}]";

    private static string Row(string name, string? revokedAt) =>
        revokedAt is null
            ? $$"""{"name":"{{name}}","revokedAt":null}"""
            : $$"""{"name":"{{name}}","revokedAt":"{{revokedAt}}"}""";

    private sealed record RecordedRequest(string Method, string PathAndQuery);

    private sealed class StubHandler(
        Func<RecordedRequest, CancellationToken, string> respondBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RecordedRequest recorded = new(request.Method.Method, request.RequestUri!.PathAndQuery);
            Requests.Add(recorded);

            string body = respondBody(recorded, cancellationToken);
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => throw exception;
    }
}
