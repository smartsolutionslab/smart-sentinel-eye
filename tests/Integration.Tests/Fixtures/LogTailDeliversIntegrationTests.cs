using System.Reflection;
using Xunit.Abstractions;

namespace SmartSentinelEye.Integration.Tests.Fixtures;

/// <summary>
/// #2054 — the log tail has never delivered a line, and nothing said so.
///
/// <para>
/// <c>ResourceLoggerService.WatchAsync</c> keys on the DCP resource id
/// (<c>camera-catalog-thwaubpm</c>), not on the app-model name.
/// <see cref="AspireFixture"/> passes the name, so every subscription watches a
/// stream that carries nothing, and <c>RecentLogs</c> answers
/// <c>(tail subscribed but the resource emitted nothing)</c> for a service that
/// has plainly emitted something. That placeholder is what the seven call sites
/// of #2053 have been printing where a stack trace was wanted.
/// </para>
///
/// <para>
/// <b>No source scan can catch this.</b> <c>LogTailCoverageTests</c> is green
/// today and truthfully so — the tailed list and the call sites do agree. What
/// was never observed is <i>delivery</i>, which only a booted fixture can show.
/// The tests live here because this assembly already pays for that boot once.
/// </para>
///
/// <para>
/// <b>Nothing in this file asserts <c>ShouldNotBeNullOrEmpty</c>, deliberately.</b>
/// All three placeholder strings satisfy it, which is the entire trap. The
/// load-bearing assertion is that the tail contains a token the test invented
/// seconds earlier; that rules out "not tailed", "empty subscription", "faulted
/// tail", "stale rather than live" and "another resource's stream" at once.
/// </para>
///
/// <para>
/// <b>The three placeholder checks therefore run <i>before</i> it, not after.</b>
/// A tail carrying the token cannot also be a placeholder, so ordered the other
/// way round they were unreachable: the token assertion threw first on every
/// input that could have reached them. They exist to name which placeholder came
/// back, which is a better message than "the token is missing, here is the
/// string" — but only if they are reached.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class LogTailDeliversIntegrationTests(AspireFixture aspire, ITestOutputHelper output)
{
    private const string CameraCatalogResource = "camera-catalog";
    private const string EventIngestionResource = "event-ingestion";
    private const string MultiFabOperator = "op-multi@smart-sentinel-eye.test";
    private const string OperatorPassword = "Operator1234";

    // Matched by prefix where the placeholder interpolates a resource name or a
    // failure reason, so the assertion does not depend on that variable half.
    private const string NotTailedPrefix = "(not tailed";
    private const string TailFailedPrefix = "(log tail failed:";
    private const string SubscribedButSilent = "(tail subscribed but the resource emitted nothing)";

    private const int DeliveryTimeoutSeconds = 30;

    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(DeliveryTimeoutSeconds);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A camera registration writes <c>Registered camera {CameraIdentifier} with
    /// name {CameraName}.</c> at <c>Information</c> from application code — not
    /// from ASP.NET's request logging, which is pinned to <c>Warning</c> in
    /// <c>appsettings.json</c> and would never appear.
    /// </summary>
    [Fact]
    public async Task A_camera_registration_reaches_the_camera_catalog_log_tail()
    {
        string token = InventedToken("logtail-camera");

        using HttpClient cameras = await aspire.CreateAuthenticatedClientAsync(
            CameraCatalogResource, MultiFabOperator, OperatorPassword);

        HttpResponseMessage created = await cameras.PostAsJsonAsync(
            "/cameras?fabId=munich",
            new { name = token, rtspUrl = "rtsp://10.0.5.42/h264" });
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        string tail = await PollForAsync(CameraCatalogResource, token);

        ShouldCarry(CameraCatalogResource, token, tail);
    }

    /// <summary>
    /// Breadth, at the cost of one dictionary read each. Test A proves the
    /// mechanism for one resource; the id is resolved per name, so a resource
    /// whose snapshot never appears under the spelling the fixture uses would
    /// stay silent and A would not notice. This test cannot say <i>whose</i>
    /// logs it read, which is why A exists.
    /// </summary>
    /// <remarks>
    /// <b>Polls, for the same reason A and C do.</b> It asserts on eight
    /// resources that started concurrently and it drives none of them, so the
    /// only thing making the first read succeed is that each has already written
    /// a line. A single read here would be a race, and this test runs in the
    /// <b>blocking</b> <c>integration</c> job — a flake there teaches people to
    /// re-run red CI rather than read it.
    /// </remarks>
    [Fact]
    public async Task Every_tailed_resource_has_delivered_log_content()
    {
        string[] tailed = TailedResourceNames();
        tailed.ShouldNotBeEmpty("AspireFixture.TailedResources is empty; this test would prove nothing.");

        DateTimeOffset deadline = DateTimeOffset.UtcNow + DeliveryTimeout;
        List<string> placeholders = PlaceholdersAmong(tailed);

        while (placeholders.Count > 0 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);
            placeholders = PlaceholdersAmong(tailed);
        }

        placeholders.ShouldBeEmpty(
            $"{placeholders.Count} of {tailed.Length} tailed resources still answered with a placeholder "
            + $"after {DeliveryTimeoutSeconds}s instead of log content:{Environment.NewLine}"
            + string.Join(Environment.NewLine, placeholders));
    }

    private List<string> PlaceholdersAmong(string[] tailed)
    {
        List<string> placeholders = [];
        foreach (string resourceName in tailed)
        {
            if (PlaceholderIn(aspire.RecentLogs(resourceName)) is string placeholder)
            {
                placeholders.Add($"{resourceName}: {placeholder}");
            }
        }

        return placeholders;
    }

    /// <summary>
    /// <b>The regression test for #2038: the subscription survives a restart.</b>
    /// Nothing else covers that. A and B both read tails whose process has run
    /// undisturbed since <c>StartAsync</c>, so a tail that dies at the one event
    /// it exists to survive passes both. The token is invented <i>after</i> the
    /// restart, and that is the whole assertion — do not fold this into the other
    /// two.
    /// </summary>
    /// <remarks>
    /// <b>It does not discriminate a resolve-once implementation, despite
    /// looking as though it should.</b> The DCP instance id was observed
    /// <i>stable</i> across a full restart on Aspire 13.5.3 (Windows) — one
    /// <c>event-ingestion-gxkpyqjx</c> through
    /// <c>Running → Stopping → Finished → Starting → Running</c> — so a resolve
    /// hoisted above the fixture's re-subscribe loop passes this test too. The
    /// re-resolution is still the right shape, because id stability is a property
    /// of this DCP build rather than a contract; it is simply defensive code that
    /// no test here exercises, and a green run must not be read as evidence
    /// otherwise. Linux is unverified.
    /// </remarks>
    /// <remarks>
    /// <b>Disruptive</b>, like every other test that restarts a resource through
    /// Aspire: the restart command fails outright on the CI runner ("Failed to
    /// stop resource"), so this is excluded there by category and observed by
    /// hand, exactly as <c>RestartLosesNothingIntegrationTests</c> is.
    /// </remarks>
    [Fact]
    [Trait("Category", "Disruptive")]
    public async Task A_restarted_resource_keeps_delivering_to_its_log_tail()
    {
        await RestartAsync(EventIngestionResource);

        string token = InventedToken("logtail-webhook");

        using HttpClient events = await aspire.CreateAuthenticatedClientAsync(
            EventIngestionResource, MultiFabOperator, OperatorPassword);

        HttpResponseMessage created = await events.PostAsJsonAsync(
            "/webhook-integrations?fabId=munich",
            new { name = token, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(
            HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        string tail = await PollForAsync(EventIngestionResource, token);

        ShouldCarry(EventIngestionResource, token, tail);
    }

    /// <summary>
    /// Delivery has latency — DCP sits between the service's stdout and the
    /// fixture's queue — so this re-reads rather than asserting once, and returns
    /// the last value it read so the failure names the placeholder it got.
    /// </summary>
    private async Task<string> PollForAsync(string resourceName, string token)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + DeliveryTimeout;
        string tail = aspire.RecentLogs(resourceName);

        while (!tail.Contains(token, StringComparison.Ordinal) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval);
            tail = aspire.RecentLogs(resourceName);
        }

        return tail;
    }

    private static void ShouldCarry(string resourceName, string token, string tail)
    {
        tail.StartsWith(NotTailedPrefix, StringComparison.Ordinal).ShouldBeFalse(
            $"'{resourceName}' is not in AspireFixture.TailedResources: {tail}");

        tail.ShouldNotBe(
            SubscribedButSilent,
            $"{resourceName}'s tail is subscribed to a stream that carries nothing.");

        tail.StartsWith(TailFailedPrefix, StringComparison.Ordinal).ShouldBeFalse(
            $"{resourceName}'s tail faulted: {tail}");

        tail.Contains(token, StringComparison.Ordinal).ShouldBeTrue(
            $"{resourceName} logged '{token}', but it never reached the tail within "
            + $"{DeliveryTimeoutSeconds}s. Last value read from RecentLogs:{Environment.NewLine}{tail}");
    }

    private static string? PlaceholderIn(string tail) =>
        tail.StartsWith(NotTailedPrefix, StringComparison.Ordinal)
        || tail.StartsWith(TailFailedPrefix, StringComparison.Ordinal)
        || string.Equals(tail, SubscribedButSilent, StringComparison.Ordinal)
            ? tail
            : null;

    /// <summary>
    /// Reads the fixture's own list rather than a copy of it, so a name added
    /// later is covered without anyone remembering this file. The field is
    /// private and in this assembly; restating the eight names here would be a
    /// second hand-maintained copy, and the first one is what produced #2053.
    /// </summary>
    private static string[] TailedResourceNames()
    {
        FieldInfo? field = typeof(AspireFixture).GetField(
            "TailedResources", BindingFlags.NonPublic | BindingFlags.Static);

        field.ShouldNotBeNull(
            "AspireFixture.TailedResources was renamed; this test reads it by name.");

        return (string[])field.GetValue(null)!;
    }

    /// <summary>
    /// Lowercase letters, digits and hyphens only, so one token is legal both as
    /// a camera name and as a webhook integration name (URL-safe by grammar). A
    /// v7 guid makes it unique on a shared database and impossible to have been
    /// logged before this test ran.
    /// </summary>
    private static string InventedToken(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";

    /// <summary>
    /// Restarts the service, and — whatever happens — leaves it running. Copied
    /// from <c>RestartLosesNothingIntegrationTests</c> rather than reinvented:
    /// the first version there did it without the <c>finally</c>, and when the
    /// restart failed on CI the service stayed down and every EventIngestion
    /// test after it failed with a socket error.
    /// </summary>
    private async Task RestartAsync(string resourceName)
    {
        ResourceCommandService commands =
            aspire.App.Services.GetRequiredService<ResourceCommandService>();

        try
        {
            ExecuteCommandResult result = await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.RestartCommand, CancellationToken.None);
            result.Success.ShouldBeTrue($"could not restart {resourceName}: {result.Message}");
        }
        finally
        {
            // Start is idempotent on a running resource, so this is safe after
            // a restart that worked and is the repair after one that did not.
            await commands.ExecuteCommandAsync(
                resourceName, KnownResourceCommands.StartCommand, CancellationToken.None);

            await WaitForHealthyAsync(resourceName);
        }
    }

    /// <summary>
    /// <c>WaitOnResourceUnavailable</c>, and that is the whole of #2038: a
    /// restart passes <i>through</i> an unavailable state on its way back up, and
    /// the default treats reaching one as a reason to stop waiting.
    /// </summary>
    private async Task WaitForHealthyAsync(string resourceName)
    {
        try
        {
            await aspire.App.ResourceNotifications
                .WaitForResourceHealthyAsync(
                    resourceName, WaitBehavior.WaitOnResourceUnavailable, CancellationToken.None)
                .WaitAsync(TimeSpan.FromMinutes(2));
        }
        catch (Exception exception)
        {
            output.WriteLine($"{resourceName} did not become healthy: {exception.Message}");
            output.WriteLine($"---- {resourceName} snapshot ----");
            output.WriteLine(await aspire.ResourceDiagnosticsAsync(resourceName));

            throw;
        }
    }
}
