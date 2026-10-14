using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Infrastructure.Resolution;
using SmartSentinelEye.SystemVariables.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.SystemVariables.Infrastructure.Tests.Resolution;

/// <summary>
/// Characterisation (spec 349, ADR-0144), captured green on unmodified
/// <c>develop</c> before <c>ReverseIndexSeederHostedService.StartAsync</c> hands
/// its status logging and its overlay loop to helpers. It pins, for every
/// response the loop can meet, the log level and full text, which overlays were
/// upserted (in order, with their texts) and the count logged.
///
/// <para>
/// <c>ReverseIndexSeederHostedServiceTests</c> pins the levels of the refusals
/// and the happy parse; this adds the words, the status that is formatted into
/// them, the per-overlay skips and the count. Counterfactual seen to fail:
/// moving <c>seeded++</c> above the <c>elements</c> check turns
/// <see cref="Overlays_without_an_identifier_or_an_elements_array_are_skipped_and_not_counted"/>
/// red.
/// </para>
///
/// <para>
/// Spec 349 section 6 oddity (d) is pinned as it is: an identifier that is not a
/// Guid aborts the seed partway, so the overlays before it stay upserted, the
/// ones after it are never reached, and the count line is never logged.
/// </para>
/// </summary>
public class ReverseIndexSeederBranchCharacterisationTests
{
    private const string FirstOverlay = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";
    private const string SecondOverlay = "1b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e";
    private const string ThirdOverlay = "2c3d4e5f-6a7b-4c8d-ae9f-1a2b3c4d5e6f";

    [Fact]
    public async Task A_401_logs_the_refused_credential_error_naming_that_status()
    {
        var seed = await RunAsync(HttpStatusCode.Unauthorized, "{}");

        seed.Upserts.ShouldBeEmpty();
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldBe(
            "ReverseIndex seed: overlay-designer refused the system-variables-seeder credential (Unauthorized). "
            + "The index is empty and stays empty until the credential is fixed and the service restarted. "
            + "Check the client's secret and its sse.overlays.read scope.");
    }

    [Fact]
    public async Task A_403_logs_the_refused_credential_error_naming_that_status()
    {
        var seed = await RunAsync(HttpStatusCode.Forbidden, "{}");

        seed.Upserts.ShouldBeEmpty();
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldBe(
            "ReverseIndex seed: overlay-designer refused the system-variables-seeder credential (Forbidden). "
            + "The index is empty and stays empty until the credential is fixed and the service restarted. "
            + "Check the client's secret and its sse.overlays.read scope.");
    }

    [Fact]
    public async Task A_500_logs_the_self_healing_warning_naming_that_status()
    {
        var seed = await RunAsync(HttpStatusCode.InternalServerError, "{}");

        seed.Upserts.ShouldBeEmpty();
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "ReverseIndex seed: overlay-designer returned InternalServerError; starting with empty index. "
            + "The index will populate as new OverlayRevisionPublishedV3 events arrive.");
    }

    [Fact]
    public async Task A_404_takes_the_same_warning_branch_with_its_own_status()
    {
        var seed = await RunAsync(HttpStatusCode.NotFound, "{}");

        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "ReverseIndex seed: overlay-designer returned NotFound; starting with empty index. "
            + "The index will populate as new OverlayRevisionPublishedV3 events arrive.");
    }

    [Fact]
    public async Task A_body_without_the_published_key_logs_the_missing_key_warning_and_seeds_nothing()
    {
        var seed = await RunAsync(HttpStatusCode.OK, """{ "chains": [] }""");

        seed.Upserts.ShouldBeEmpty();
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe("ReverseIndex seed: response missing 'published' key; index left empty.");
    }

    [Fact]
    public async Task An_empty_published_list_logs_a_count_of_zero()
    {
        var seed = await RunAsync(HttpStatusCode.OK, """{ "published": [] }""");

        seed.Upserts.ShouldBeEmpty();
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("ReverseIndex seeded with 0 published overlays.");
    }

    [Fact]
    public async Task Overlays_without_an_identifier_or_an_elements_array_are_skipped_and_not_counted()
    {
        var body = $$$"""
            {
              "published": [
                { "overlayIdentifier": "{{{FirstOverlay}}}", "elements": [ { "kind": "Text", "text": "A {{x}}" }, { "kind": "Box" } ] },
                { "name": "no identifier", "elements": [ { "kind": "Text", "text": "ignored" } ] },
                { "overlayIdentifier": "not-even-a-guid", "name": "no elements" },
                { "overlayIdentifier": "also-not-a-guid", "elements": "not an array" },
                { "overlayIdentifier": "{{{SecondOverlay}}}", "elements": [ { "kind": "Text", "text": "B {{y}}" }, { "kind": "Text" } ] }
              ]
            }
            """;

        var seed = await RunAsync(HttpStatusCode.OK, body);

        seed.Upserts.Count.ShouldBe(2);
        seed.Upserts[0].Overlay.ShouldBe(Guid.Parse(FirstOverlay));
        seed.Upserts[0].Texts.ShouldBe(["A {{x}}", string.Empty]);
        seed.Upserts[1].Overlay.ShouldBe(Guid.Parse(SecondOverlay));
        seed.Upserts[1].Texts.ShouldBe(["B {{y}}", string.Empty]);
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Message.ShouldBe("ReverseIndex seeded with 2 published overlays.");
    }

    [Fact]
    public async Task A_non_guid_identifier_aborts_the_seed_after_the_overlays_before_it()
    {
        var body = $$$"""
            {
              "published": [
                { "overlayIdentifier": "{{{FirstOverlay}}}", "elements": [ { "kind": "Text", "text": "A {{x}}" } ] },
                { "overlayIdentifier": "not-a-guid", "elements": [ { "kind": "Text", "text": "never reached" } ] },
                { "overlayIdentifier": "{{{ThirdOverlay}}}", "elements": [ { "kind": "Text", "text": "C {{z}}" } ] }
              ]
            }
            """;

        var seed = await RunAsync(HttpStatusCode.OK, body);

        var upsert = seed.Upserts.ShouldHaveSingleItem();
        upsert.Overlay.ShouldBe(Guid.Parse(FirstOverlay));
        upsert.Texts.ShouldBe(["A {{x}}"]);
        var entry = seed.Logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Message.ShouldBe(
            "ReverseIndex seed failed; starting with empty index. Self-heal will kick in as overlay V1 events arrive.");
        entry.Exception.ShouldNotBeNull().GetType().ShouldBe(typeof(FormatException));
        entry.Exception.Message.ShouldBe("One of the identified items was in an invalid format.");
    }

    [Fact]
    public async Task A_cancelled_token_propagates_and_logs_nothing()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        CapturingLogger<ReverseIndexSeederHostedService> logger = new();
        ReverseIndexSeederHostedService seeder = new(
            new SingleClientFactory(new HttpClient(new StubOverlayDesigner(HttpStatusCode.OK, "{}"))
            {
                BaseAddress = new Uri("http://overlay-designer"),
            }),
            new RecordingReverseIndex(),
            logger);

        var thrown = await Should.ThrowAsync<OperationCanceledException>(
            () => seeder.StartAsync(cancelled.Token));

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
        logger.Entries.ShouldBeEmpty();
    }

    private static async Task<Seed> RunAsync(HttpStatusCode status, string body)
    {
        RecordingReverseIndex index = new();
        CapturingLogger<ReverseIndexSeederHostedService> logger = new();
        ReverseIndexSeederHostedService seeder = new(
            new SingleClientFactory(new HttpClient(new StubOverlayDesigner(status, body))
            {
                BaseAddress = new Uri("http://overlay-designer"),
            }),
            index,
            logger);

        await seeder.StartAsync(CancellationToken.None);

        return new Seed(logger, index.Upserts);
    }

    private sealed record Seed(
        CapturingLogger<ReverseIndexSeederHostedService> Logger,
        IReadOnlyList<(Guid Overlay, IReadOnlyList<string> Texts)> Upserts);

    private sealed class RecordingReverseIndex : IReverseIndex
    {
        public List<(Guid Overlay, IReadOnlyList<string> Texts)> Upserts { get; } = [];

        public void UpsertOverlayReferences(Guid overlayIdentifier, IReadOnlyList<string> labelTexts) =>
            Upserts.Add((overlayIdentifier, labelTexts));

        public void RemoveOverlay(Guid overlayIdentifier) => throw new NotSupportedException();

        public IReadOnlyCollection<Guid> LookupOverlays(string variableName) => throw new NotSupportedException();

        public IReadOnlyList<string>? LookupLabelTexts(Guid overlayIdentifier) => throw new NotSupportedException();

        public IReadOnlyCollection<Guid> AllOverlays() => throw new NotSupportedException();
    }

    private sealed class StubOverlayDesigner(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
