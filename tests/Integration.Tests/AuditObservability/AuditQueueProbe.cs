using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.AuditObservability.Infrastructure;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.AuditObservability;

/// <summary>
/// Reads the audit queues' in-flight population straight off RabbitMQ's
/// management API (spec 109 US3).
///
/// <para>
/// <b>The broker, not a log search.</b> ADR-0127 recorded an Aspire
/// structured-log search returning zero hits for events that were demonstrably
/// landing, so the evidence here comes from the broker and from Postgres and
/// from nowhere else.
/// </para>
///
/// <para>
/// <b>A refusal throws naming the address and the status.</b> An unreachable
/// management API and an idle queue are indistinguishable in a number, and the
/// whole reading is built on telling those two apart.
/// </para>
///
/// <para>
/// <b>The read also answers the matched queues' short names.</b> A wrong
/// prefix and an idle broker both produce a count of zero; only the queue
/// names tell a reader which one happened, so the count is never returned
/// without them (spec 178 US1).
/// </para>
/// </summary>
internal static class AuditQueueProbe
{
    /// <summary>
    /// Queues are named <c>{moduleQueuePrefix}.{eventType.FullName}</c>, and the
    /// audit module's prefix is its context name. Read from the module rather
    /// than spelled here: <c>wolverine_audit</c> is the <i>outbox schema</i>, not
    /// the queue prefix, and a filter on it matches nothing and reports a mean of
    /// zero — the very failure this probe exists to refuse.
    /// </summary>
    internal static readonly string QueuePrefix = AuditObservabilityInfrastructureModule.ContextName + ".";

    /// <summary>
    /// The management plugin, with the credentials the AppHost parameterised
    /// rather than guessed: a 401 here reads exactly like "no queues".
    /// </summary>
    internal static async Task<HttpClient> ClientAsync(AspireFixture aspire, CancellationToken cancellationToken)
    {
        Uri management = aspire.App.GetEndpoint("rabbitmq", "management");
        string connection = await aspire.App.GetConnectionStringAsync("rabbitmq", cancellationToken) ?? "";
        string userInfo = new Uri(connection).UserInfo;

        HttpClient client = new() { BaseAddress = management };
        client.DefaultRequestHeaders.Authorization = new(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(userInfo))));

        return client;
    }

    /// <summary>
    /// Sums <c>messages_unacknowledged</c> across the audit queues, and answers
    /// the matched queues' short names alongside the count.
    ///
    /// <para>
    /// A refusal throws naming the address and the status. Answering zero would
    /// be indistinguishable from an idle broker, and the whole reading is built
    /// on telling those two apart.
    /// </para>
    /// </summary>
    internal static async Task<(int Unacknowledged, string[] Queues)> ReadAsync(
        HttpClient broker, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await broker.GetAsync("/api/queues", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"the RabbitMQ management API at {broker.BaseAddress}api/queues answered "
                + $"{(int)response.StatusCode} {response.ReasonPhrase}. A sampler that reported zero "
                + "here would be reporting an unreachable broker as an empty queue.");
        }

        JsonElement payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        int unacknowledged = 0;
        List<string> queues = [];
        foreach (JsonElement queue in payload.EnumerateArray())
        {
            string name = queue.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith(QueuePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            queues.Add(name[(name.LastIndexOf('.') + 1)..]);
            if (queue.TryGetProperty("messages_unacknowledged", out JsonElement outstanding))
            {
                unacknowledged += outstanding.GetInt32();
            }
        }

        return (unacknowledged, [.. queues.Order()]);
    }

    /// <summary>Sums <c>messages_unacknowledged</c> across the audit queues.</summary>
    internal static async Task<int> UnacknowledgedAsync(HttpClient broker, CancellationToken cancellationToken)
    {
        (int unacknowledged, _) = await ReadAsync(broker, cancellationToken);
        return unacknowledged;
    }

    /// <summary>
    /// Polls until the audit queues report nothing unacknowledged, so a run's
    /// sample window contains that run's population and nobody else's, and
    /// answers what it last saw together with the queue names it was reading.
    /// </summary>
    internal static async Task<(int Unacknowledged, string[] Queues)> WaitForQuiescenceReadingAsync(
        HttpClient broker, TimeSpan deadline, CancellationToken cancellationToken)
    {
        DateTimeOffset until = DateTimeOffset.UtcNow + deadline;
        (int unacknowledged, string[] queues) = await ReadAsync(broker, cancellationToken);

        while (unacknowledged > 0 && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            (unacknowledged, queues) = await ReadAsync(broker, cancellationToken);
        }

        return (unacknowledged, queues);
    }

    /// <summary>
    /// Polls until the audit queues report nothing unacknowledged, so a run's
    /// sample window contains that run's population and nobody else's.
    /// </summary>
    internal static async Task<int> WaitForQuiescenceAsync(
        HttpClient broker, TimeSpan deadline, CancellationToken cancellationToken)
    {
        (int unacknowledged, _) = await WaitForQuiescenceReadingAsync(broker, deadline, cancellationToken);
        return unacknowledged;
    }
}
