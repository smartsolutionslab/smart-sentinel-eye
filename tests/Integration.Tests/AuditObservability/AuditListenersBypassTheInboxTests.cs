using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// ADR-0126. The audit listeners settle each delivery at the broker instead of
/// writing it to the Postgres inbox first, which is where a third of the
/// per-message work went.
///
/// <para>
/// Asserted on the inbox table rather than on latency, because the win is a
/// throughput one and latency on a shared CI runner is not a signal. The
/// failure this catches is the change silently reverting — an endpoint policy,
/// a Wolverine upgrade, or a second <c>ConfigureListeners</c> call replacing
/// the first, which is exactly how the sibling <c>ListenerCount</c> setting was
/// lost once while the code still plainly asked for it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class AuditListenersBypassTheInboxTests(AspireFixture aspire)
{
    private const int Events = 25;
    private static readonly TimeSpan IngestDeadline = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Audited_events_leave_no_rows_in_the_durable_inbox()
    {
        using HttpClient variables = await aspire.CreateAdminClientAsync("system-variables");
        await using AuditObservabilityDbContext context =
            await aspire.CreateAuditObservabilityDbContextAsync();

        // Read before, not "assume zero": the inbox is shared with every other
        // audit queue, and the suite has been running. What must not move is the
        // count *attributable to these events*.
        long before = await InboxRowsAsync(context);

        string name = await DefineAsync(variables);
        string identifier = await SetRepeatedlyAsync(variables, name, Events);

        int landed = await WaitForRowsAsync(context, identifier);
        landed.ShouldBe(Events, "the events must be audited before the inbox can be judged");

        long after = await InboxRowsAsync(context);

        (after - before).ShouldBeLessThan(
            Events,
            $"the inbox grew by {after - before} rows while {Events} events were audited. "
            + "With native broker acks it should not grow at all on their account (ADR-0126); "
            + "growing by roughly one row per event means the listeners are durable again and "
            + "every message is being written to Postgres twice.");
    }

    private static async Task<long> InboxRowsAsync(AuditObservabilityDbContext context)
    {
        List<long> counted = await context.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM wolverine_audit.wolverine_incoming_envelopes")
            .ToListAsync();

        return counted[0];
    }

    private static async Task<string> DefineAsync(HttpClient variables)
    {
        string name = $"ack{Guid.NewGuid():N}"[..16];
        HttpResponseMessage defined = await variables.PostAsJsonAsync("/system-variables", new
        {
            name,
            type = "Number",
            truthyLabel = (string?)null,
            falsyLabel = (string?)null,
        });

        defined.EnsureSuccessStatusCode();
        return name;
    }

    private static async Task<string> SetRepeatedlyAsync(HttpClient variables, string name, int times)
    {
        System.Text.Json.JsonElement read = await ReadAsync(variables, name);
        int version = read.GetProperty("version").GetInt32();

        for (int i = 0; i < times; i++, version++)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Put, $"/system-variables/{name}/value?fabId=munich");
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            request.Content = JsonContent.Create(new { value = (version + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) });

            using HttpResponseMessage response = await variables.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        return (await ReadAsync(variables, name)).GetProperty("variableIdentifier").GetString()!;
    }

    private static async Task<System.Text.Json.JsonElement> ReadAsync(HttpClient variables, string name)
    {
        using HttpResponseMessage read = await variables.GetAsync($"/system-variables/{name}?fabId=munich");
        read.EnsureSuccessStatusCode();
        return await read.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }

    private static async Task<int> WaitForRowsAsync(AuditObservabilityDbContext context, string identifier)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + IngestDeadline;
        int landed = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            List<long> counted = await context.Database
                .SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM audit_events "
                    + "WHERE event_kind = 'SystemVariableValueChangedV1' AND resource_identifier = {0}",
                    identifier)
                .ToListAsync();

            landed = (int)counted[0];
            if (landed >= Events)
            {
                return landed;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        return landed;
    }
}
